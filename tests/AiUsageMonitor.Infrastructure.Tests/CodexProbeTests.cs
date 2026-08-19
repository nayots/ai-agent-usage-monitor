using System.ComponentModel;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;
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
        processes.EnqueueSession(ExePath, CodexProbe.AppServerArguments, RateLimitFrame());
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
            CodexProbe.AppServerArguments,
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
        processes.EnqueueSession(ExePath, CodexProbe.AppServerArguments, """{"id":2,"error":{"code":-32600,"message":"Not initialized"}}""");
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
        processes.EnqueueSession(ExePath, CodexProbe.AppServerArguments);
        var probe = new CodexProbe(processes, () => ExePath);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Error, snapshot.State);
        Assert.Equal("codex app-server closed stdout before an id:2 response was observed.", snapshot.Error);
    }

    [Fact]
    public async Task MalformedJsonLineIsSkippedBeforeTheIdTwoResult()
    {
        FakeProcessRunner processes = WithVersion();
        processes.EnqueueSession(ExePath, CodexProbe.AppServerArguments, "not json at all", RateLimitFrame());
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
        processes.EnqueueSession(ExePath, CodexProbe.AppServerArguments, RateLimitFrame("""{"usedPercent":37.5,"resetsAt":1700000000}"""));
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
        processes.EnqueueSession(ExePath, CodexProbe.AppServerArguments, RateLimitFrame());
        var probe = new CodexProbe(processes, () => ExePath);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Connected, snapshot.State);
        Assert.Null(snapshot.Version);
    }

    [Fact]
    public async Task UnchangedExecutableUsesTheCachedVersionOnTheSecondProbe()
    {
        FakeProcessRunner processes = WithVersion();
        processes.EnqueueSession(ExePath, CodexProbe.AppServerArguments, RateLimitFrame());
        processes.EnqueueSession(ExePath, CodexProbe.AppServerArguments, RateLimitFrame());
        DateTime lastWrite = new(2026, 8, 13, 10, 0, 0, DateTimeKind.Utc);
        // Zero installation lifetime: these three exercise the mtime-keyed ProviderVersionCache,
        // which now sits behind ProviderInstallationCache. Letting the outer cache answer would
        // leave the inner one - the thing that stops a --version spawn once the outer lifetime
        // lapses but the binary has not changed - with no coverage at all.
        var probe = new CodexProbe(
            processes, () => ExePath, new ProviderVersionCache(), _ => lastWrite,
            installations: new ProviderInstallationCache(TimeSpan.Zero));

        ProviderSnapshot first = await probe.ProbeAsync(CancellationToken.None);
        ProviderSnapshot second = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(1, processes.RunCapturedCallCount(ExePath));
        Assert.Equal(first.Version, second.Version);
        Assert.Contains("Version codex 1.2.3 (cached; executable unchanged since it was read).", second.Notes);
    }

    [Fact]
    public async Task ChangedExecutableTimestampRereadsTheVersion()
    {
        var processes = new FakeProcessRunner();
        processes.EnqueueCaptured(ExePath, "--version", 0, "codex 1.2.3");
        processes.EnqueueCaptured(ExePath, "--version", 0, "codex 1.2.4");
        processes.EnqueueSession(ExePath, CodexProbe.AppServerArguments, RateLimitFrame());
        processes.EnqueueSession(ExePath, CodexProbe.AppServerArguments, RateLimitFrame());
        DateTime lastWrite = new(2026, 8, 13, 10, 0, 0, DateTimeKind.Utc);
        // Zero installation lifetime: these three exercise the mtime-keyed ProviderVersionCache,
        // which now sits behind ProviderInstallationCache. Letting the outer cache answer would
        // leave the inner one - the thing that stops a --version spawn once the outer lifetime
        // lapses but the binary has not changed - with no coverage at all.
        var probe = new CodexProbe(
            processes, () => ExePath, new ProviderVersionCache(), _ => lastWrite,
            installations: new ProviderInstallationCache(TimeSpan.Zero));

        ProviderSnapshot first = await probe.ProbeAsync(CancellationToken.None);
        lastWrite = lastWrite.AddSeconds(1);
        ProviderSnapshot second = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(2, processes.RunCapturedCallCount(ExePath));
        Assert.Equal("codex 1.2.3", first.Version);
        Assert.Equal("codex 1.2.4", second.Version);
    }

    [Fact]
    public async Task FailedVersionReadIsRetriedOnTheNextProbe()
    {
        var processes = new FakeProcessRunner();
        processes.EnqueueCapturedFailure(ExePath, "--version", new Win32Exception());
        processes.EnqueueCaptured(ExePath, "--version", 0, "codex 1.2.3");
        processes.EnqueueSession(ExePath, CodexProbe.AppServerArguments, RateLimitFrame());
        processes.EnqueueSession(ExePath, CodexProbe.AppServerArguments, RateLimitFrame());
        DateTime lastWrite = new(2026, 8, 13, 10, 0, 0, DateTimeKind.Utc);
        // Zero installation lifetime: these three exercise the mtime-keyed ProviderVersionCache,
        // which now sits behind ProviderInstallationCache. Letting the outer cache answer would
        // leave the inner one - the thing that stops a --version spawn once the outer lifetime
        // lapses but the binary has not changed - with no coverage at all.
        var probe = new CodexProbe(
            processes, () => ExePath, new ProviderVersionCache(), _ => lastWrite,
            installations: new ProviderInstallationCache(TimeSpan.Zero));

        ProviderSnapshot first = await probe.ProbeAsync(CancellationToken.None);
        ProviderSnapshot second = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(2, processes.RunCapturedCallCount(ExePath));
        Assert.Null(first.Version);
        Assert.Equal("codex 1.2.3", second.Version);
    }

    [Fact]
    public async Task AppServerIsLaunchedReadOnlyWithApprovalsUntrusted()
    {
        // The fake keys its sessions on the exact argument string, so enqueueing under the literal
        // the probe is required to use is the assertion: a probe that dropped the flags, reordered
        // them, or put them after the subcommand finds no session and never reaches Connected.
        // Order is load-bearing - -s and -a belong to the top-level codex command, not app-server.
        FakeProcessRunner processes = WithVersion();
        processes.EnqueueSession(ExePath, "-s read-only -a untrusted app-server", RateLimitFrame());
        var probe = new CodexProbe(processes, () => ExePath);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Connected, snapshot.State);
        Assert.Equal("-s read-only -a untrusted app-server", CodexProbe.AppServerArguments);
    }

    [Fact]
    public async Task IndividualLimitBecomesASpendWindowWithTheRemainingPercentInverted()
    {
        // The provider reports what is LEFT. A spend limit 5% consumed must not render as 95%.
        FakeProcessRunner processes = WithVersion();
        processes.EnqueueSession(
            ExePath,
            CodexProbe.AppServerArguments,
            BucketFrame("""
                "primary":{"usedPercent":37.5,"resetsAt":1700000000,"windowDurationMins":300},
                "individualLimit":{"limit":"200.00","used":"10.00","remainingPercent":95,"resetsAt":1700000500}
                """));
        var probe = new CodexProbe(processes, () => ExePath);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(2, snapshot.Windows.Count);
        QuotaWindow spend = snapshot.Windows.Single(w => w.Id == "default:individualLimit");
        Assert.Equal(5.0, spend.UsedPercent);
        Assert.Equal(95.0, spend.RemainingPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_500), spend.ResetsAt);
    }

    [Fact]
    public async Task SpendWindowIsLabelledOwnWordsAndDeclaresNoInventedDuration()
    {
        FakeProcessRunner processes = WithVersion();
        processes.EnqueueSession(
            ExePath,
            CodexProbe.AppServerArguments,
            BucketFrame("""
                "individualLimit":{"limit":"200.00","used":"10.00","remainingPercent":95,"resetsAt":1700000500}
                """));
        var probe = new CodexProbe(processes, () => ExePath);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        QuotaWindow spend = Assert.Single(snapshot.Windows);
        Assert.Equal("Spend limit", spend.Label);
        // The schema pins this field's meaning, so the label is ours and must not be rendered as a
        // raw provider token the application failed to understand.
        Assert.False(spend.LabelIsProviderToken);
        // No period length is derivable from the payload, so no elapsed marker may be offered.
        Assert.Null(spend.WindowDuration);
        Assert.Null(spend.ElapsedFraction(DateTimeOffset.UnixEpoch));
        Assert.True(spend.IsPartial);
        Assert.Equal("200.00", spend.Extra["individualLimit.limit"]);
        Assert.Equal("10.00", spend.Extra["individualLimit.used"]);
        Assert.Equal("individualLimit", spend.Extra["slot"]);

        // The row shows this beneath the bar instead of leaving a spend limit expressed only as a
        // percentage. Both halves are the provider's own already-formatted strings, passed through
        // verbatim - nothing here parses a currency or picks a locale.
        Assert.Equal("10.00 of 200.00", spend.AmountText);
    }

    [Fact]
    public async Task ASpendLimitMissingHalfItsPairKeepsItsPercentageRatherThanStatingHalfAComparison()
    {
        FakeProcessRunner processes = WithVersion();
        processes.EnqueueSession(
            ExePath,
            CodexProbe.AppServerArguments,
            BucketFrame("""
                "individualLimit":{"used":"10.00","remainingPercent":95,"resetsAt":1700000500}
                """));
        var probe = new CodexProbe(processes, () => ExePath);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);
        QuotaWindow spend = snapshot.Windows.Single(w => w.Id == "default:individualLimit");

        Assert.Null(spend.AmountText);
        Assert.Equal(5.0, spend.UsedPercent!.Value, 3);
    }

    [Fact]
    public async Task SpendWindowReportsUnknownUsageRatherThanZeroWhenThePercentIsAbsent()
    {
        FakeProcessRunner processes = WithVersion();
        processes.EnqueueSession(
            ExePath,
            CodexProbe.AppServerArguments,
            BucketFrame("""
                "individualLimit":{"limit":"200.00","used":"10.00","resetsAt":1700000500}
                """));
        var probe = new CodexProbe(processes, () => ExePath);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        QuotaWindow spend = Assert.Single(snapshot.Windows);
        Assert.Null(spend.UsedPercent);
        Assert.Null(spend.RemainingPercent);
    }

    [Fact]
    public async Task AbsentIndividualLimitAddsNoWindow()
    {
        FakeProcessRunner processes = WithVersion();
        processes.EnqueueSession(
            ExePath,
            CodexProbe.AppServerArguments,
            BucketFrame("""
                "primary":{"usedPercent":37.5,"resetsAt":1700000000,"windowDurationMins":300},
                "individualLimit":null
                """));
        var probe = new CodexProbe(processes, () => ExePath);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        QuotaWindow window = Assert.Single(snapshot.Windows);
        Assert.Equal("default:primary", window.Id);
    }

    [Theory]
    // A null credits array means only the count is known; an empty one means details were fetched
    // and none came back. The schema states these are different facts, so the absent/present key
    // must keep them apart. A capped array shorter than availableCount is reported as itself.
    [InlineData("null", null)]
    [InlineData("[]", "0")]
    [InlineData("""[{"id":"a","resetType":"codexRateLimits","status":"available","grantedAt":1700000000}]""", "1")]
    public async Task ResetCreditDetailRowsKeepNullApartFromEmpty(string credits, string? expectedDetailRows)
    {
        FakeProcessRunner processes = WithVersion();
        processes.EnqueueSession(
            ExePath,
            CodexProbe.AppServerArguments,
            """
            {"id":2,"result":{"rateLimitsByLimitId":{"default":{"primary":{"usedPercent":10}}},
            "rateLimitResetCredits":{"availableCount":3,"credits":CREDITS}}}
            """.Replace("CREDITS", credits).ReplaceLineEndings(string.Empty));
        var probe = new CodexProbe(processes, () => ExePath);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        QuotaWindow window = Assert.Single(snapshot.Windows);
        Assert.Equal("3", window.Extra["resetCredits.availableCount"]);

        if (expectedDetailRows is null)
        {
            Assert.False(window.Extra.ContainsKey("resetCredits.detailRows"));
        }
        else
        {
            Assert.Equal(expectedDetailRows, window.Extra["resetCredits.detailRows"]);
        }
    }

    private static FakeProcessRunner WithVersion()
    {
        var processes = new FakeProcessRunner();
        processes.EnqueueCaptured(ExePath, "--version", 0, "codex 1.2.3");
        return processes;
    }

    /// <summary>An id:2 result whose single bucket is exactly the supplied properties.</summary>
    private static string BucketFrame(string bucketProperties) =>
        """{"id":2,"result":{"rateLimitsByLimitId":{"default":{BUCKET}}}}"""
            .Replace("BUCKET", bucketProperties.ReplaceLineEndings(string.Empty));

    private static string RateLimitFrame(string? primary = null) =>
        """{"id":2,"result":{"rateLimitsByLimitId":{"default":{"primary":PRIMARY,"secondary":null}}}}"""
            .Replace("PRIMARY", primary ?? """{"usedPercent":37.5,"resetsAt":1700000000,"windowDurationMins":300}""");
}
