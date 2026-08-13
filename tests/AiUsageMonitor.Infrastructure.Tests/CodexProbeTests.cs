using System.ComponentModel;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers.Codex;
using AiUsageMonitor.Infrastructure.Tests.Fakes;

namespace AiUsageMonitor.Infrastructure.Tests;

public sealed class CodexProbeTests
{
    private const string ExePath = "C:\\tools\\codex.exe";

    [Fact]
    public async Task AbsentInstallReturnsNotInstalledWithoutStartingAProcess()
    {
        var probe = new CodexProbe(new FakeProcessRunner(), () => null);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.NotInstalled, snapshot.State);
        Assert.False(snapshot.Installed);
        Assert.Empty(snapshot.Windows);
        Assert.Null(snapshot.Error);
        Assert.Contains("Checked (in order)", Assert.Single(snapshot.Notes));
    }

    [Fact]
    public async Task HappyPathMapsTheOfficialPrimaryWindow()
    {
        FakeProcessRunner processes = WithVersion();
        processes.EnqueueSession(ExePath, "app-server", RateLimitFrame());
        var probe = new CodexProbe(processes, () => ExePath);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        QuotaWindow window = Assert.Single(snapshot.Windows);
        Assert.Equal(ConnectionState.Connected, snapshot.State);
        Assert.Equal(MechanismTier.Official, snapshot.Tier);
        Assert.Equal(37.5, window.UsedPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), window.ResetsAt);
        Assert.Equal(TimeSpan.FromMinutes(300), window.WindowDuration);
        Assert.NotNull(snapshot.RetrievedAt);
        Assert.Null(snapshot.Error);
    }

    [Fact]
    public async Task InterleavedNotificationsAndOtherResponsesAreSkippedBeforeTheIdTwoResult()
    {
        FakeProcessRunner processes = WithVersion();
        processes.EnqueueSession(
            ExePath,
            "app-server",
            """{"method":"remoteControl/status/changed","params":{}}""",
            """{"id":1,"result":{"serverInfo":{"name":"codex"}}}""",
            RateLimitFrame());
        var probe = new CodexProbe(processes, () => ExePath);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Connected, snapshot.State);
        Assert.Single(snapshot.Windows);
        Assert.Contains("Observed and skipped unsolicited notification: remoteControl/status/changed", snapshot.Notes);
    }

    [Fact]
    public async Task ProtocolErrorFrameIsSanitized()
    {
        FakeProcessRunner processes = WithVersion();
        processes.EnqueueSession(ExePath, "app-server", """{"id":2,"error":{"code":-32600,"message":"Not initialized"}}""");
        var probe = new CodexProbe(processes, () => ExePath);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Error, snapshot.State);
        Assert.Equal("The Codex app-server rejected the rate-limit request (error -32600).", snapshot.Error);
        Assert.DoesNotContain("Not initialized", snapshot.Error);
        Assert.DoesNotContain("{", snapshot.Error);
    }

    [Fact]
    public async Task ClosedStdoutBeforeTheIdTwoResponseReturnsTheAuthoredMechanismError()
    {
        FakeProcessRunner processes = WithVersion();
        processes.EnqueueSession(ExePath, "app-server");
        var probe = new CodexProbe(processes, () => ExePath);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Error, snapshot.State);
        Assert.Equal("codex app-server closed stdout before an id:2 response was observed.", snapshot.Error);
    }

    [Fact]
    public async Task MalformedJsonLineIsSkippedBeforeTheIdTwoResult()
    {
        FakeProcessRunner processes = WithVersion();
        processes.EnqueueSession(ExePath, "app-server", "not json at all", RateLimitFrame());
        var probe = new CodexProbe(processes, () => ExePath);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Connected, snapshot.State);
        Assert.Single(snapshot.Windows);
    }

    [Fact]
    public async Task CallerCancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var probe = new CodexProbe(new FakeProcessRunner(), () => ExePath);

        await Assert.ThrowsAsync<OperationCanceledException>(() => probe.ProbeAsync(cancellation.Token));
    }

    [Fact]
    public async Task MissingWindowDurationKeepsTheWindowPartialWithoutInventingAValue()
    {
        FakeProcessRunner processes = WithVersion();
        processes.EnqueueSession(ExePath, "app-server", RateLimitFrame("""{"usedPercent":37.5,"resetsAt":1700000000}"""));
        var probe = new CodexProbe(processes, () => ExePath);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        QuotaWindow window = Assert.Single(snapshot.Windows);
        Assert.True(window.IsPartial);
        Assert.Null(window.WindowDuration);
        Assert.Equal(37.5, window.UsedPercent);
    }

    [Fact]
    public async Task VersionLaunchFailureDoesNotPreventARateLimitRead()
    {
        var processes = new FakeProcessRunner();
        processes.EnqueueCapturedFailure(ExePath, "--version", new Win32Exception());
        processes.EnqueueSession(ExePath, "app-server", RateLimitFrame());
        var probe = new CodexProbe(processes, () => ExePath);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Connected, snapshot.State);
        Assert.Null(snapshot.Version);
    }

    private static FakeProcessRunner WithVersion()
    {
        var processes = new FakeProcessRunner();
        processes.EnqueueCaptured(ExePath, "--version", 0, "codex 1.2.3");
        return processes;
    }

    private static string RateLimitFrame(string? primary = null) =>
        """{"id":2,"result":{"rateLimitsByLimitId":{"default":{"primary":PRIMARY,"secondary":null}}}}"""
            .Replace("PRIMARY", primary ?? """{"usedPercent":37.5,"resetsAt":1700000000,"windowDurationMins":300}""");
}
