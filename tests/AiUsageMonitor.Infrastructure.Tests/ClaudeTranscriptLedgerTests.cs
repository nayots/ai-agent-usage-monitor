using System.Text;
using AiUsageMonitor.Infrastructure.Providers.Claude;

namespace AiUsageMonitor.Infrastructure.Tests;

public sealed class ClaudeTranscriptLedgerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DuplicateRepliesAreCountedOnce()
    {
        using var directory = new TempDirectory();
        string line = Line("one", 10);
        File.WriteAllText(directory.File("a.jsonl"), line + "\n" + line + "\n" + line + "\n");
        var ledger = new ClaudeTranscriptLedger(directory.File("."), TimeZoneInfo.Utc);

        ledger.Refresh(Now);

        Assert.Equal(10, ledger.Totals(Now).Today.InputTokens);
    }

    [Fact]
    public void DifferentRepliesAreSummed()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(directory.File("a.jsonl"), Line("one", 10) + "\n" + Line("two", 20) + "\n");
        var ledger = new ClaudeTranscriptLedger(directory.File("."), TimeZoneInfo.Utc);

        ledger.Refresh(Now);

        Assert.Equal(30, ledger.Totals(Now).Today.InputTokens);
    }

    [Fact]
    public void LaterRefreshReadsOnlyAppendedCompleteBytes()
    {
        using var directory = new TempDirectory();
        string file = directory.File("a.jsonl");
        File.WriteAllText(file, Line("one", 10) + "\n");
        var ledger = new ClaudeTranscriptLedger(directory.File("."), TimeZoneInfo.Utc);
        ledger.Refresh(Now);
        long before = ledger.BytesRead;
        string appended = Line("two", 20) + "\n";
        File.AppendAllText(file, appended);

        ledger.Refresh(Now);

        Assert.Equal(Encoding.UTF8.GetByteCount(appended), ledger.BytesRead - before);
        Assert.Equal(30, ledger.Totals(Now).Today.InputTokens);
    }

    [Fact]
    public void PartialLinesWaitForTheirNewlineWithoutDoubleCounting()
    {
        using var directory = new TempDirectory();
        string file = directory.File("a.jsonl");
        string second = Line("two", 20);
        File.WriteAllText(file, Line("one", 10) + "\n" + second[..(second.Length / 2)]);
        var ledger = new ClaudeTranscriptLedger(directory.File("."), TimeZoneInfo.Utc);
        ledger.Refresh(Now);
        File.AppendAllText(file, second[(second.Length / 2)..] + "\n");

        ledger.Refresh(Now);

        Assert.Equal(30, ledger.Totals(Now).Today.InputTokens);
    }

    [Fact]
    public void ReplacedFilesDoNotDoubleCountSeenReplies()
    {
        using var directory = new TempDirectory();
        string file = directory.File("a.jsonl");
        File.WriteAllText(file, Line("one", 10) + "\n" + Line("long", 100) + "\n");
        var ledger = new ClaudeTranscriptLedger(directory.File("."), TimeZoneInfo.Utc);
        ledger.Refresh(Now);
        File.WriteAllText(file, Line("one", 10) + "\n" + Line("two", 20) + "\n");

        ledger.Refresh(Now);

        Assert.Equal(130, ledger.Totals(Now).Today.InputTokens);
    }

    [Fact]
    public void OldFilesAreNotRead()
    {
        using var directory = new TempDirectory();
        string file = directory.File("a.jsonl");
        File.WriteAllText(file, Line("one", 10) + "\n");
        File.SetLastWriteTimeUtc(file, new DateTime(2026, 8, 31, 23, 59, 0, DateTimeKind.Utc));
        var ledger = new ClaudeTranscriptLedger(directory.File("."), TimeZoneInfo.Utc);

        ClaudeLedgerRefresh refresh = ledger.Refresh(Now);

        Assert.Equal(0, ledger.BytesRead);
        Assert.Equal(0, refresh.FilesRead);
    }

    [Fact]
    public void DayAndMonthTotalsUseReplyLocalDates()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(directory.File("a.jsonl"), Line("yesterday", 10, "2026-09-24T09:00:00Z") + "\n" + Line("today", 20) + "\n" + Line("old", 30, "2026-08-31T09:00:00Z") + "\n");
        var ledger = new ClaudeTranscriptLedger(directory.File("."), TimeZoneInfo.Utc);
        ledger.Refresh(Now);

        ClaudeUsageTotals totals = ledger.Totals(Now);
        Assert.Equal(20, totals.Today.InputTokens);
        Assert.Equal(30, totals.Month.InputTokens);
    }

    [Fact]
    public void TimeZoneAssignsRepliesAndResetInstantsLocally()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(directory.File("a.jsonl"), Line("one", 10, "2026-09-24T22:30:00Z") + "\n");
        TimeZoneInfo zone = TimeZoneInfo.CreateCustomTimeZone("plus-three", TimeSpan.FromHours(3), "plus-three", "plus-three");
        var ledger = new ClaudeTranscriptLedger(directory.File("."), zone);
        ledger.Refresh(Now);

        ClaudeUsageTotals totals = ledger.Totals(Now);
        Assert.Equal(10, totals.Today.InputTokens);
        Assert.Equal(new DateTimeOffset(2026, 9, 25, 21, 0, 0, TimeSpan.Zero), totals.TodayEndsAt);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 21, 0, 0, TimeSpan.Zero), totals.MonthEndsAt);
    }

    [Fact]
    public void UnknownModelsContributeOnlyToUnpricedTokens()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(directory.File("a.jsonl"), Line("one", 10, model: "claude-future-9") + "\n");
        var ledger = new ClaudeTranscriptLedger(directory.File("."), TimeZoneInfo.Utc);
        ledger.Refresh(Now);

        ClaudeUsagePeriod today = ledger.Totals(Now).Today;
        Assert.Equal(10, today.UnpricedTokens);
        Assert.Equal(0m, today.CostUsd);
        Assert.True(today.HasUnpricedTokens);
    }

    [Fact]
    public void MissingProjectsDirectoryIsAZeroResult()
    {
        using var directory = new TempDirectory();
        var ledger = new ClaudeTranscriptLedger(directory.File("missing"), TimeZoneInfo.Utc);

        ClaudeLedgerRefresh refresh = ledger.Refresh(Now);

        Assert.False(refresh.ProjectsDirectoryFound);
        Assert.Equal(0, ledger.Totals(Now).Month.TotalTokens);
    }

    [Fact]
    public void SidechainRepliesAreCountedLikeOtherReplies()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(directory.File("a.jsonl"), Line("one", 10).Replace("\"usage\":{", "\"isSidechain\":true,\"usage\":{") + "\n");
        var ledger = new ClaudeTranscriptLedger(directory.File("."), TimeZoneInfo.Utc);

        ledger.Refresh(Now);

        Assert.Equal(10, ledger.Totals(Now).Today.InputTokens);
    }

    [Fact]
    public void AnUnreadableFileDoesNotPreventOtherFilesFromBeingCounted()
    {
        using var directory = new TempDirectory();
        string locked = directory.File("locked.jsonl");
        File.WriteAllText(locked, Line("locked", 10) + "\n");
        File.WriteAllText(directory.File("readable.jsonl"), Line("readable", 20) + "\n");
        using var lockStream = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var ledger = new ClaudeTranscriptLedger(directory.File("."), TimeZoneInfo.Utc);

        ClaudeLedgerRefresh refresh = ledger.Refresh(Now);

        Assert.Equal(1, refresh.FilesUnreadable);
        Assert.Equal(20, ledger.Totals(Now).Today.InputTokens);
    }

    /// <summary>
    /// Real transcripts run to tens of megabytes, so they are read in fixed-size chunks rather than
    /// whole. A reply whose line straddles a chunk boundary must still be counted exactly once.
    /// </summary>
    [Fact]
    public void RepliesStraddlingReadBufferBoundariesAreCountedOnce()
    {
        using var directory = new TempDirectory();
        var content = new StringBuilder();
        string padding = "{\"type\":\"user\",\"text\":\"" + new string('x', 997) + "\"}";
        long expected = 0;
        for (int i = 0; i < 3000; i++)
        {
            content.Append(padding).Append('\n');
            if (i % 7 == 0)
            {
                content.Append(Line($"reply-{i}", i + 1)).Append('\n');
                expected += i + 1;
            }
        }

        File.WriteAllText(directory.File("big.jsonl"), content.ToString());
        var ledger = new ClaudeTranscriptLedger(directory.File("."), TimeZoneInfo.Utc);

        ledger.Refresh(Now);

        Assert.Equal(expected, ledger.Totals(Now).Today.InputTokens);
        Assert.Equal(Encoding.UTF8.GetByteCount(content.ToString()), ledger.BytesRead);
    }

    /// <summary>A single line longer than the read buffer - a large tool result - is carried whole.</summary>
    [Fact]
    public void ALineLongerThanTheReadBufferIsHandled()
    {
        using var directory = new TempDirectory();
        string huge = "{\"type\":\"user\",\"text\":\"" + new string('y', 3_000_000) + "\"}";
        File.WriteAllText(directory.File("a.jsonl"), huge + "\n" + Line("after", 42) + "\n");
        var ledger = new ClaudeTranscriptLedger(directory.File("."), TimeZoneInfo.Utc);

        ledger.Refresh(Now);

        Assert.Equal(42, ledger.Totals(Now).Today.InputTokens);
    }

    private static string Line(string id, long input, string timestamp = "2026-09-25T09:00:00Z", string model = "claude-opus-5") =>
        $"{{\"type\":\"assistant\",\"requestId\":\"request-{id}\",\"timestamp\":\"{timestamp}\",\"message\":{{\"id\":\"message-{id}\",\"model\":\"{model}\",\"usage\":{{\"input_tokens\":{input}}}}}}}";
}
