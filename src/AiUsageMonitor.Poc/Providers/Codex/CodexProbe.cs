using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AiUsageMonitor.Domain;

namespace AiUsageMonitor.Poc.Providers.Codex;

/// <summary>
/// Codex provider probe. Fully proven mechanism: discovers the local codex.exe, reads its version,
/// then launches "codex app-server" and speaks a small slice of its newline-delimited JSON-RPC
/// protocol over stdio to read live rate limits. No HTTP, no credential files - everything here goes
/// through the same local executable a developer would run themselves.
/// </summary>
public sealed class CodexProbe : IProviderProbe
{
    public string Name => "Codex";

    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RateLimitsTimeout = TimeSpan.FromSeconds(10);

    private const string Mechanism = "codex app-server (JSON-RPC over stdio, JSONL) - account/rateLimits/read";

    public async Task<ProviderSnapshot> ProbeAsync(CancellationToken ct)
    {
        string? exePath = DiscoverExecutable();

        if (exePath is null)
        {
            return new ProviderSnapshot(
                ProviderName: Name,
                Installed: false,
                Version: null,
                ExecutablePath: null,
                State: ConnectionState.NotInstalled,
                Mechanism: "no local codex.exe found",
                UpdateModel: "unavailable",
                Windows: [],
                RetrievedAt: null,
                Error: null,
                Notes:
                [
                    "Checked (in order): %APPDATA%\\npm\\node_modules\\@openai\\codex\\node_modules\\@openai\\codex-win32-x64\\vendor\\x86_64-pc-windows-msvc\\bin\\codex.exe; " +
                    "a glob over codex-win32-*\\vendor\\*\\bin\\codex.exe; codex.cmd on PATH."
                ]);
        }

        var notes = new List<string>();
        string? version = await TryGetVersionAsync(exePath, ct, notes).ConfigureAwait(false);

        try
        {
            (IReadOnlyList<QuotaWindow> windows, List<string> protocolNotes) =
                await ReadRateLimitsAsync(exePath, ct).ConfigureAwait(false);
            notes.AddRange(protocolNotes);
            notes.Add(
                "account/rateLimits/updated notifications were verified to NOT arrive while idle - they only fire during " +
                "an active model turn, which this probe deliberately never starts. Update model is therefore pull (poll).");

            return new ProviderSnapshot(
                ProviderName: Name,
                Installed: true,
                Version: version,
                ExecutablePath: exePath,
                State: ConnectionState.Connected,
                Mechanism: Mechanism,
                UpdateModel: "pull (poll)",
                Windows: windows,
                RetrievedAt: DateTimeOffset.UtcNow,
                Error: null,
                Notes: notes);
        }
        catch (OperationCanceledException)
        {
            return new ProviderSnapshot(
                ProviderName: Name,
                Installed: true,
                Version: version,
                ExecutablePath: exePath,
                State: ConnectionState.Error,
                Mechanism: Mechanism,
                UpdateModel: "pull (poll)",
                Windows: [],
                RetrievedAt: null,
                Error: $"Timed out after {RateLimitsTimeout.TotalSeconds:0}s waiting for the account/rateLimits/read response.",
                Notes: notes);
        }
        catch (Exception ex)
        {
            return new ProviderSnapshot(
                ProviderName: Name,
                Installed: true,
                Version: version,
                ExecutablePath: exePath,
                State: ConnectionState.Error,
                Mechanism: Mechanism,
                UpdateModel: "pull (poll)",
                Windows: [],
                RetrievedAt: null,
                Error: ex.Message,
                Notes: notes);
        }
    }

    // ----- Executable discovery -----------------------------------------------------------------

    private static string? DiscoverExecutable()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        string primary = Path.Combine(
            appData, "npm", "node_modules", "@openai", "codex", "node_modules", "@openai",
            "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc", "bin", "codex.exe");
        if (File.Exists(primary))
        {
            return primary;
        }

