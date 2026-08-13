using System.Net;
using System.Net.Http.Headers;
using AiUsageMonitor.Domain;
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

    private static ClaudeOAuthUsageProbe CreateProbe(HttpMessageHandler handler, string credentialsPath)
    {
        var processes = new FakeProcessRunner();
        processes.EnqueueCaptured(ExePath, "--version", 0, "2.1.227 (Claude Code)");
        return new ClaudeOAuthUsageProbe(processes, handler, () => ExePath, () => credentialsPath);
    }

    private static string WriteCredentials(TempDirectory directory, string token)
    {
        string path = directory.File("credentials.json");
        File.WriteAllText(path, """{"claudeAiOauth":{"accessToken":"TOKEN"}}""".Replace("TOKEN", token));
        return path;
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body) =>
        new(statusCode) { Content = new StringContent(body) };

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
