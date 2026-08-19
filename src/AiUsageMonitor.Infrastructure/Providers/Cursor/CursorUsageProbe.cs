using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiUsageMonitor.Domain;

namespace AiUsageMonitor.Infrastructure.Providers.Cursor;

/// <summary>
/// Cursor provider probe. Reads the access token Cursor itself stored in its local SQLite state
/// database and asks Cursor's own dashboard API for this user's spend against their monthly
/// ceiling.
///
/// This mechanism is UNOFFICIAL and undocumented - it is not a published API, carries no
/// stability guarantee, and must be labelled as such everywhere it surfaces. It fails safe into
/// an explicit <see cref="ConnectionState"/> rather than fabricating data. Per PRD §4.1.1 / §23
/// the token is used strictly in-memory, only against Cursor's own first-party host, and is never
/// logged, persisted, echoed in an exception, or placed anywhere in the returned snapshot.
///
/// Three things this probe deliberately does NOT do, each of which the obvious implementation
/// would:
/// <list type="bullet">
/// <item>It never refreshes the token. The refresh token is not even read - token lifecycle is
/// entirely Cursor's job. The measured access token lifetime is 60 days, so this costs almost
/// nothing.</item>
/// <item>It never calls <c>GetTeamMembers</c> or <c>GetTeamSpend</c>. Both return the whole
/// organisation's roster - real names and work email addresses - and neither is needed, because
/// the usage-event endpoint is already scoped to the caller.</item>
/// <item>It never reads the stored email address or profile.</item>
/// </list>
/// </summary>
public sealed class CursorUsageProbe : IProviderProbe
{
    public string Name => "Cursor";
    public string Mechanism => MechanismText;
    public MechanismTier Tier => MechanismTier.Unofficial;
    public bool MakesFirstPartyNetworkCall => true;

    // Hard constraint: this is the ONLY network destination this probe may reach. Hardcoded,
    // never derived from configuration, a redirect, or provider input.
    private const string RpcBase = "https://api2.cursor.sh/aiserver.v1.DashboardService/";
    private const string UserAgent = "AiUsageMonitor";

    private const string MechanismText = "Cursor dashboard API + local state database (UNOFFICIAL/undocumented)";
    private const string UpdateModel = "pull (poll)";

    private static readonly TimeSpan MaxThrottleWait = TimeSpan.FromHours(1);
    private static readonly TimeSpan ExpirySkewAllowance = TimeSpan.FromSeconds(30);

    // All rendered verbatim on the card. UI copy, not diagnostics.
    private const string NoSignInMessage =
        "Cursor is installed but has not stored a sign-in on this machine — open Cursor and sign in.";

    private const string ExpiredSignInMessage =
        "Cursor's stored sign-in has expired — open Cursor and sign in again.";

    private const string TokenRejectedMessage =
        "Cursor rejected the stored sign-in — open Cursor and sign in again.";

    private const string ThrottledMessage =
        "Cursor is asking this app to slow down — the next check is scheduled automatically.";

    private const string NoFiguresMessage =
        "Cursor returned no usage figures this application knows how to read for this account.";

    private const string TooManyEventsMessage =
        "This billing cycle has more usage events than this app will download in one go.";

    private static readonly HttpClient Shared = CreateClient(new HttpClientHandler());

    private readonly HttpClient _client;
    private readonly Func<string?> _locateExecutable;
    private readonly Func<string, string?> _readVersion;
    private readonly Func<string> _databasePath;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ProviderInstallationCache _installations;

    private readonly object _totalGate = new();
    private CachedEventTotal? _lastTotal;

    public CursorUsageProbe(
        HttpMessageHandler? handler = null,
        Func<string?>? locateExecutable = null,
        Func<string, string?>? readVersion = null,
        Func<string>? databasePath = null,
        Func<DateTimeOffset>? clock = null,
        ProviderInstallationCache? installations = null)
    {
        _client = handler is null ? Shared : CreateClient(handler);
        _locateExecutable = locateExecutable ?? CursorExecutableLocator.Locate;
        _readVersion = readVersion ?? CursorExecutableLocator.TryReadVersion;
        _databasePath = databasePath ?? CursorStateStore.DefaultDatabasePath;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _installations = installations ?? new ProviderInstallationCache();
    }

    private static HttpClient CreateClient(HttpMessageHandler handler)
    {
        if (handler is HttpClientHandler httpHandler)
        {
            // Never follow a redirect anywhere - api2.cursor.sh is the only permitted destination.
            httpHandler.AllowAutoRedirect = false;
        }

        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
    }

    /// <inheritdoc />
    public void InvalidateInstallation() => _installations.Invalidate();

    public async Task<ProviderSnapshot> ProbeAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var notes = new List<string>();
        string? exePath = null;
        string? version = null;

