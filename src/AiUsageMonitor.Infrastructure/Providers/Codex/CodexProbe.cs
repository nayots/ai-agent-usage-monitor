using System.Globalization;
using System.Text.Json;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;

namespace AiUsageMonitor.Infrastructure.Providers.Codex;

/// <summary>
/// Codex provider probe. Fully proven mechanism: discovers the local codex.exe, reads its version,
/// then launches "codex app-server" and speaks a small slice of its newline-delimited JSON-RPC
/// protocol over stdio to read live rate limits. No HTTP, no credential files - everything here goes
/// through the same local executable a developer would run themselves.
/// </summary>
public sealed class CodexProbe : IProviderProbe
{
    public string Name => "Codex";
    public string Mechanism => MechanismText;
    public MechanismTier Tier => MechanismTier.Official;

    /// <summary>
    /// False because this application launches a local process. The codex app-server call reaches
    /// the network on OpenAI's side, but this application makes no network call; the field answers
    /// what this application does.
    /// </summary>
    public bool MakesFirstPartyNetworkCall => false;

    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RateLimitsTimeout = TimeSpan.FromSeconds(10);

    private const string MechanismText = "codex app-server (JSON-RPC over stdio, JSONL) - account/rateLimits/read";

    /// <summary>
    /// How the app-server is launched. The sandbox and approval flags are defence-in-depth: this
    /// probe only ever calls <c>account/rateLimits/read</c> and never opens a session, so nothing
    /// it does today can be affected by them - but if a future version of the app-server ever acts
    /// on its own, the process this application spawned is already capped at read-only with every
    /// approval denied. Verified against codex-cli 0.144.6: accepted, and the rate-limit response
    /// is byte-identical to the unflagged call.
    ///
    /// Order matters. <c>-s</c> and <c>-a</c> are flags of the top-level <c>codex</c> command, not
    /// of the <c>app-server</c> subcommand, so they must precede it.
    /// </summary>
    public const string AppServerArguments = "-s read-only -a untrusted app-server";

    private const string SpendLimitSlot = "individualLimit";
    private const string SpendLimitLabel = "Spend limit";

    private readonly IProcessRunner _processes;
    private readonly Func<string?> _locateExecutable;
    private readonly ProviderVersionCache _versions;
    private readonly ProviderInstallationCache _installations;
    private readonly Func<string, DateTime> _lastWriteUtc;
    private readonly Func<DateTimeOffset> _clock;

