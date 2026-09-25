using System.Text;

namespace AiUsageMonitor.Infrastructure.Providers.Claude;

public sealed record ClaudeUsagePeriod(long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens, decimal CostUsd, long UnpricedTokens)
{
    public long TotalTokens => InputTokens + OutputTokens + CacheReadTokens + CacheWriteTokens;
    public bool HasUnpricedTokens => UnpricedTokens > 0;
}

public sealed record ClaudeUsageTotals(ClaudeUsagePeriod Today, ClaudeUsagePeriod Month, DateTimeOffset TodayEndsAt, DateTimeOffset MonthEndsAt);

public sealed record ClaudeLedgerRefresh(int FilesRead, int FilesUnreadable, int NewReplies, bool ProjectsDirectoryFound);

public sealed class ClaudeTranscriptLedger
{
    private sealed class Bucket
    {
        public long InputTokens;
        public long OutputTokens;
        public long CacheReadTokens;
        public long CacheWriteTokens;
        public decimal CostUsd;
        public long UnpricedTokens;
    }

    private const int ChunkSize = 1024 * 1024;

    private static ReadOnlySpan<byte> UsageMarker => "\"usage\""u8;

    private readonly string _projectsDirectory;
    private readonly TimeZoneInfo _timeZone;
    private readonly object _gate = new();
    private readonly Dictionary<string, long> _offsets = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dedupKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<DateOnly, Bucket> _buckets = [];
    private long _bytesRead;

    public ClaudeTranscriptLedger(string projectsDirectory, TimeZoneInfo? timeZone = null)
    {
        _projectsDirectory = projectsDirectory;
        _timeZone = timeZone ?? TimeZoneInfo.Local;
    }

    public long BytesRead
    {
        get { lock (_gate) { return _bytesRead; } }
    }

    public ClaudeLedgerRefresh Refresh(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!Directory.Exists(_projectsDirectory))
            {
                return new ClaudeLedgerRefresh(0, 0, 0, false);
            }

            DateOnly monthStart = LocalDate(now).AddDays(1 - LocalDate(now).Day);
            DateTime monthStartLocal = monthStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            int filesRead = 0;
            int filesUnreadable = 0;
            int newReplies = 0;

            foreach (string file in Directory.EnumerateFiles(_projectsDirectory, "*.jsonl", SearchOption.AllDirectories))
            {
                try
                {
                    FileInfo info = new(file);
                    if (TimeZoneInfo.ConvertTime(new DateTimeOffset(info.LastWriteTimeUtc), _timeZone).DateTime < monthStartLocal)
                    {
                        continue;
                    }

                    long offset = _offsets.GetValueOrDefault(file);
                    if (info.Length < offset)
                    {
                        offset = 0;
                    }

                    if (info.Length == offset)
                    {
                        continue;
                    }

                    filesRead++;
                    long completeLength = ReadCompleteLines(file, offset, monthStart, ref newReplies);
                    _offsets[file] = offset + completeLength;
                    _bytesRead += completeLength;
                }
                catch (IOException)
                {
                    filesUnreadable++;
                }
                catch (UnauthorizedAccessException)
                {
                    filesUnreadable++;
                }
            }

            foreach (DateOnly date in _buckets.Keys.Where(date => date < monthStart).ToArray())
            {
                _buckets.Remove(date);
            }

