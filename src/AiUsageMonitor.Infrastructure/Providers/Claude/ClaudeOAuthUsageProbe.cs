using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;

namespace AiUsageMonitor.Infrastructure.Providers.Claude;

/// <summary>
/// Claude Code provider probe using the ONLY verified mechanism authorised by PRD ss4.1.1
/// ("Unofficial Mechanism Policy") and ss11: Claude Code's own undocumented usage endpoint,
/// authenticated with the same local OAuth token Claude Code itself writes to
/// <c>%USERPROFILE%\.claude\.credentials.json</c>. This mechanism can be queried on demand and does
/// not go stale merely because no interactive session is running. The statusLine JSON contract was
/// evaluated as an alternative and rejected (push-only, session-only, requires config modification -
/// see PRD ss11.1); it is not used by this application.
///
/// This mechanism is UNOFFICIAL and undocumented - it is not a published Anthropic API, carries no
/// stability guarantee, and must be labelled as such everywhere it surfaces. It must fail safe into
/// an explicit <see cref="ConnectionState"/> rather than fabricate data if it breaks. Per ss4.1.1 /
/// ss23, the OAuth token is used strictly in-memory, only against Anthropic's own first-party host,
/// and is never logged, persisted, echoed in an exception, or placed anywhere in the returned
/// snapshot (not even <see cref="ProviderSnapshot.Notes"/> or a <see cref="QuotaWindow.Extra"/>
/// dictionary). This app never refreshes, rewrites, or otherwise manages the token's lifecycle - that
/// remains entirely Claude Code's own responsibility.
/// </summary>
public sealed class ClaudeOAuthUsageProbe : IProviderProbe
{
    public string Name => "Claude Code";
    public string Mechanism => MechanismText;
    public MechanismTier Tier => MechanismTier.Unofficial;
    public bool MakesFirstPartyNetworkCall => true;

    // Hard constraint: this is the ONLY network destination this probe (or the whole program) may
    // reach. It is hardcoded, never derived from configuration, a redirect, or provider input.
    private const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
    private const string UserAgent = "ai-agent-usage-monitor-poc/0.1";
    private const string AnthropicBetaHeaderValue = "oauth-2025-04-20";

    private const string MechanismText = "Anthropic OAuth usage endpoint (UNOFFICIAL/undocumented)";
    private const string UpdateModel = "pull (poll)";

    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxThrottleWait = TimeSpan.FromHours(1);

    // Message text is mandated verbatim by the task's hard constraints - do not alter.
    private const string TokenRejectedMessage =
        "OAuth token rejected or expired — run any Claude Code session to refresh it";

    // Rendered verbatim on the card. UI copy, not diagnostics.
    private const string ThrottledMessage =
        "Anthropic is asking this app to slow down — the next check is scheduled automatically.";

    // Both rendered verbatim on the card. The distinction is the point: one of these is fixed by
    // using Claude Code at all, the other cannot be and needs a deliberate sign-in.
    private const string SignInNeedsRefreshMessage =
        "Claude Code's stored sign-in has expired — run any Claude Code session to refresh it";

    private const string SignInExpiredMessage =
        "Claude Code's sign-in has fully expired — run \"claude\" and sign in again";

    /// <summary>How much clock skew to allow before treating a stored expiry as already past.</summary>
    private static readonly TimeSpan ExpirySkewAllowance = TimeSpan.FromSeconds(30);

    // 2000-01-01 and 2100-01-01 as unix milliseconds. See TryGetUnixMilliseconds for why a value
    // outside this range is treated as unknown rather than as an expiry.
    private const long MinPlausibleUnixMs = 946_684_800_000L;
    private const long MaxPlausibleUnixMs = 4_102_444_800_000L;

    private static readonly HttpClient Client = CreateClient();

