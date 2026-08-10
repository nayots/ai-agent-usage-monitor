using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AiUsageMonitor.Poc.Domain;
using AiUsageMonitor.Poc.Providers.Claude;
using AiUsageMonitor.Poc.Providers.Codex;

Console.OutputEncoding = Encoding.UTF8;

try
{
    PrintHeader();

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    CancellationToken ct = cts.Token;

    IProviderProbe[] probes = [new CodexProbe(), new ClaudeOAuthUsageProbe()];
    Task<(ProviderSnapshot Snapshot, double ElapsedMs)>[] tasks =
        probes.Select(p => RunTimedAsync(p, ct)).ToArray();

    (ProviderSnapshot Snapshot, double ElapsedMs)[] results = await Task.WhenAll(tasks).ConfigureAwait(false);

    foreach ((ProviderSnapshot snapshot, double elapsedMs) in results)
    {
        PrintProviderReport(snapshot, elapsedMs);
    }

    PrintDuckTypedSelfTest();
    PrintClaudeFixtureTest();

    Console.WriteLine();
    Console.WriteLine(new string('=', 78));
    Console.WriteLine("Done.");
}
catch (Exception ex)
{
    // This is a diagnostic tool - an unexpected failure is itself a finding, not a crash.
    Console.WriteLine();
    Console.WriteLine($"UNEXPECTED FAILURE: {ex.GetType().Name}: {ex.Message}");
}

return 0;

// ---------------------------------------------------------------------------------------------
// Orchestration
// ---------------------------------------------------------------------------------------------

static async Task<(ProviderSnapshot Snapshot, double ElapsedMs)> RunTimedAsync(IProviderProbe probe, CancellationToken ct)
{
    Stopwatch sw = Stopwatch.StartNew();
    try
    {
        ProviderSnapshot snapshot = await probe.ProbeAsync(ct).ConfigureAwait(false);
        sw.Stop();
        return (snapshot, sw.Elapsed.TotalMilliseconds);
    }
    catch (Exception ex)
    {
        // Each probe is expected to catch its own failures and return an Error snapshot; this is a
        // last-resort backstop so one misbehaving probe can never take down the whole report.
        sw.Stop();
        var crash = new ProviderSnapshot(
            ProviderName: probe.Name,
            Installed: false,
            Version: null,
            ExecutablePath: null,
            State: ConnectionState.Error,
            Mechanism: "n/a",
            UpdateModel: "unavailable",
            Windows: [],
            RetrievedAt: null,
            Error: $"Unhandled exception escaped the probe: {ex.GetType().Name}: {ex.Message}",
            Notes: []);
        return (crash, sw.Elapsed.TotalMilliseconds);
    }
}

// ---------------------------------------------------------------------------------------------
// Printing
// ---------------------------------------------------------------------------------------------

static void PrintHeader()
{
    Console.WriteLine("AI Agent Usage Monitor - POC diagnostic report");
    Console.WriteLine($"Generated at (UTC): {DateTimeOffset.UtcNow:u}");
    Console.WriteLine();
    Console.WriteLine("Mechanism provenance:");
    Console.WriteLine("  Codex       - OFFICIAL: codex app-server JSON-RPC over stdio, account/rateLimits/read (pull/poll).");
    Console.WriteLine("  Claude Code - UNOFFICIAL: undocumented OAuth usage endpoint (api.anthropic.com/api/oauth/usage),");
    Console.WriteLine("                authenticated with the local claude.ai OAuth token (pull/poll). This is the ONLY");
    Console.WriteLine("                mechanism per PRD ss4.1.1/ss11 - the statusLine JSON contract was evaluated and");
    Console.WriteLine("                rejected (push-only, needs config modification, stale when idle); there is no");
    Console.WriteLine("                official fallback. This endpoint is undocumented, carries no stability guarantee,");
    Console.WriteLine("                and may break without notice; it fails safe into an explicit state if it does.");
    Console.WriteLine();
    Console.WriteLine("Never printed: raw tokens, credentials, or full raw payloads - field names/shapes only.");
}

