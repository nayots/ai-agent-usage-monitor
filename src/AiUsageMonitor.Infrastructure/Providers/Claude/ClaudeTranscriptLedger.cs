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
                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    stream.Seek(offset, SeekOrigin.Begin);
                    byte[] bytes = new byte[stream.Length - offset];
                    _ = stream.Read(bytes, 0, bytes.Length);
                    int completeLength = Array.LastIndexOf(bytes, (byte)'\n') + 1;
                    if (completeLength == 0)
                    {
                        continue;
                    }

                    string completed = Encoding.UTF8.GetString(bytes, 0, completeLength);
                    foreach (string line in completed.Split('\n'))
                    {
                        ClaudeUsageEntry? entry = ClaudeTranscriptLine.TryParse(line.TrimEnd('\r'));
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
