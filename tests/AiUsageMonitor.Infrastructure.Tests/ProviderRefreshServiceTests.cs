using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;
using AiUsageMonitor.Infrastructure.Refresh;

namespace AiUsageMonitor.Infrastructure.Tests;

public class ProviderRefreshServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeProbe(string name, Func<CancellationToken, Task<ProviderSnapshot>> behaviour) : IProviderProbe
    {
        public int Calls { get; private set; }
        public string Name => name;

        public Task<ProviderSnapshot> ProbeAsync(CancellationToken ct)
        {
            Calls++;
            return behaviour(ct);
        }
    }

    private static ProviderSnapshot Snapshot(string name, ConnectionState state) => new(
        ProviderName: name,
        Installed: true,
        Version: null,
        ExecutablePath: null,
        State: state,
        Mechanism: "fake",
        Tier: MechanismTier.Official,
        UpdateModel: "pull (poll)",
        Windows: [],
        RetrievedAt: state == ConnectionState.Connected ? Now : null,
        Error: null,
        Notes: []);

    private static ProviderDescriptor Descriptor(string name, Func<CancellationToken, Task<ProviderSnapshot>> behaviour) =>
        new(name, name[..1], new FakeProbe(name, behaviour));

    private static ProviderRefreshService Service(params ProviderDescriptor[] providers) =>
        new(providers, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(60));

    [Fact]
    public async Task RaisesOneEventPerProvider()
    {
        ProviderDescriptor a = Descriptor("Alpha", _ => Task.FromResult(Snapshot("Alpha", ConnectionState.Connected)));
        ProviderDescriptor b = Descriptor("Beta", _ => Task.FromResult(Snapshot("Beta", ConnectionState.Connected)));
        ProviderRefreshService service = Service(a, b);

        List<string> seen = [];
        service.Refreshed += (_, e) => { lock (seen) { seen.Add(e.Provider.DisplayName); } };

        await service.RefreshAllAsync(force: true, Now, CancellationToken.None);

        Assert.Equal(["Alpha", "Beta"], seen.Order());
    }

    [Fact]
    public async Task OneProviderThrowingDoesNotStopTheOther()
    {
        ProviderDescriptor bad = Descriptor("Alpha", _ => throw new InvalidOperationException("boom"));
        ProviderDescriptor good = Descriptor("Beta", _ => Task.FromResult(Snapshot("Beta", ConnectionState.Connected)));
        ProviderRefreshService service = Service(bad, good);

        Dictionary<string, ProviderSnapshot> results = [];
        service.Refreshed += (_, e) => { lock (results) { results[e.Provider.DisplayName] = e.Snapshot; } };

        await service.RefreshAllAsync(force: true, Now, CancellationToken.None);

        Assert.Equal(ConnectionState.Error, results["Alpha"].State);
        Assert.Equal(ConnectionState.Connected, results["Beta"].State);
    }

    [Fact]
    public async Task AThrownExceptionNeverEscapesAsAFailedTask()
    {
        ProviderDescriptor bad = Descriptor("Alpha", _ => throw new InvalidOperationException("boom"));

        await Service(bad).RefreshAllAsync(force: true, Now, CancellationToken.None);
    }

    [Fact]
    public async Task AHangingProbeIsCutOffAndReportedAsAnError()
    {
        ProviderDescriptor hanging = Descriptor("Alpha", async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Snapshot("Alpha", ConnectionState.Connected);
        });
        ProviderRefreshService service = Service(hanging);

        ProviderSnapshot? result = null;
        service.Refreshed += (_, e) => result = e.Snapshot;

        await service.RefreshAllAsync(force: true, Now, CancellationToken.None);

        Assert.Equal(ConnectionState.Error, result!.State);
        Assert.Contains("Timed out", result.Error);
    }

    [Fact]
    public async Task AProbeThatIgnoresCancellationIsStillCutOff()
    {
        // The hanging test above is cooperative: its Task.Delay observes the token, so it would
        // pass even if the service only signalled cancellation and awaited. This probe never looks
        // at its token, which is the case a merely-cooperative timeout cannot bound at all.
        using ManualResetEventSlim release = new();
        ProviderDescriptor stubborn = Descriptor("Alpha", _ =>
            Task.Run(() =>
            {
                release.Wait(TimeSpan.FromSeconds(30));
                return Snapshot("Alpha", ConnectionState.Connected);
            }));
        ProviderRefreshService service = Service(stubborn);

        ProviderSnapshot? result = null;
        service.Refreshed += (_, e) => result = e.Snapshot;

        try
        {
            await service.RefreshAllAsync(force: true, Now, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(ConnectionState.Error, result!.State);
            Assert.Contains("Timed out", result.Error);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task ASubscriberThrowingDoesNotAbortTheCycleForOtherProviders()
    {
        // Subscribers run synchronously on the completing thread, so an exception in one would
        // otherwise escape RefreshAllAsync - which documents itself as never throwing - and cancel
        // every other provider's refresh along with it.
        ProviderDescriptor first = Descriptor("Alpha", _ => Task.FromResult(Snapshot("Alpha", ConnectionState.Connected)));
        ProviderDescriptor second = Descriptor("Beta", _ => Task.FromResult(Snapshot("Beta", ConnectionState.Connected)));
        ProviderRefreshService service = Service(first, second);

        List<string> seen = [];
        service.Refreshed += (_, e) =>
        {
            seen.Add(e.Provider.DisplayName);
            throw new InvalidOperationException("A subscriber bug.");
        };

        await service.RefreshAllAsync(force: true, Now, CancellationToken.None);

        Assert.Equal(["Alpha", "Beta"], seen.Order());
    }

    [Fact]
    public async Task CallerCancellationIsNotReportedAsAProviderError()
    {
        using CancellationTokenSource cts = new();
        ProviderDescriptor slow = Descriptor("Alpha", async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Snapshot("Alpha", ConnectionState.Connected);
        });
        ProviderRefreshService service = Service(slow);

        bool raised = false;
        service.Refreshed += (_, _) => raised = true;

        Task refresh = service.RefreshAllAsync(force: true, Now, cts.Token);
        await cts.CancelAsync();
        await refresh;

        Assert.False(raised);
    }

    [Fact]
    public async Task AFailingProviderIsSkippedUntilItsBackoffExpires()
    {
        FakeProbe probe = new("Alpha", _ => Task.FromResult(Snapshot("Alpha", ConnectionState.Error)));
        ProviderDescriptor descriptor = new("Alpha", "A", probe);
        ProviderRefreshService service = Service(descriptor);

        await service.RefreshAllAsync(force: false, Now, CancellationToken.None);
        Assert.Equal(1, probe.Calls);

        await service.RefreshAllAsync(force: false, Now.AddSeconds(1), CancellationToken.None);
        Assert.Equal(1, probe.Calls);

        await service.RefreshAllAsync(force: false, Now.AddSeconds(61), CancellationToken.None);
        Assert.Equal(2, probe.Calls);
    }

    [Fact]
    public async Task AManualRefreshIgnoresBackoff()
    {
        FakeProbe probe = new("Alpha", _ => Task.FromResult(Snapshot("Alpha", ConnectionState.Error)));
        ProviderDescriptor descriptor = new("Alpha", "A", probe);
        ProviderRefreshService service = Service(descriptor);

        await service.RefreshAllAsync(force: false, Now, CancellationToken.None);
        await service.RefreshAllAsync(force: true, Now.AddSeconds(1), CancellationToken.None);

        Assert.Equal(2, probe.Calls);
    }

    [Fact]
    public async Task ASuccessfulRefreshClearsTheBackoff()
    {
        ConnectionState next = ConnectionState.Error;
        FakeProbe probe = new("Alpha", _ => Task.FromResult(Snapshot("Alpha", next)));
        ProviderDescriptor descriptor = new("Alpha", "A", probe);
        ProviderRefreshService service = Service(descriptor);

        await service.RefreshAllAsync(force: false, Now, CancellationToken.None);
        next = ConnectionState.Connected;
        await service.RefreshAllAsync(force: true, Now.AddSeconds(1), CancellationToken.None);

        await service.RefreshAllAsync(force: false, Now.AddSeconds(2), CancellationToken.None);
        Assert.Equal(3, probe.Calls);
    }

    [Fact]
    public void NotInstalledIsAFactNotAFailureSoItIsNeverBackedOff()
    {
        Assert.Equal(TimeSpan.Zero, ProviderRefreshService.BackoffFor(0, TimeSpan.FromSeconds(60)));
    }

    [Theory]
    [InlineData(1, 60)]
    [InlineData(2, 120)]
    [InlineData(3, 240)]
    [InlineData(4, 480)]
    [InlineData(5, 480)]
    [InlineData(9, 480)]
    public void BackoffDoublesAndThenStopsGrowing(int failures, int expectedSeconds) =>
        Assert.Equal(
            TimeSpan.FromSeconds(expectedSeconds),
            ProviderRefreshService.BackoffFor(failures, TimeSpan.FromSeconds(60)));
}