    public CodexProbe(
        IProcessRunner? processes = null,
        Func<string?>? locateExecutable = null,
        ProviderVersionCache? versions = null,
        Func<string, DateTime>? lastWriteUtc = null,
        Func<DateTimeOffset>? clock = null,
        ProviderInstallationCache? installations = null)
    {
        _processes = processes ?? DefaultProcessRunner.Instance;
        _locateExecutable = locateExecutable ?? CodexExecutableLocator.Locate;
        _versions = versions ?? new ProviderVersionCache();
        _installations = installations ?? new ProviderInstallationCache();
        _lastWriteUtc = lastWriteUtc ?? File.GetLastWriteTimeUtc;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public void InvalidateInstallation() => _installations.Invalidate();

    public async Task<ProviderSnapshot> ProbeAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var notes = new List<string>();
        ProviderInstallation installation = await DetectInstallationAsync(ct, notes).ConfigureAwait(false);
        string? exePath = installation.ExecutablePath;

        if (exePath is null)
        {
            notes.Add(
                "Checked (in order): vendored codex.exe under %APPDATA%\\npm; codex.exe on PATH; " +
                "vendored codex.exe under PATH directories that hold a codex.cmd or codex.ps1 shim.");

            return new ProviderSnapshot(
                ProviderName: Name,
                Installed: false,
                Version: null,
                ExecutablePath: null,
                State: ConnectionState.NotInstalled,
                Mechanism: "no local codex.exe found",
                Tier: MechanismTier.Official,
                UpdateModel: "unavailable",
                Windows: [],
                RetrievedAt: null,
                Error: null,
                Notes: notes);
        }

        string? version = installation.Version;

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
                Tier: Tier,
                UpdateModel: "pull (poll)",
                Windows: windows,
                RetrievedAt: DateTimeOffset.UtcNow,
                Error: null,
                Notes: notes);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ProviderSnapshot(
                ProviderName: Name,
                Installed: true,
                Version: version,
                ExecutablePath: exePath,
                State: ConnectionState.Error,
                Mechanism: Mechanism,
                Tier: Tier,
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
                Tier: Tier,
                UpdateModel: "pull (poll)",
                Windows: [],
                RetrievedAt: null,
                Error: ProviderErrorText.For(ex),
                Notes: notes);
        }
    }

    // ----- Version ---------------------------------------------------------------------------------

    /// <summary>
    /// Where codex.exe is on this machine and what version it reports - from the last detection while
    /// that is still within its lifetime, otherwise by looking again. Detection walks PATH and may
    /// launch the executable, and none of that is a rate-limit read, so it runs on a cadence of its
    /// own; see <see cref="ProviderInstallationCache"/>.
    /// </summary>
    private async Task<ProviderInstallation> DetectInstallationAsync(CancellationToken ct, List<string> notes)
    {
        if (_installations.TryGet(_clock(), out ProviderInstallation cached, out TimeSpan age))
        {
            notes.Add(
                $"Installation and version re-used from a check {RelativeTime.FormatAge(age)}; this machine is "
                + $"re-examined every {_installations.Lifetime.TotalMinutes:0} minutes, or at once via "
                + "\"Re-check providers\" in the settings window.");
            return cached;
        }

        string? exePath = _locateExecutable();
        string? version = exePath is null
            ? null
            : await TryGetVersionAsync(exePath, ct, notes).ConfigureAwait(false);

        var detected = new ProviderInstallation(exePath, version);
        _installations.Store(detected, _clock());
        return detected;
    }

    private async Task<string?> TryGetVersionAsync(string exePath, CancellationToken ct, List<string> notes)
    {
        DateTime? lastWrite = TryGetLastWriteUtc(exePath);
        if (lastWrite is DateTime timestamp && _versions.TryGet(exePath, timestamp, out string cachedVersion))
        {
            notes.Add($"Version {cachedVersion} (cached; executable unchanged since it was read).");
            return cachedVersion;
        }

        try
        {
            (int exitCode, string stdOut, string stdErr) =
                await _processes.RunCapturedAsync(exePath, "--version", VersionTimeout, ct).ConfigureAwait(false);

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

            if (lastWrite is DateTime storedAt)
            {
                _versions.Store(exePath, storedAt, text);
            }

            return text;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            notes.Add($"codex --version did not complete within {VersionTimeout.TotalSeconds:0}s.");
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            notes.Add($"codex --version failed: {ex.Message}");
            return null;
        }
    }

    private DateTime? TryGetLastWriteUtc(string exePath)
    {
        try
        {
            return _lastWriteUtc(exePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // ----- account/rateLimits/read over app-server (JSONL over stdio) --------------------------

    private async Task<(IReadOnlyList<QuotaWindow> Windows, List<string> Notes)> ReadRateLimitsAsync(
        string exePath, CancellationToken ct)
    {
        var notes = new List<string>();

        using IProcessSession process = _processes.Start(exePath, AppServerArguments);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(RateLimitsTimeout);

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

            if (CodexProtocol.TryReadResult(line, notes, out JsonElement response))
            {
                result = response;
            }
        }

        if (result is null)
        {
            throw new ProviderMechanismException("codex app-server closed stdout before an id:2 response was observed.");
        }

        // Close stdin now that we have what we need - verified behaviour: exits code 0 in ~25ms.
        process.StandardInput.Close();
        await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);

        List<QuotaWindow> windows = MapRateLimits(result.Value);
        return (windows, notes);
    }

    // ----- Response mapping (exact, verified schema - not duck-typed) --------------------------

    private static List<QuotaWindow> MapRateLimits(JsonElement result)
    {
        var windows = new List<QuotaWindow>();
        int order = 0;

        // Result-level, a sibling of the buckets rather than a member of one: reset credits belong
        // to the account, so every window carries them.
        Dictionary<string, string> resetCredits = MapResetCredits(result);

        if (result.TryGetProperty("rateLimitsByLimitId", out JsonElement byId)
            && byId.ValueKind == JsonValueKind.Object
            && byId.EnumerateObject().Any())
        {
            foreach (JsonProperty entry in byId.EnumerateObject())
            {
                AppendBucketWindows(windows, entry.Name, entry.Value, resetCredits, ref order);
            }
        }
        else if (result.TryGetProperty("rateLimits", out JsonElement single) && single.ValueKind == JsonValueKind.Object)
        {
            string limitId = TryGetString(single, "limitId") ?? "unknown";
            AppendBucketWindows(windows, limitId, single, resetCredits, ref order);
        }

        return windows;
    }

    /// <summary>
    /// Credits that reset a rate limit early. Deliberately NOT a quota window - they are a way to
    /// clear a limit, not a limit being consumed - so they ride along in <c>Extra</c> instead.
    /// </summary>
    private static Dictionary<string, string> MapResetCredits(JsonElement result)
    {
        var extra = new Dictionary<string, string>();

        if (!result.TryGetProperty("rateLimitResetCredits", out JsonElement summary)
            || summary.ValueKind != JsonValueKind.Object)
        {
            return extra;
        }

        if (TryGetLong(summary, "availableCount") is long availableCount)
        {
            extra["resetCredits.availableCount"] = availableCount.ToString(CultureInfo.InvariantCulture);
        }

        // A null "credits" and an empty one are different facts, and the protocol schema says so
        // explicitly: null means only the count is known, [] means detail rows were fetched and
        // none came back. Emitting this key only for a real array preserves that distinction -
        // an absent key reads as "not reported", which is exactly what null means. The array may
        // also be capped shorter than availableCount, so it is reported as its own number rather
        // than being taken as a recount.
        if (summary.TryGetProperty("credits", out JsonElement credits) && credits.ValueKind == JsonValueKind.Array)
        {
            extra["resetCredits.detailRows"] = credits.GetArrayLength().ToString(CultureInfo.InvariantCulture);
        }

        return extra;
    }

    private static void AppendBucketWindows(
        List<QuotaWindow> windows,
        string limitId,
        JsonElement bucket,
        Dictionary<string, string> resetCredits,
        ref int order)
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

        var sharedExtra = new Dictionary<string, string>(resetCredits);
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

        // A spend-control limit. Null on plus - the plan types that populate it are the business
        // and enterprise seats the protocol's PlanType enum enumerates. Structurally it is a quota
        // window (a percentage plus a reset instant), so it becomes one rather than a special case
        // the UI would have to learn about.
        if (bucket.TryGetProperty("individualLimit", out JsonElement individualLimit)
            && individualLimit.ValueKind == JsonValueKind.Object)
        {
            windows.Add(BuildSpendLimitWindow(limitId, individualLimit, sharedExtra, order));
            order++;
        }
    }

    /// <summary>
    /// Maps <c>individualLimit</c> (protocol type <c>SpendControlLimitSnapshot</c>) into a window.
    /// </summary>
    private static QuotaWindow BuildSpendLimitWindow(
        string limitId, JsonElement slotEl, Dictionary<string, string> sharedExtra, int order)
    {
        // This field reports what is LEFT, not what has been spent, and inverting it is the whole
        // job here: mapping remainingPercent straight through would render a barely-touched limit
        // as nearly exhausted. Worth stating because the obvious external reference gets it wrong -
        // OpenUsage's public docs describe this object as limit/used/resetsAt and do not mention
        // remainingPercent at all. The generated protocol schema is the source of truth.
        double? usedPercent = TryGetDouble(slotEl, "remainingPercent") is double remainingPercent
            ? 100.0 - remainingPercent
            : null;

        var extra = new Dictionary<string, string>(sharedExtra)
        {
            ["limitId"] = limitId,
            ["slot"] = SpendLimitSlot,
        };

        // Currency amounts, which the provider reports as strings. Kept verbatim rather than parsed
        // into numbers this application would then have to guess a currency and a locale for.
        if (TryGetString(slotEl, "limit") is string limit)
        {
            extra[$"{SpendLimitSlot}.limit"] = limit;
        }

        if (TryGetString(slotEl, "used") is string used)
        {
            extra[$"{SpendLimitSlot}.used"] = used;
        }

        return new QuotaWindow(
            Id: $"{limitId}:{SpendLimitSlot}",
            Label: SpendLimitLabel,
            UsedPercent: usedPercent,
            ResetsAt: TryGetUnixSeconds(slotEl, "resetsAt"),
            // Structurally unknowable from this payload: it carries a reset instant and nothing
            // that states how long the period runs for. Inferring "monthly" from the reset
            // boundary would be a guess, so the elapsed marker (PRD ss16) is omitted instead -
            // which is also why IsPartial below is unconditionally true.
            WindowDuration: null,
            Order: order,
            IsPartial: true,
            Extra: extra,
            // Not an unrecognised provider token: this field's meaning is pinned by the generated
            // protocol schema, so the label is this application's own words and renders as such.
            LabelIsProviderToken: false);
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

    private static long? TryGetLong(JsonElement el, string propertyName) =>
        el.TryGetProperty(propertyName, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long l)
            ? l
            : null;

    private static DateTimeOffset? TryGetUnixSeconds(JsonElement el, string propertyName) =>
        el.TryGetProperty(propertyName, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
}