        try
        {
            ProviderInstallation installation = DetectInstallation(notes);
            exePath = installation.ExecutablePath;
            if (exePath is null)
            {
                notes.Add(@"No local Cursor installation found (checked %LOCALAPPDATA%\Programs\cursor, Program Files, and PATH).");
                return new ProviderSnapshot(
                    ProviderName: Name,
                    Installed: false,
                    Version: null,
                    ExecutablePath: null,
                    State: ConnectionState.NotInstalled,
                    Mechanism: "no local Cursor installation found",
                    Tier: MechanismTier.Unofficial,
                    UpdateModel: "unavailable",
                    Windows: [],
                    RetrievedAt: null,
                    Error: null,
                    Notes: notes);
            }

            version = installation.Version;

            // The token lives only in this local variable for the lifetime of this call. It is read
            // once, used to build Authorization headers, and never assigned to a field, a note, an
            // Extra entry, or an exception message.
            string? token = CursorStateStore.ReadAccessToken(_databasePath(), notes, out CursorAccountMetadata account);
            if (token is null)
            {
                return Snapshot(true, version, exePath, ConnectionState.Unavailable, [], null, NoSignInMessage, notes);
            }

            // The token states its own expiry, so a spent one can be recognised without spending a
            // request to be told so. Strictly an optimisation over the 401 path: an unknown or future
            // expiry falls through and sends the request exactly as before.
            if (account.AccessTokenExpiresAt is DateTimeOffset expiresAt && expiresAt <= _clock() + ExpirySkewAllowance)
            {
                notes.Add(
                    "The stored sign-in had already expired when this check ran, so no request was sent - "
                    + "an expired token can only be rejected.");
                return Snapshot(true, version, exePath, ConnectionState.Unavailable, [], null, ExpiredSignInMessage, notes);
            }

            CursorCall usage = await PostAsync("GetCurrentPeriodUsage", "{}", token, ct).ConfigureAwait(false);
            if (Reject(usage, true, version, exePath, notes) is ProviderSnapshot rejected)
            {
                return rejected;
            }

            CursorCall plan = await PostAsync("GetPlanInfo", "{}", token, ct).ConfigureAwait(false);
            if (Reject(plan, true, version, exePath, notes) is ProviderSnapshot planRejected)
            {
                return planRejected;
            }

            CursorBillingCycle cycle = CursorBillingCycle.Read(usage.Json, plan.Json);
            notes.Add(cycle.DurationWasDerived
                ? "The billing cycle's start was not reported; it was derived from a month-boundary cycle end."
                : "The billing cycle was taken from the provider's own reported instants.");

            IReadOnlyList<QuotaWindow> windows = usage.Json is JsonElement usageJson
                ? CursorSpendWindows.FromPlanUsage(usageJson, cycle, account.MembershipType)
                : [];

            if (windows.Count > 0)
            {
                notes.Add($"{windows.Count} quota window(s) read from this account's plan usage.");
                return Snapshot(true, version, exePath, ConnectionState.Connected, windows, _clock(), null, notes);
            }

            if (account.TeamId is not long teamId)
            {
                notes.Add("The usage response carried no plan figures, and no team is recorded locally to ask about.");
                return Snapshot(true, version, exePath, ConnectionState.Unsupported, [], null, NoFiguresMessage, notes);
            }

            notes.Add("No plan figures in the usage response; reading this seat's spend from its usage events.");
            return await ProbeTeamSeatAsync(token, teamId, cycle, account, version, exePath, notes, ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return Snapshot(exePath is not null, version, exePath, ConnectionState.Error, [], null, "Cursor's response was not valid JSON.", notes);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Snapshot(
                exePath is not null, version, exePath, ConnectionState.Error, [], null,
                $"Request to Cursor timed out after {_client.Timeout.TotalSeconds:0}s.", notes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return Snapshot(exePath is not null, version, exePath, ConnectionState.Error, [], null, ProviderErrorText.For(ex), notes);
        }
        catch (Exception ex)
        {
            return Snapshot(exePath is not null, version, exePath, ConnectionState.Error, [], null, ProviderErrorText.For(ex), notes);
        }
    }

    private async Task<ProviderSnapshot> ProbeTeamSeatAsync(
        string token,
        long teamId,
        CursorBillingCycle cycle,
        CursorAccountMetadata account,
        string? version,
        string exePath,
        List<string> notes,
        CancellationToken ct)
    {
        CursorCall hardLimit = await PostAsync(
            "GetHardLimit", TeamRequest(teamId), token, ct).ConfigureAwait(false);
        if (Reject(hardLimit, true, version, exePath, notes) is ProviderSnapshot rejected)
        {
            return rejected;
        }

        double limitCents = hardLimit.Json is JsonElement limitJson
            && limitJson.ValueKind == JsonValueKind.Object
            && limitJson.TryGetProperty("perUserMonthlyLimitDollars", out JsonElement dollars)
            && dollars.ValueKind == JsonValueKind.Number
            && dollars.TryGetDouble(out double value)
                ? value * 100.0
                : 0.0;

        DateTimeOffset windowStart = cycle.Start ?? _clock().AddDays(-31);
        DateTimeOffset windowEnd = _clock();

        // One cheap request answers "has anything happened since last time?". Only a changed count
        // pays for the pages, which is what keeps an idle widget from re-downloading a month of
        // events every two minutes.
        CursorCall probe = await PostAsync(
            "GetFilteredUsageEvents",
            EventsRequest(teamId, windowStart, windowEnd, page: 1, pageSize: 1),
            token,
            ct).ConfigureAwait(false);
        if (Reject(probe, true, version, exePath, notes) is ProviderSnapshot probeRejected)
        {
            return probeRejected;
        }

        int reportedCount = probe.Json is JsonElement probeJson
            && probeJson.ValueKind == JsonValueKind.Object
            && probeJson.TryGetProperty("totalUsageEventsCount", out JsonElement count)
            && count.ValueKind == JsonValueKind.Number
            && count.TryGetInt32(out int parsed)
                ? parsed
                : -1;

        var key = new CachedEventTotal(cycle.Start, cycle.End, reportedCount, account.AccessTokenExpiresAt, 0);
        if (TryReuseTotal(key, out double cachedCents))
        {
            notes.Add($"Event count unchanged at {reportedCount}; the spend total was reused without refetching the pages.");
            return Connected(cachedCents, limitCents, cycle, account, version, exePath, notes);
        }

        var accumulator = new CursorEventAccumulator();
        for (int page = 1; page <= CursorUsageEvents.MaxPages; page++)
        {
            CursorCall pageCall = await PostAsync(
                "GetFilteredUsageEvents",
                EventsRequest(teamId, windowStart, windowEnd, page, CursorUsageEvents.PageSize),
                token,
                ct).ConfigureAwait(false);
            if (Reject(pageCall, true, version, exePath, notes) is ProviderSnapshot pageRejected)
            {
                return pageRejected;
            }

            if (pageCall.Json is not JsonElement pageJson || !accumulator.AddPage(pageJson))
            {
                break;
            }

            if (accumulator.IsComplete)
            {
                break;
            }
        }

        if (accumulator.Refusal is string refusal)
        {
            notes.Add("The spend total was refused rather than reported; see the message on the card.");
            return Snapshot(true, version, exePath, ConnectionState.Unavailable, [], null, refusal, notes);
        }

        if (!accumulator.IsComplete)
        {
            notes.Add($"Stopped after {CursorUsageEvents.MaxPages} pages without reaching the end of this cycle's events.");
            return Snapshot(true, version, exePath, ConnectionState.Unavailable, [], null, TooManyEventsMessage, notes);
        }

        StoreTotal(key with { SpentCents = accumulator.SpentCents });
        notes.Add($"{accumulator.EventCount} usage event(s) totalled for this billing cycle.");
        return Connected(accumulator.SpentCents, limitCents, cycle, account, version, exePath, notes);
    }

    private ProviderSnapshot Connected(
        double spentCents,
        double limitCents,
        CursorBillingCycle cycle,
        CursorAccountMetadata account,
        string? version,
        string exePath,
        List<string> notes)
    {
        QuotaWindow window = CursorSpendWindows.FromEventTotal(spentCents, limitCents, cycle, account.MembershipType);
        if (limitCents <= 0)
        {
            notes.Add("No per-user monthly ceiling was reported, so the spend is shown without a percentage.");
        }

        return Snapshot(true, version, exePath, ConnectionState.Connected, [window], _clock(), null, notes);
    }

    private bool TryReuseTotal(CachedEventTotal key, out double spentCents)
    {
        lock (_totalGate)
        {
            if (_lastTotal is CachedEventTotal previous
                && previous.CycleStart == key.CycleStart
                && previous.CycleEnd == key.CycleEnd
                && previous.EventCount == key.EventCount
                && previous.SignInExpiresAt == key.SignInExpiresAt
                && key.SignInExpiresAt is not null
                && key.EventCount >= 0)
            {
                spentCents = previous.SpentCents;
                return true;
            }
        }

        spentCents = 0;
        return false;
    }

    private void StoreTotal(CachedEventTotal total)
    {
        lock (_totalGate)
        {
            _lastTotal = total;
        }
    }

    /// <summary>
    /// A spend total remembered only for as long as the process runs, keyed on the cycle, the
    /// event count, and which sign-in produced it. Nothing is written to disk.
    /// <para>
    /// The sign-in is identified by the token's own expiry instant, which is non-secret, already
    /// extracted as printable metadata, and different for every fresh sign-in. It is deliberately
    /// NOT a hash of the token: a record's generated <c>ToString</c> prints every property it
    /// has, so a credential derivative stored here could reach a log through nothing more than
    /// string interpolation - which is the whole reason the token itself is kept out of every
    /// record in this provider. A stable fingerprint of a credential is also a tracking
    /// identifier, and this application has no use for one.
    /// </para>
    /// <para>
    /// An unknown expiry therefore never matches, so a sign-in this application cannot identify
    /// re-reads its events rather than trusting a total that may belong to someone else.
    /// </para>
    /// </summary>
    private sealed record CachedEventTotal(
        DateTimeOffset? CycleStart,
        DateTimeOffset? CycleEnd,
        int EventCount,
        DateTimeOffset? SignInExpiresAt,
        double SpentCents);

    private ProviderInstallation DetectInstallation(List<string> notes)
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
        string? version = exePath is null ? null : _readVersion(exePath);
        notes.Add(version is null
            ? "Cursor's version could not be read from its executable metadata."
            : $"Version {version}, read from the executable's own file metadata - no process was started.");

        var detected = new ProviderInstallation(exePath, version);
        _installations.Store(detected, _clock());
        return detected;
    }