        string searchRoot = Path.Combine(appData, "npm", "node_modules", "@openai", "codex", "node_modules", "@openai");
        if (Directory.Exists(searchRoot))
        {
            foreach (string archDir in SafeEnumerateDirectories(searchRoot, "codex-win32-*"))
            {
                string vendorDir = Path.Combine(archDir, "vendor");
                if (!Directory.Exists(vendorDir))
                {
                    continue;
                }

                foreach (string tripleDir in SafeEnumerateDirectories(vendorDir, "*"))
                {
                    string candidate = Path.Combine(tripleDir, "bin", "codex.exe");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        return FindOnPath("codex.cmd");
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string path, string pattern)
    {
        try
        {
            return Directory.EnumerateDirectories(path, pattern);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string? FindOnPath(string fileName)
    {
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }

        foreach (string dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                continue;
            }

            string candidate = Path.Combine(dir.Trim(), fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    // ----- Version ---------------------------------------------------------------------------------

    private static async Task<string?> TryGetVersionAsync(string exePath, CancellationToken ct, List<string> notes)
    {
        try
        {
            (int exitCode, string stdOut, string stdErr) =
                await ProcessRunner.RunCapturedAsync(exePath, "--version", VersionTimeout, ct).ConfigureAwait(false);

            string text = stdOut.Trim();
            if (text.Length == 0)
            {
                text = stdErr.Trim();
            }

            if (text.Length == 0)
            {
                notes.Add($"codex --version exited {exitCode} with no output.");
                return null;
            }

            return text;
        }
        catch (Exception ex)
        {
            notes.Add($"codex --version failed: {ex.Message}");
            return null;
        }
    }

    // ----- account/rateLimits/read over app-server (JSONL over stdio) --------------------------

    private static async Task<(IReadOnlyList<QuotaWindow> Windows, List<string> Notes)> ReadRateLimitsAsync(
        string exePath, CancellationToken ct)
    {
        var notes = new List<string>();

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = "app-server",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            StandardInputEncoding = new UTF8Encoding(false),
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(RateLimitsTimeout);

        try
        {
            process.Start();

            // Pipelining is safe per the verified protocol: write both requests, flush once, then read.
            await process.StandardInput.WriteAsync(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"ai-agent-usage-monitor\",\"title\":null,\"version\":\"0.1.0\"}}}\n"
                    .AsMemory(),
                cts.Token).ConfigureAwait(false);
            await process.StandardInput.WriteAsync(
                "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"account/rateLimits/read\"}\n".AsMemory(),
                cts.Token).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cts.Token).ConfigureAwait(false);

            JsonElement? result = null;
            while (result is null)
            {
                string? line = await process.StandardOutput.ReadLineAsync(cts.Token).ConfigureAwait(false);
                if (line is null)
                {
                    break; // stdout closed before we saw an id:2 response
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                JsonDocument doc;
                try
                {
                    doc = JsonDocument.Parse(line);
                }
                catch (JsonException)
                {
                    continue; // defensive: skip any line that fails to parse as JSON
                }

                using (doc)
                {
                    JsonElement root = doc.RootElement;

                    // Responses omit "jsonrpc" entirely. Any line without an "id" is an unsolicited
                    // notification (e.g. remoteControl/status/changed) interleaved right after
                    // initialize - skip it and keep reading, never assume the first line is the answer.
                    if (!root.TryGetProperty("id", out JsonElement idEl) || idEl.ValueKind != JsonValueKind.Number)
                    {
                        if (root.TryGetProperty("method", out JsonElement methodEl) && methodEl.ValueKind == JsonValueKind.String)
                        {
                            notes.Add($"Observed and skipped unsolicited notification: {methodEl.GetString()}");
                        }

                        continue;
                    }

                    if (idEl.GetInt32() != 2)
                    {
                        continue; // e.g. the id:1 initialize acknowledgement
                    }

                    if (root.TryGetProperty("error", out JsonElement errorEl))
                    {
                        throw new InvalidOperationException($"codex app-server returned an error: {errorEl.GetRawText()}");
                    }

                    if (root.TryGetProperty("result", out JsonElement resultEl))
                    {
                        result = resultEl.Clone();
                    }
                }
            }

            if (result is null)
            {
                throw new InvalidOperationException("codex app-server closed stdout before an id:2 response was observed.");
            }

            // Close stdin now that we have what we need - verified behaviour: exits code 0 in ~25ms.
            process.StandardInput.Close();
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);

            List<QuotaWindow> windows = MapRateLimits(result.Value);
            return (windows, notes);
        }
        finally
        {
            ProcessRunner.TryKill(process);
        }
    }

    // ----- Response mapping (exact, verified schema - not duck-typed) --------------------------

    private static List<QuotaWindow> MapRateLimits(JsonElement result)
    {
        var windows = new List<QuotaWindow>();
        int order = 0;

        if (result.TryGetProperty("rateLimitsByLimitId", out JsonElement byId)
            && byId.ValueKind == JsonValueKind.Object
            && byId.EnumerateObject().Any())
        {
            foreach (JsonProperty entry in byId.EnumerateObject())
            {
                AppendBucketWindows(windows, entry.Name, entry.Value, ref order);
            }
        }
        else if (result.TryGetProperty("rateLimits", out JsonElement single) && single.ValueKind == JsonValueKind.Object)
        {
            string limitId = TryGetString(single, "limitId") ?? "unknown";
            AppendBucketWindows(windows, limitId, single, ref order);
        }

        return windows;
    }

    private static void AppendBucketWindows(List<QuotaWindow> windows, string limitId, JsonElement bucket, ref int order)
    {
        string? limitName = TryGetString(bucket, "limitName");
        string? planType = TryGetString(bucket, "planType");
        string? rateLimitReachedType = TryGetString(bucket, "rateLimitReachedType");

        string? creditsBalance = null;
        bool? hasCredits = null;
        bool? unlimitedCredits = null;
        if (bucket.TryGetProperty("credits", out JsonElement credits) && credits.ValueKind == JsonValueKind.Object)
        {
            creditsBalance = TryGetString(credits, "balance");
            hasCredits = TryGetBool(credits, "hasCredits");
            unlimitedCredits = TryGetBool(credits, "unlimited");
        }

        string? individualLimitRaw =
            bucket.TryGetProperty("individualLimit", out JsonElement il) && il.ValueKind != JsonValueKind.Null
                ? il.GetRawText()
                : null;

        var sharedExtra = new Dictionary<string, string>();
        if (planType is not null)
        {
            sharedExtra["planType"] = planType;
        }

        if (rateLimitReachedType is not null)
        {
            sharedExtra["rateLimitReachedType"] = rateLimitReachedType;
        }

        if (hasCredits is not null)
        {
            sharedExtra["credits.hasCredits"] = hasCredits.Value.ToString();
        }

        if (unlimitedCredits is not null)
        {
            sharedExtra["credits.unlimited"] = unlimitedCredits.Value.ToString();
        }

        if (creditsBalance is not null)
        {
            sharedExtra["credits.balance"] = creditsBalance;
        }

        if (individualLimitRaw is not null)
        {
            sharedExtra["individualLimit"] = individualLimitRaw;
        }

        if (bucket.TryGetProperty("primary", out JsonElement primary) && primary.ValueKind == JsonValueKind.Object)
        {
            windows.Add(BuildWindow(limitId, "primary", primary, limitName, sharedExtra, order));
            order++;
        }

        // "secondary" was observed null in the live capture - only emit it when actually populated.
        if (bucket.TryGetProperty("secondary", out JsonElement secondary) && secondary.ValueKind == JsonValueKind.Object)
        {
            windows.Add(BuildWindow(limitId, "secondary", secondary, limitName, sharedExtra, order));
            order++;
        }
    }

    private static QuotaWindow BuildWindow(
        string limitId, string slot, JsonElement slotEl, string? limitName, Dictionary<string, string> sharedExtra, int order)
    {
        string id = $"{limitId}:{slot}";
        double? usedPercent = TryGetDouble(slotEl, "usedPercent");
        DateTimeOffset? resetsAt = TryGetUnixSeconds(slotEl, "resetsAt");
        TimeSpan? windowDuration = TryGetDouble(slotEl, "windowDurationMins") is double mins
            ? TimeSpan.FromMinutes(mins)
            : null;

        bool isPartial = resetsAt is null || windowDuration is null;

        var extra = new Dictionary<string, string>(sharedExtra)
        {
            ["limitId"] = limitId,
            ["slot"] = slot,
        };

        // A provider-supplied limitName is a real label - not a claim that needs justifying.
        // Falling back to the id requires the same honesty check the duck-typed path uses, so the
        // two label sources can never drift out of sync (see DuckTypedQuotaExtractor.TryHumanize).
        string label;
        bool labelIsProviderToken;
        if (limitName is not null)
        {
            label = limitName;
            labelIsProviderToken = false;
        }
        else
        {
            labelIsProviderToken = !DuckTypedQuotaExtractor.TryHumanize(id, out label);
        }

        return new QuotaWindow(
            Id: id,
            Label: label,
            UsedPercent: usedPercent,
            ResetsAt: resetsAt,
            WindowDuration: windowDuration,
            Order: order,
            IsPartial: isPartial,
            Extra: extra,
            LabelIsProviderToken: labelIsProviderToken);
    }

    private static string? TryGetString(JsonElement el, string propertyName) =>
        el.TryGetProperty(propertyName, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool? TryGetBool(JsonElement el, string propertyName)
    {
        if (!el.TryGetProperty(propertyName, out JsonElement v))
        {
            return null;
        }

        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static double? TryGetDouble(JsonElement el, string propertyName) =>
        el.TryGetProperty(propertyName, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d)
            ? d
            : null;

    private static DateTimeOffset? TryGetUnixSeconds(JsonElement el, string propertyName) =>
        el.TryGetProperty(propertyName, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
}