static void PrintProviderReport(ProviderSnapshot s, double elapsedMs)
{
    Console.WriteLine();
    Console.WriteLine(new string('=', 78));
    Console.WriteLine(s.ProviderName);
    Console.WriteLine(new string('=', 78));
    Console.WriteLine($"  Installed     : {s.Installed}");
    Console.WriteLine($"  Version       : {s.Version ?? "(unknown)"}");
    Console.WriteLine($"  Executable    : {s.ExecutablePath ?? "(not found)"}");
    Console.WriteLine($"  State         : {s.State}");
    Console.WriteLine($"  Mechanism     : {s.Mechanism}");
    Console.WriteLine($"  Update model  : {s.UpdateModel ?? "(unknown)"}");
    Console.WriteLine($"  Retrieved at  : {(s.RetrievedAt is { } r ? r.ToLocalTime().ToString("u") : "(never)")}");
    Console.WriteLine($"  Probe latency : {elapsedMs:0} ms");

    if (s.Error is not null)
    {
        Console.WriteLine($"  Error         : {s.Error}");
    }

    if (s.Windows.Count == 0)
    {
        Console.WriteLine("  Quota windows : (none)");
    }
    else
    {
        Console.WriteLine("  Quota windows:");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (QuotaWindow w in s.Windows.OrderBy(w => w.Order))
        {
            PrintWindow(w, now);
        }
    }

    if (s.Notes.Count > 0)
    {
        Console.WriteLine("  Notes:");
        foreach (string note in s.Notes)
        {
            Console.WriteLine($"    - {note}");
        }
    }
}

static void PrintWindow(QuotaWindow w, DateTimeOffset now)
{
    string used = w.UsedPercent is { } u ? $"{u:0.#}% used" : "used: unknown";
    string remaining = w.RemainingPercent is { } rem ? $"{rem:0.#}% remaining" : "remaining: unknown";
    TimeSpan? until = w.TimeUntilReset(now);
    string resetCountdown = until is { } t ? FormatDuration(t) : "unknown";
    string duration = w.WindowDuration is { } d ? FormatDuration(d) : "unknown";
    string bar = BuildProgressBar(w.UsedPercent);
    double? elapsedFraction = w.ElapsedFraction(now);
    string elapsedMarker = elapsedFraction is { } e ? $"{e * 100:0}%" : "—"; // em dash when not computable

    Console.WriteLine($"    [{w.Order}] {w.Label}  (id={w.Id}){(w.IsPartial ? "  [partial]" : string.Empty)}");
    Console.WriteLine($"        {used} / {remaining}");
    Console.WriteLine($"        bar: {bar}");
    Console.WriteLine($"        resets in: {resetCountdown}   window: {duration}   elapsed marker: {elapsedMarker}");
    if (w.Extra.Count > 0)
    {
        string extraStr = string.Join(", ", w.Extra.Select(kv => $"{kv.Key}={kv.Value}"));
        Console.WriteLine($"        extra: {extraStr}");
    }
}

static string FormatDuration(TimeSpan t)
{
    if (t < TimeSpan.Zero)
    {
        t = TimeSpan.Zero;
    }

    var parts = new List<string>();
    if (t.Days > 0)
    {
        parts.Add($"{t.Days}d");
    }

    if (t.Hours > 0 || t.Days > 0)
    {
        parts.Add($"{t.Hours}h");
    }

    parts.Add($"{t.Minutes}m");
    return string.Join(' ', parts);
}

static string BuildProgressBar(double? usedPercent, int width = 20)
{
    if (usedPercent is not { } u)
    {
        return "[" + new string('?', width) + "] unknown";
    }

    double clamped = Math.Clamp(u, 0, 100);
    int filled = (int)Math.Round(clamped / 100.0 * width);
    return "[" + new string('#', filled) + new string('-', width - filled) + $"] {clamped:0}%";
}