    private readonly IProcessRunner _processes;
    private readonly HttpClient _client;
    private readonly Func<string?> _locateExecutable;
    private readonly Func<string> _credentialsPath;
    private readonly ProviderVersionCache _versions;
    private readonly ProviderInstallationCache _installations;
    private readonly Func<string, DateTime> _lastWriteUtc;
    private readonly Func<DateTimeOffset> _clock;

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            // Never follow a redirect anywhere - api.anthropic.com is the only permitted destination.
            AllowAutoRedirect = false,
        };

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
    }

    private static HttpClient CreateClient(HttpMessageHandler handler)
    {
        if (handler is HttpClientHandler httpHandler)
        {
            // Test-only seams retain the production no-redirect policy without touching Client.
            httpHandler.AllowAutoRedirect = false;
        }

        return new HttpClient(handler)
        {
            Timeout = Client.Timeout,
        };
    }

    public ClaudeOAuthUsageProbe(
        IProcessRunner? processes = null,
        HttpMessageHandler? handler = null,
        Func<string?>? locateExecutable = null,
        Func<string>? credentialsPath = null,
        ProviderVersionCache? versions = null,
        Func<string, DateTime>? lastWriteUtc = null,
        Func<DateTimeOffset>? clock = null,
        ProviderInstallationCache? installations = null)
    {
        _processes = processes ?? DefaultProcessRunner.Instance;
        _client = handler is null ? Client : CreateClient(handler);
        _locateExecutable = locateExecutable ?? ClaudeExecutableLocator.Locate;
        _credentialsPath = credentialsPath ?? GetCredentialsPath;
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
            notes.Add("No local claude executable found (checked %USERPROFILE%\\.local\\bin, the npm global shim, and PATH).");
            return new ProviderSnapshot(
                ProviderName: Name,
                Installed: false,
                Version: null,
                ExecutablePath: null,
                State: ConnectionState.NotInstalled,
                Mechanism: "no local claude executable found",
                Tier: MechanismTier.Unofficial,
                UpdateModel: "unavailable",
                Windows: [],
                RetrievedAt: null,
                Error: null,
                Notes: notes);
        }

        string? version = installation.Version;

        string credentialsPath = _credentialsPath();
        bool credentialsFileExists = File.Exists(credentialsPath);

        // The token lives only in this local variable for the lifetime of this method call. It is
        // read once, used once to build the Authorization header below, and then never touched
        // again - it is never assigned into a field, Notes, Extra, or any exception message.
        string? token = ReadAccessToken(credentialsPath, credentialsFileExists, notes, out ClaudeAccountMetadata account);
        if (token is null)
        {
            // Missing file / missing claudeAiOauth.accessToken -> Unavailable, never an exception.
            return Snapshot(
                installed: true,
                version,
                exePath,
                ConnectionState.Unavailable,
                [],
                null,
                "Claude Code is installed but has not stored a sign-in on this machine.",
                notes);
        }

        // The credential file states its own expiry, so a spent token can be recognised without
        // spending a request to be told so. Deliberately never records the instant itself - the
        // fact is what is useful, and notes end up in diagnostic dumps.
        if (ExpiredSignInMessage(account, _clock()) is string expiredMessage)
        {
            notes.Add(
                "The stored sign-in had already expired when this check ran, so no request was sent - "
                + "an expired token can only be rejected, and every call counts toward this endpoint's throttling.");
            notes.Add(expiredMessage == SignInExpiredMessage
                ? "The refresh token has expired too, so Claude Code cannot repair this by itself."
                : "The refresh token is still usable, so running any Claude Code session will restore this.");

            return Snapshot(true, version, exePath, ConnectionState.Unavailable, [], null, expiredMessage, notes);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("anthropic-beta", AnthropicBetaHeaderValue);
            request.Headers.UserAgent.ParseAdd(UserAgent);

            using HttpResponseMessage response = await _client.SendAsync(request, ct).ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                notes.Add($"HTTP {(int)response.StatusCode} ({response.StatusCode}) received from the usage endpoint.");
                return Snapshot(true, version, exePath, ConnectionState.Error, [], null, TokenRejectedMessage, notes);
            }

            if (response.StatusCode is HttpStatusCode.TooManyRequests)
            {
                DateTimeOffset? notBefore = ThrottleInstantFrom(response.Headers.RetryAfter, _clock());
                notes.Add(notBefore is null
                    ? "HTTP 429 (TooManyRequests); no usable Retry-After header, so the application's own wait applies."
                    : "HTTP 429 (TooManyRequests); the endpoint's Retry-After instruction is being honoured.");

                return Snapshot(
                    true, version, exePath, ConnectionState.Error, [], null, ThrottledMessage, notes,
                    new ThrottleAdvice(notBefore));
            }

            string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // Never the raw body - top-level JSON key names only, never values.
                IReadOnlyList<string> keys = TryGetTopLevelKeys(body);
                notes.Add(keys.Count > 0
                    ? $"Response top-level JSON keys: {string.Join(", ", keys)}."
                    : "Response body was not a JSON object (or was empty) - no keys to report.");

                string error = $"Unexpected HTTP {(int)response.StatusCode} ({response.StatusCode}) from the usage endpoint.";
                return Snapshot(true, version, exePath, ConnectionState.Error, [], null, error, notes);
            }

            using JsonDocument doc = JsonDocument.Parse(body);

            // Reuse the shared, provider-neutral extractor rather than a bespoke parser. It already
            // handles this endpoint's dialect (utilization + ISO-8601 resets_at) as well as
            // statusLine's (used_percentage + unix seconds) with no code change needed here.
            IReadOnlyList<QuotaWindow> extracted = DuckTypedQuotaExtractor.Extract(doc.RootElement);

            // Defensive, and cheap. The shared extractor does not currently produce a window from a limits[]
            // entry - the entries use "percent", which is not one of its percent keys - but if the provider
            // ever renames that field to one the extractor does know, it would start emitting windows whose
            // ids are array positions. An id like "limits[2]" is not a provider identity: it moves whenever
            // the array is reordered. Dropping them here keeps the normalized entries below the single source
            // of truth for this array.
            IReadOnlyList<QuotaWindow> topLevel = extracted
                .Where(w => !w.Id.StartsWith("limits[", StringComparison.Ordinal))
                .ToList();

            IReadOnlyList<QuotaWindow> scoped = ClaudeScopedLimits.Normalize(doc.RootElement, topLevel);
            IReadOnlyList<QuotaWindow> windows = WithAccountLabels([.. topLevel, .. scoped], account);

            notes.Add($"{windows.Count} quota window(s) discovered.");
            notes.Add(scoped.Count == 0
                ? "No additional quota windows found in the limits array beyond those already reported."
                : $"{scoped.Count} additional quota window(s) normalized from the limits array.");

            int unhumanizedCount = windows.Count(w => w.LabelIsProviderToken);
            notes.Add(unhumanizedCount > 0
                ? $"{unhumanizedCount} quota window(s) have provider-token labels that could not be humanised into friendly \"N unit\" labels."
                : "Every discovered window key humanised cleanly into a friendly \"N unit\" label.");

            return Snapshot(true, version, exePath, ConnectionState.Connected, windows, _clock(), null, notes);
        }
        catch (JsonException)
        {
            return Snapshot(true, version, exePath, ConnectionState.Error, [], null, "Response body was not valid JSON.", notes);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The caller's token was not the one that fired - this is the HttpClient's own 10s
            // Timeout, not caller-requested cancellation (which is left to propagate normally).
            return Snapshot(
                true,
                version,
                exePath,
                ConnectionState.Error,
                [],
                null,
                $"Request to the usage endpoint timed out after {_client.Timeout.TotalSeconds:0}s.",
                notes);
        }
        catch (HttpRequestException ex)
        {
            return Snapshot(true, version, exePath, ConnectionState.Error, [], null, ProviderErrorText.For(ex), notes);
        }
    }

    /// <summary>
    /// Where Claude Code is on this machine and what version it reports - from the last detection
    /// while that is still within its lifetime, otherwise by looking again. Detection walks PATH and
    /// may launch the executable, and none of that is a quota read, so it runs on a cadence of its
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
            (int exitCode, string stdOut, _) =
                await _processes.RunCapturedAsync(exePath, "--version", VersionTimeout, ct).ConfigureAwait(false);

            string? version = exitCode == 0 ? ClaudeExecutableLocator.ParseVersion(stdOut) : null;
            notes.Add(version is null
                ? $"claude --version exited {exitCode} without a parseable version."
                : $"Version reported by the official `claude --version` command: {version}.");

            if (version is not null && lastWrite is DateTime storedAt)
            {
                _versions.Store(exePath, storedAt, version);
            }

            return version;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // ProcessRunner signals its OWN timeout as OperationCanceledException - it links the
            // caller's token to a CancelAfter(VersionTimeout) source. A slow or hung executable is
            // not a shutdown: the version is simply unknown, and the quota read below must still
            // happen. Letting this escape would throw the whole probe out, which breaks the
            // timeout-bounded and one-provider-cannot-affect-the-other constraints (PRD ss24).
            notes.Add($"claude --version did not complete within {VersionTimeout.TotalSeconds:0}s.");
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Caller-requested cancellation is deliberately NOT caught here: it propagates so the
            // refresh service can tell shutdown apart from a provider failure.
            notes.Add($"claude --version failed: {ex.Message}");
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

    private ProviderSnapshot Snapshot(
        bool installed,
        string? version,
        string? executablePath,
        ConnectionState state,
        IReadOnlyList<QuotaWindow> windows,
        DateTimeOffset? retrievedAt,
        string? error,
        List<string> notes,
        ThrottleAdvice? throttle = null) =>
        new(
            ProviderName: Name,
            Installed: installed,
            Version: version,
            ExecutablePath: executablePath,
            State: state,
            Mechanism: Mechanism,
            Tier: Tier,
            UpdateModel: UpdateModel,
            Windows: windows,
            RetrievedAt: retrievedAt,
            Error: error,
            Notes: notes,
            Throttle: throttle);

    /// <summary>
    /// Converts a Retry-After header into an absolute instant, or null when it carries nothing usable.
    /// Both header forms are accepted: delta-seconds and an HTTP-date. A value that is absent, zero,
    /// negative, or already in the past is "no usable instruction" rather than "retry immediately" —
    /// the caller must not read null as permission to ask again at once.
    /// </summary>
    private static DateTimeOffset? ThrottleInstantFrom(RetryConditionHeaderValue? retryAfter, DateTimeOffset now)
    {
        if (retryAfter is null)
        {
            return null;
        }

        TimeSpan wait;
        if (retryAfter.Delta is TimeSpan delta)
        {
            wait = delta;
        }
        else if (retryAfter.Date is DateTimeOffset date)
        {
            wait = date - now;
        }
        else
        {
            return null;
        }

        if (wait <= TimeSpan.Zero)
        {
            return null;
        }

        return now + (wait > MaxThrottleWait ? MaxThrottleWait : wait);
    }

    private static string GetCredentialsPath()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".claude", ".credentials.json");
    }

    /// <summary>
    /// Reads <c>claudeAiOauth.accessToken</c> from the local credential store. Returns null - never
    /// throws - for any failure (missing file, unreadable, malformed JSON, missing property, empty
    /// value); the caller maps that into <see cref="ConnectionState.Unavailable"/>. Only ever records
    /// the presence/absence of the token in <paramref name="notes"/> - literally "token: &lt;present,
    /// redacted&gt;" or "token: &lt;absent&gt;" - never the value, its prefix, or its length.
    ///
    /// The token is returned as a bare string and the non-secret fields beside it come back
    /// separately in <paramref name="metadata"/>. Keeping them in two places is deliberate: see
    /// <see cref="ClaudeAccountMetadata"/> for why the credential must never share a container with
    /// anything this application is willing to print.
    /// </summary>
    private static string? ReadAccessToken(
        string credentialsPath, bool fileExists, List<string> notes, out ClaudeAccountMetadata metadata)
    {
        metadata = ClaudeAccountMetadata.Empty;

        if (!fileExists)
        {
            notes.Add($"No credentials file at {credentialsPath}. token: <absent>");
            return null;
        }

        try
        {
            string json = File.ReadAllText(credentialsPath);
            using JsonDocument doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out JsonElement oauth)
                || oauth.ValueKind != JsonValueKind.Object)
            {
                notes.Add("credentials.json has no \"claudeAiOauth\" object. token: <absent>");
                return null;
            }

            if (!oauth.TryGetProperty("accessToken", out JsonElement tokenEl)
                || tokenEl.ValueKind != JsonValueKind.String)
            {
                notes.Add("claudeAiOauth.accessToken not found or not a string. token: <absent>");
                return null;
            }

            string? token = tokenEl.GetString();
            if (string.IsNullOrWhiteSpace(token))
            {
                notes.Add("claudeAiOauth.accessToken was empty. token: <absent>");
                return null;
            }

            metadata = new ClaudeAccountMetadata(
                AccessTokenExpiresAt: TryGetUnixMilliseconds(oauth, "expiresAt"),
                RefreshTokenExpiresAt: TryGetUnixMilliseconds(oauth, "refreshTokenExpiresAt"),
                SubscriptionType: TryGetNonEmptyString(oauth, "subscriptionType"),
                RateLimitTier: TryGetNonEmptyString(oauth, "rateLimitTier"));

            notes.Add("token: <present, redacted>");
            return token;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Exception type name only - never ex.Message, which for a JsonException could echo a
            // parse-position snippet of file content.
            notes.Add($"credentials.json could not be read or parsed ({ex.GetType().Name}). token: <absent>");
            metadata = ClaudeAccountMetadata.Empty;
            return null;
        }
    }

    private static string? TryGetNonEmptyString(JsonElement el, string propertyName) =>
        el.TryGetProperty(propertyName, out JsonElement v)
        && v.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()
            : null;

    /// <summary>
    /// Reads a unix-millisecond instant, or null when the value is absent, not a number, or outside
    /// the plausible range.
    ///
    /// The range check is a safety interlock, not tidiness. The only consumer of these instants
    /// decides whether to skip a network request, so a misread value - a seconds-based timestamp, a
    /// sentinel zero, a field the provider repurposes - could silently stop the widget working at
    /// all. Anything that does not look like a real millisecond instant is reported as UNKNOWN, and
    /// unknown always falls through to attempting the request.
    /// </summary>
    private static DateTimeOffset? TryGetUnixMilliseconds(JsonElement el, string propertyName)
    {
        if (!el.TryGetProperty(propertyName, out JsonElement v)
            || v.ValueKind != JsonValueKind.Number
            || !v.TryGetInt64(out long milliseconds)
            || milliseconds < MinPlausibleUnixMs
            || milliseconds > MaxPlausibleUnixMs)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
    }

    /// <summary>
    /// The message to show instead of sending a request that the local credential file already says
    /// would fail, or null to go ahead and send it.
    ///
    /// Skipping is only ever an optimisation over the 401 path that already exists, so it is applied
    /// strictly: an unknown expiry, or one still in the future, sends the request as before. The
    /// value of stopping here is that a doomed request is not free - every call counts toward the
    /// throttling that put a 120-second floor on this provider in the first place.
    /// </summary>
    private static string? ExpiredSignInMessage(ClaudeAccountMetadata metadata, DateTimeOffset now)
    {
        if (metadata.AccessTokenExpiresAt is not DateTimeOffset expiresAt
            || expiresAt > now + ExpirySkewAllowance)
        {
            return null;
        }

        // The access token is spent. Whether that repairs itself is entirely down to the refresh
        // token, so an unknown refresh expiry takes the optimistic branch - telling someone to sign
        // in again when they did not have to is worse advice than telling them to run the tool.
        return metadata.RefreshTokenExpiresAt is DateTimeOffset refreshExpiresAt && refreshExpiresAt <= now
            ? SignInExpiredMessage
            : SignInNeedsRefreshMessage;
    }

    /// <summary>
    /// Copies the account-level facts from the credential file onto every window, mirroring what the
    /// Codex adapter does with <c>planType</c>. Costs no request - these arrive in the same read that
    /// produced the token. Never overwrites a key the response itself supplied: payload data is more
    /// specific than anything inferred from a local file, so it wins.
    /// </summary>
    private static IReadOnlyList<QuotaWindow> WithAccountLabels(
        IReadOnlyList<QuotaWindow> windows, ClaudeAccountMetadata metadata)
    {
        var accountExtra = new Dictionary<string, string>();
        AddIfPresent(accountExtra, "subscriptionType", metadata.SubscriptionType);
        AddIfPresent(accountExtra, "rateLimitTier", metadata.RateLimitTier);

        if (accountExtra.Count == 0)
        {
            return windows;
        }

        return [.. windows.Select(window =>
        {
            var merged = new Dictionary<string, string>(window.Extra);
            foreach (KeyValuePair<string, string> entry in accountExtra)
            {
                merged.TryAdd(entry.Key, entry.Value);
            }

            return window with { Extra = merged };
        })];

        static void AddIfPresent(Dictionary<string, string> target, string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                target[key] = value;
            }
        }
    }

    /// <summary>Top-level JSON object key names only - never values, never the raw body.</summary>
    private static IReadOnlyList<string> TryGetTopLevelKeys(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                ? doc.RootElement.EnumerateObject().Select(p => p.Name).ToList()
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