    private readonly record struct CursorCall(HttpStatusCode Status, JsonElement? Json, RetryConditionHeaderValue? RetryAfter);

    /// <summary>
    /// Request bodies are WRITTEN as JSON, never assembled as text.
    /// <para>
    /// This is not fastidiousness. The first version of this method built the body from a raw
    /// string literal, and the quote-delimiter arithmetic silently ate the opening quote of the
    /// first property name and the closing quote of the last value. It compiled, and every unit
    /// test passed, because the test double matched the body with substring checks instead of
    /// parsing it - so the defect only appeared against the live endpoint, as an HTTP 400. A
    /// writer cannot emit malformed JSON, which removes the whole class of failure rather than
    /// this one instance of it.
    /// </para>
    /// </summary>
    private static string EventsRequest(long teamId, DateTimeOffset start, DateTimeOffset end, int page, int pageSize) =>
        WriteJson(writer =>
        {
            writer.WriteNumber("teamId", teamId);

            // The endpoint expects these two as decimal STRINGS of unix milliseconds, not numbers.
            writer.WriteString("startDate", start.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
            writer.WriteString("endDate", end.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
            writer.WriteNumber("page", page);
            writer.WriteNumber("pageSize", pageSize);
        });

    private static string TeamRequest(long teamId) => WriteJson(writer => writer.WriteNumber("teamId", teamId));

    private static string WriteJson(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private async Task<CursorCall> PostAsync(string method, string body, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, RpcBase + method)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("Connect-Protocol-Version", "1");
        request.Headers.UserAgent.ParseAdd(UserAgent);

        using HttpResponseMessage response = await _client.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new CursorCall(response.StatusCode, null, response.Headers.RetryAfter);
        }

        string payload = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using JsonDocument document = JsonDocument.Parse(payload);
        return new CursorCall(response.StatusCode, document.RootElement.Clone(), null);
    }

    /// <summary>
    /// The snapshot a failed call must return, or null when the call succeeded and the caller
    /// should carry on.
    /// </summary>
    private ProviderSnapshot? Reject(
        CursorCall call, bool installed, string? version, string? exePath, List<string> notes)
    {
        if (call.Status == HttpStatusCode.OK)
        {
            return null;
        }

        if (call.Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            notes.Add($"HTTP {(int)call.Status} ({call.Status}) received from Cursor's dashboard API.");
            return Snapshot(installed, version, exePath, ConnectionState.Error, [], null, TokenRejectedMessage, notes);
        }

        if (call.Status == HttpStatusCode.TooManyRequests)
        {
            DateTimeOffset? notBefore = ThrottleInstantFrom(call.RetryAfter, _clock());
            notes.Add(notBefore is null
                ? "HTTP 429 (TooManyRequests); no usable Retry-After header, so the application's own wait applies."
                : "HTTP 429 (TooManyRequests); the endpoint's Retry-After instruction is being honoured.");
            return Snapshot(
                installed, version, exePath, ConnectionState.Error, [], null, ThrottledMessage, notes,
                new ThrottleAdvice(notBefore));
        }

        notes.Add($"HTTP {(int)call.Status} ({call.Status}) received from Cursor's dashboard API.");
        return Snapshot(
            installed, version, exePath, ConnectionState.Error, [], null,
            $"Unexpected HTTP {(int)call.Status} ({call.Status}) from Cursor.", notes);
    }

    /// <summary>
    /// A Retry-After header as an absolute instant, or null when it carries nothing usable. Absent,
    /// zero, negative or already past is "no usable instruction" - never permission to retry at once.
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
}