            return new ClaudeLedgerRefresh(filesRead, filesUnreadable, newReplies, true);
        }
    }

    public ClaudeUsageTotals Totals(DateTimeOffset now)
    {
        lock (_gate)
        {
            DateOnly today = LocalDate(now);
            DateOnly monthStart = today.AddDays(1 - today.Day);
            ClaudeUsagePeriod todayPeriod = Period(_buckets.GetValueOrDefault(today));
            var month = new Bucket();
            foreach ((DateOnly date, Bucket bucket) in _buckets.Where(pair => pair.Key >= monthStart))
            {
                month.InputTokens += bucket.InputTokens;
                month.OutputTokens += bucket.OutputTokens;
                month.CacheReadTokens += bucket.CacheReadTokens;
                month.CacheWriteTokens += bucket.CacheWriteTokens;
                month.CostUsd += bucket.CostUsd;
                month.UnpricedTokens += bucket.UnpricedTokens;
            }

            DateOnly nextMonth = monthStart.AddMonths(1);
            return new ClaudeUsageTotals(todayPeriod, Period(month), AtLocalMidnight(today.AddDays(1)), AtLocalMidnight(nextMonth));
        }
    }

    /// <summary>
    /// Reads from <paramref name="offset"/> to the end of the file in fixed-size chunks, handing
    /// each complete line to the parser, and returns how many bytes of complete lines were
    /// consumed. A trailing partial line is left for the next refresh.
    /// <para>
    /// Chunked on purpose: a single transcript measured 51 MB, and reading it whole cost a buffer
    /// of that size plus a string twice as large on the first scan - in a tray widget. Only a line
    /// containing <c>"usage"</c> is decoded at all; every other line (prompts, tool output) stays
    /// raw bytes and is never turned into a string.
    /// </para>
    /// </summary>
    private long ReadCompleteLines(string file, long offset, DateOnly monthStart, ref int newReplies)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, ChunkSize, FileOptions.SequentialScan);
        stream.Seek(offset, SeekOrigin.Begin);

        byte[] chunk = new byte[ChunkSize];
        using var carry = new MemoryStream();
        long consumedBefore = 0;
        long completeLength = 0;
        int read;

        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            ReadOnlySpan<byte> remaining = chunk.AsSpan(0, read);
            int position = 0;

            while (true)
            {
                int newline = remaining.IndexOf((byte)'\n');
                if (newline < 0)
                {
                    carry.Write(remaining);
                    break;
                }

                if (carry.Length > 0)
                {
                    carry.Write(remaining[..newline]);
                    Consume(carry.GetBuffer().AsSpan(0, (int)carry.Length), monthStart, ref newReplies);
                    carry.SetLength(0);
                }
                else
                {
                    Consume(remaining[..newline], monthStart, ref newReplies);
                }

                position += newline + 1;
                completeLength = consumedBefore + position;
                remaining = remaining[(newline + 1)..];
            }

            consumedBefore += read;
        }

        return completeLength;
    }

    private void Consume(ReadOnlySpan<byte> line, DateOnly monthStart, ref int newReplies)
    {
        if (line.IndexOf(UsageMarker) < 0)
        {
            return;
        }

        ClaudeUsageEntry? entry = ClaudeTranscriptLine.TryParse(Encoding.UTF8.GetString(line).TrimEnd('\r'));
        if (entry is not null && _dedupKeys.Add(entry.DedupKey))
        {
            DateOnly date = LocalDate(entry.Timestamp);
            if (date >= monthStart)
            {
                Add(date, entry);
                newReplies++;
            }
        }
    }

    private void Add(DateOnly date, ClaudeUsageEntry entry)
    {
        Bucket bucket = _buckets.GetValueOrDefault(date) ?? (_buckets[date] = new Bucket());
        bucket.InputTokens += entry.InputTokens;
        bucket.OutputTokens += entry.OutputTokens;
        bucket.CacheReadTokens += entry.CacheReadTokens;
        bucket.CacheWriteTokens += entry.CacheWrite5mTokens + entry.CacheWrite1hTokens;
        if (ClaudeApiPricing.CostUsd(entry) is decimal cost)
        {
            bucket.CostUsd += cost;
        }
        else
        {
            bucket.UnpricedTokens += entry.TotalTokens;
        }
    }

    private DateOnly LocalDate(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, _timeZone).DateTime);

    private DateTimeOffset AtLocalMidnight(DateOnly date)
    {
        DateTime local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, _timeZone.GetUtcOffset(local));
    }

    private static ClaudeUsagePeriod Period(Bucket? bucket) => bucket is null
        ? new ClaudeUsagePeriod(0, 0, 0, 0, 0m, 0)
        : new ClaudeUsagePeriod(bucket.InputTokens, bucket.OutputTokens, bucket.CacheReadTokens, bucket.CacheWriteTokens, bucket.CostUsd, bucket.UnpricedTokens);
}
