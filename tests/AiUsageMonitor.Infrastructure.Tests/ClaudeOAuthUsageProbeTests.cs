using System.Net;
using System.Net.Http.Headers;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;
using AiUsageMonitor.Infrastructure.Providers.Claude;
using AiUsageMonitor.Infrastructure.Tests.Fakes;

namespace AiUsageMonitor.Infrastructure.Tests;

public sealed class ClaudeOAuthUsageProbeTests
{
    private const string ExePath = "C:\\tools\\claude.exe";

    [Fact]
    public async Task AbsentInstallReturnsNotInstalledWithoutIssuingAnHttpRequest()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, "{}"));
        var probe = new ClaudeOAuthUsageProbe(new FakeProcessRunner(), handler, () => null, () => "unused");

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.NotInstalled, snapshot.State);
        Assert.False(snapshot.Installed);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task MissingCredentialsReturnsUnavailableWithoutIssuingAnHttpRequest()
    {
        using var directory = new TempDirectory();
        var handler = new StubHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, "{}"));
        var probe = CreateProbe(handler, directory.File("missing.json"));

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Unavailable, snapshot.State);
        Assert.Equal("Claude Code is installed but has not stored a sign-in on this machine.", snapshot.Error);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task CredentialsWithoutClaudeOAuthReturnUnavailableWithoutIssuingAnHttpRequest()
    {
        using var directory = new TempDirectory();
        string path = directory.File("credentials.json");
        File.WriteAllText(path, """{"other":true}""");
        var handler = new StubHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, "{}"));
        var probe = CreateProbe(handler, path);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Unavailable, snapshot.State);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task UsageEndpointDialectReturnsUnofficialConnectedSnapshot()
    {
        using var directory = new TempDirectory();
        string path = WriteCredentials(directory, "token");
        var handler = new StubHttpMessageHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            """{"five_hour":{"utilization":42.5,"resets_at":"2026-08-17T12:00:00Z"}}"""));
        var probe = CreateProbe(handler, path);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Connected, snapshot.State);
        Assert.Equal(MechanismTier.Unofficial, snapshot.Tier);
        Assert.Single(snapshot.Windows);
        Assert.NotNull(snapshot.RetrievedAt);
    }

    [Fact]
    public async Task TheLiveShapedFixtureProducesNoDuplicateWindows()
    {
        ProviderSnapshot snapshot = await ProbeFixtureAsync();

        Assert.Equal(snapshot.Windows.Count, snapshot.Windows.Select(window => window.Id).Distinct(StringComparer.Ordinal).Count());
        (long Reset, double UsedPercent)[] comparable = snapshot.Windows
            .Where(window => window.ResetsAt is not null && window.UsedPercent is not null)
            .Select(window => (window.ResetsAt!.Value.ToUnixTimeSeconds(), window.UsedPercent!.Value))
            .ToArray();
        Assert.Equal(comparable.Length, comparable.Distinct().Count());
    }

    [Fact]
    public async Task TheTopLevelWindowsSurviveUnchanged()
    {
        ProviderSnapshot snapshot = await ProbeFixtureAsync();

        QuotaWindow fiveHour = Assert.Single(snapshot.Windows, window => window.Id == "five_hour");
        Assert.Equal(41.5, fiveHour.UsedPercent);
        Assert.Equal(new DateTimeOffset(2026, 8, 17, 20, 50, 0, TimeSpan.Zero), fiveHour.ResetsAt);

        QuotaWindow sevenDay = Assert.Single(snapshot.Windows, window => window.Id == "seven_day");
        Assert.Equal(12.25, sevenDay.UsedPercent);
        Assert.Equal(new DateTimeOffset(2026, 8, 24, 15, 0, 0, TimeSpan.Zero), sevenDay.ResetsAt);
    }

    [Fact]
    public async Task AScopedLimitAppearsAsItsOwnWindow()
    {
        ProviderSnapshot snapshot = await ProbeFixtureAsync();

        Assert.Single(snapshot.Windows, window => window.Id == "weekly_scoped_opus");
    }

    [Fact]
    public async Task NimbusQuillIsStillAPartialWindow()
    {
        ProviderSnapshot snapshot = await ProbeFixtureAsync();

        QuotaWindow nimbus = Assert.Single(snapshot.Windows, window => window.Id == "nimbus_quill");
        Assert.Null(nimbus.ResetsAt);
        Assert.Equal("nimbus_quill", nimbus.Label);
        Assert.True(nimbus.LabelIsProviderToken);
    }

    [Fact]
    public async Task SpendAndExtraUsageStillProduceNoWindows()
    {
        ProviderSnapshot snapshot = await ProbeFixtureAsync();

        Assert.DoesNotContain(snapshot.Windows, window => window.Id is "spend" or "extra_usage");
    }

    [Fact]
    public async Task TheAddedNoteNeverQuotesResponseContent()
    {
        ProviderSnapshot snapshot = await ProbeFixtureAsync();

        Assert.Contains("3 additional quota window(s) normalized from the limits array.", snapshot.Notes);
        Assert.DoesNotContain(snapshot.Notes, note => note.Contains("weekly_scoped_opus", StringComparison.Ordinal)
            || note.Contains("warning", StringComparison.Ordinal)
            || note.Contains("synthetic", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CredentialTokenIsUsedOnlyInTheAuthorizationHeader()
    {
        const string sentinel = "credential-sentinel-5f0b68bd";
        using var directory = new TempDirectory();
        string path = WriteCredentials(directory, sentinel);
        var handler = new StubHttpMessageHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            """{"five_hour":{"utilization":42.5,"resets_at":"2026-08-17T12:00:00Z"}}"""));
        var probe = CreateProbe(handler, path);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(new AuthenticationHeaderValue("Bearer", sentinel), handler.Authorization);
        Assert.DoesNotContain(sentinel, string.Join(Environment.NewLine, snapshot.Notes));
        Assert.DoesNotContain(sentinel, snapshot.Error ?? string.Empty);
        Assert.DoesNotContain(sentinel, snapshot.Windows.SelectMany(window => window.Extra.Values));
        Assert.DoesNotContain(sentinel, snapshot.Mechanism);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task RejectedTokenReturnsTheAuthoredMessage(HttpStatusCode statusCode)
    {
        using var directory = new TempDirectory();
        var handler = new StubHttpMessageHandler(_ => JsonResponse(statusCode, "{}"));
        var probe = CreateProbe(handler, WriteCredentials(directory, "token"));

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Error, snapshot.State);
        Assert.Equal("OAuth token rejected or expired — run any Claude Code session to refresh it", snapshot.Error);
        Assert.Contains($"HTTP {(int)statusCode}", string.Join(Environment.NewLine, snapshot.Notes));
    }

    [Fact]
    public async Task ServerErrorRecordsOnlyTopLevelKeyNames()
    {
        const string distinctiveValue = "response-value-that-must-not-surface";
        using var directory = new TempDirectory();
        var handler = new StubHttpMessageHandler(_ => JsonResponse(
            HttpStatusCode.InternalServerError,
            """{"error":"VALUE","request_id":"abc"}""".Replace("VALUE", distinctiveValue)));
        var probe = CreateProbe(handler, WriteCredentials(directory, "token"));

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Error, snapshot.State);
        Assert.Contains("Response top-level JSON keys: error, request_id.", snapshot.Notes);
        Assert.DoesNotContain(distinctiveValue, SnapshotText(snapshot));
    }

    [Fact]
    public async Task A429WithDeltaSecondsRetryAfterReportsThatInstant()
    {
        DateTimeOffset now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        using var directory = new TempDirectory();
        var handler = new StubHttpMessageHandler(_ =>
        {
            var response = JsonResponse(HttpStatusCode.TooManyRequests, "{\"error\":\"secret-value\"}");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
            return response;
        });
        var probe = CreateProbe(handler, WriteCredentials(directory, "token"), () => now);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Error, snapshot.State);
        Assert.Empty(snapshot.Windows);
        Assert.Null(snapshot.RetrievedAt);
        Assert.Equal(now + TimeSpan.FromMinutes(2), snapshot.Throttle!.NotBefore);
        Assert.True(snapshot.Throttle.IsProviderSpecified);
        Assert.DoesNotContain("429", snapshot.Error);
        Assert.DoesNotContain("Retry-After", snapshot.Error);
        Assert.DoesNotContain("secret-value", SnapshotText(snapshot));
    }

    [Fact]
    public async Task A429WithAnHttpDateRetryAfterReportsThatInstant()
    {
        DateTimeOffset now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset retryAt = now + TimeSpan.FromMinutes(5);
        using var directory = new TempDirectory();
        var handler = new StubHttpMessageHandler(_ =>
        {
            var response = JsonResponse(HttpStatusCode.TooManyRequests, "{}");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAt);
            return response;
        });
        var probe = CreateProbe(handler, WriteCredentials(directory, "token"), () => now);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(retryAt, snapshot.Throttle!.NotBefore);
        Assert.True(snapshot.Throttle.IsProviderSpecified);
    }

    [Fact]
    public async Task A429WithNoRetryAfterReportsAThrottleWithNoInstant()
    {
        using var directory = new TempDirectory();
        var handler = new StubHttpMessageHandler(_ => JsonResponse(HttpStatusCode.TooManyRequests, "{}"));
        var probe = CreateProbe(handler, WriteCredentials(directory, "token"));

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.NotNull(snapshot.Throttle);
        Assert.Null(snapshot.Throttle.NotBefore);
        Assert.False(snapshot.Throttle.IsProviderSpecified);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task A429WithNoUsableRetryAfterReportsAThrottleWithoutAnInstant(int seconds)
    {
        DateTimeOffset now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        using var directory = new TempDirectory();
        var handler = new StubHttpMessageHandler(_ =>
        {
            var response = JsonResponse(HttpStatusCode.TooManyRequests, "{}");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
            return response;
        });
        var probe = CreateProbe(handler, WriteCredentials(directory, "token"), () => now);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.NotNull(snapshot.Throttle);
        Assert.Null(snapshot.Throttle.NotBefore);
        Assert.False(snapshot.Throttle.IsProviderSpecified);
    }

    [Fact]
    public async Task A429ClampsAnAbsurdRetryAfterToOneHour()
    {
        DateTimeOffset now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        using var directory = new TempDirectory();
        var handler = new StubHttpMessageHandler(_ =>
        {
            var response = JsonResponse(HttpStatusCode.TooManyRequests, "{}");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromDays(1));
            return response;
        });
        var probe = CreateProbe(handler, WriteCredentials(directory, "token"), () => now);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(now + TimeSpan.FromHours(1), snapshot.Throttle!.NotBefore);
    }

    [Fact]
    public async Task A429WithAnExpiredRetryAfterReportsNoInstant()
    {
        DateTimeOffset now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        using var directory = new TempDirectory();
        var handler = new StubHttpMessageHandler(_ =>
        {
            var response = JsonResponse(HttpStatusCode.TooManyRequests, "{}");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(now - TimeSpan.FromMinutes(1));
            return response;
        });
        var probe = CreateProbe(handler, WriteCredentials(directory, "token"), () => now);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Null(snapshot.Throttle!.NotBefore);
        Assert.False(snapshot.Throttle.IsProviderSpecified);
    }

    [Fact]
    public async Task ANon429FailureCarriesNoThrottle()
    {
        using var directory = new TempDirectory();
        var handler = new StubHttpMessageHandler(_ => JsonResponse(
            HttpStatusCode.InternalServerError,
            """{"error":"unavailable"}"""));
        var probe = CreateProbe(handler, WriteCredentials(directory, "token"));

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Null(snapshot.Throttle);
        Assert.Equal("Unexpected HTTP 500 (InternalServerError) from the usage endpoint.", snapshot.Error);
        Assert.Contains("Response top-level JSON keys: error.", snapshot.Notes);
    }

    [Fact]
    public async Task ASuccessCarriesNoThrottle()
    {
        using var directory = new TempDirectory();
        var handler = new StubHttpMessageHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            """{"five_hour":{"utilization":42.5,"resets_at":"2026-08-17T12:00:00Z"}}"""));
        var probe = CreateProbe(handler, WriteCredentials(directory, "token"));

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Connected, snapshot.State);
        Assert.Null(snapshot.Throttle);
    }

    [Fact]
    public async Task MalformedSuccessBodyReturnsTheAuthoredJsonError()
    {
        using var directory = new TempDirectory();
        var handler = new StubHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, "not json"));
        var probe = CreateProbe(handler, WriteCredentials(directory, "token"));

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Error, snapshot.State);
        Assert.Equal("Response body was not valid JSON.", snapshot.Error);
        Assert.Empty(snapshot.Windows);
    }

    [Fact]
    public async Task HttpRequestExceptionIsConvertedToAppAuthoredErrorText()
    {
        using var directory = new TempDirectory();
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("handler-specific-message"));
        var probe = CreateProbe(handler, WriteCredentials(directory, "token"));

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Error, snapshot.State);
        Assert.DoesNotContain("handler-specific-message", snapshot.Error);
    }

    [Fact]
    public async Task CallerCancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var probe = new ClaudeOAuthUsageProbe(new FakeProcessRunner(), new StubHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, "{}")), () => ExePath, () => "unused");

        await Assert.ThrowsAsync<OperationCanceledException>(() => probe.ProbeAsync(cancellation.Token));
    }

    [Fact]
    public async Task UnchangedExecutableUsesTheCachedVersionOnTheSecondProbe()
    {
        using var directory = new TempDirectory();
        var processes = new FakeProcessRunner();
        processes.EnqueueCaptured(ExePath, "--version", 0, "2.1.227 (Claude Code)");
        DateTime lastWrite = new(2026, 8, 13, 10, 0, 0, DateTimeKind.Utc);
        var handler = new StubHttpMessageHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            """{"five_hour":{"utilization":42.5,"resets_at":"2026-08-17T12:00:00Z"}}"""));
        // Zero installation lifetime: this exercises the mtime-keyed ProviderVersionCache, which now
        // sits behind ProviderInstallationCache. Letting the outer cache answer would leave the inner
        // one - the thing that stops a --version spawn once the outer lifetime lapses but the binary
        // has not changed - with no coverage at all.
        var probe = new ClaudeOAuthUsageProbe(
            processes,
            handler,
            () => ExePath,
            () => WriteCredentials(directory, "token"),
            new ProviderVersionCache(),
            _ => lastWrite,
            installations: new ProviderInstallationCache(TimeSpan.Zero));

        ProviderSnapshot first = await probe.ProbeAsync(CancellationToken.None);
        ProviderSnapshot second = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(1, processes.RunCapturedCallCount(ExePath));
        Assert.Equal(first.Version, second.Version);
        Assert.Contains("Version 2.1.227 (cached; executable unchanged since it was read).", second.Notes);
    }

    private static ClaudeOAuthUsageProbe CreateProbe(HttpMessageHandler handler, string credentialsPath, Func<DateTimeOffset>? clock = null)
    {
        var processes = new FakeProcessRunner();
        processes.EnqueueCaptured(ExePath, "--version", 0, "2.1.227 (Claude Code)");
        return new ClaudeOAuthUsageProbe(processes, handler, () => ExePath, () => credentialsPath, clock: clock);
    }

    private static async Task<ProviderSnapshot> ProbeFixtureAsync()
    {
        using var directory = new TempDirectory();
        var handler = new StubHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, File.ReadAllText(FixturePath)));
        var probe = CreateProbe(handler, WriteCredentials(directory, "token"));

        return await probe.ProbeAsync(CancellationToken.None);
    }

    private static string WriteCredentials(TempDirectory directory, string token)
    {
        string path = directory.File("credentials.json");
        File.WriteAllText(path, """{"claudeAiOauth":{"accessToken":"TOKEN"}}""".Replace("TOKEN", token));
        return path;
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body) =>
        new(statusCode) { Content = new StringContent(body) };

    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "claude-usage-limits-sample.json");

    private static string SnapshotText(ProviderSnapshot snapshot) => string.Join(
        Environment.NewLine,
        snapshot.Notes
            .Append(snapshot.Error ?? string.Empty)
            .Append(snapshot.Mechanism)
            .Concat(snapshot.Windows.SelectMany(window => window.Extra.Values)));

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            Authorization = request.Headers.Authorization;
            return Task.FromResult(respond(request));
        }
    }
}