// ---------------------------------------------------------------------------------------------
// Self-tests (not live data)
// ---------------------------------------------------------------------------------------------

static void PrintDuckTypedSelfTest()
{
    Console.WriteLine();
    Console.WriteLine(new string('=', 78));
    Console.WriteLine("SELF-TEST (inline sample, not live data): DuckTypedQuotaExtractor");
    Console.WriteLine(new string('=', 78));
    Console.WriteLine("Proves new/unknown provider window names survive without any code change.");

    const string sampleJson =
        """
        {
          "rateLimits": {
            "five_hour": { "used_percent": 42.5, "resets_at": 1786800000 },
            "seven_day": { "utilization": 73.1, "reset": "2026-08-17T12:00:00Z" },
            "seven_day_opus": { "usedPercent": 12.0, "resetsAt": 1786900000, "windowDurationMins": 10080 },
            "three_hour_nimbus": { "percent_used": 5.5, "reset_at": 1786700000 }
          },
          "meta": { "note": "self-test sample, deliberately not a real provider shape" }
        }
        """;

    using JsonDocument doc = JsonDocument.Parse(sampleJson);
    IReadOnlyList<QuotaWindow> windows = DuckTypedQuotaExtractor.Extract(doc.RootElement);

    string[] expectedIds = ["five_hour", "seven_day", "seven_day_opus", "three_hour_nimbus"];
    bool allExpectedFound = expectedIds.All(id => windows.Any(w => w.Id == id));
    bool countMatches = windows.Count == expectedIds.Length;

    Console.WriteLine($"  Windows discovered : {windows.Count} (expected {expectedIds.Length})");
    Console.WriteLine($"  All four present, including the unknown 'three_hour_nimbus': {allExpectedFound}");
    Console.WriteLine($"  PASS: {allExpectedFound && countMatches}");
    Console.WriteLine();

    DateTimeOffset now = DateTimeOffset.UtcNow;
    foreach (QuotaWindow w in windows.OrderBy(w => w.Order))
    {
        PrintWindow(w, now);
    }
}

static void PrintClaudeFixtureTest()
{
    Console.WriteLine();
    Console.WriteLine(new string('=', 78));
    Console.WriteLine("PARSER REGRESSION TEST (recorded sample): Claude statusLine JSON -> DuckTypedQuotaExtractor");
    Console.WriteLine(new string('=', 78));
    Console.WriteLine("The sample originates from the statusLine contract, which is no longer a supported");
    Console.WriteLine("mechanism (see PRD ss11.1) - it is retained solely as parser test data.");
    Console.WriteLine("Proves the shared duck-typed parser still handles this dialect correctly, and that");
    Console.WriteLine("context_window's used_percentage (no reset field) is correctly excluded.");

    using JsonDocument doc = JsonDocument.Parse(ClaudeFixtures.StatusLineSampleJson);
    IReadOnlyList<QuotaWindow> windows = DuckTypedQuotaExtractor.Extract(doc.RootElement);

    bool exactlyTwo = windows.Count == 2;
    bool hasFiveHour = windows.Any(w => w.Id == "five_hour");
    bool hasSevenDay = windows.Any(w => w.Id == "seven_day");
    bool noContextWindow = windows.All(w => w.Id != "context_window");
    bool pass = exactlyTwo && hasFiveHour && hasSevenDay && noContextWindow;

    Console.WriteLine($"  Windows discovered : {windows.Count} (expected exactly 2)");
    Console.WriteLine($"  five_hour present: {hasFiveHour}, seven_day present: {hasSevenDay}");
    Console.WriteLine($"  context_window correctly excluded (has used_percentage but no reset-ish field): {noContextWindow}");
    Console.WriteLine($"  PASS: {pass}");
    Console.WriteLine();

    DateTimeOffset now = DateTimeOffset.UtcNow;
    foreach (QuotaWindow w in windows.OrderBy(w => w.Order))
    {
        PrintWindow(w, now);
    }
}
