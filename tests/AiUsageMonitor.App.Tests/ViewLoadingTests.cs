using System.Windows;
using System.Windows.Controls;
using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.App.Views;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;

namespace AiUsageMonitor.App.Tests;

[Collection("wpf")]
public class ViewLoadingTests(WpfFixture wpf)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed class SilentProbe(string name) : IProviderProbe
    {
        public string Name => name;

        public Task<ProviderSnapshot> ProbeAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private static QuotaWindow Window(double? used, bool token, bool withReset) => new(
        Id: token ? "nimbus_quill" : "five_hour",
        Label: token ? "nimbus_quill" : "5-hour window",
        UsedPercent: used,
        ResetsAt: withReset ? Now.AddHours(4) : null,
        WindowDuration: withReset ? TimeSpan.FromHours(5) : null,
        Order: 0,
        IsPartial: !withReset,
        Extra: new Dictionary<string, string>(),
        LabelIsProviderToken: token);

    private static ProviderSnapshot Snapshot(ConnectionState state, IReadOnlyList<QuotaWindow> windows) => new(
        ProviderName: "Claude Code", Installed: true, Version: "2.1.227", ExecutablePath: null,
        State: state, Mechanism: "stub", Tier: MechanismTier.Unofficial, UpdateModel: "pull (poll)",
        Windows: windows, RetrievedAt: state == ConnectionState.NotInstalled ? null : Now,
        Error: null, Notes: []);

    [Theory]
    // The d suffixes are load-bearing. xUnit boxes an InlineData literal at its written type and
    // hands it to the reflection binder, which does no numeric widening: a bare 47 arrives as an
    // Int32 and fails to bind to double? at run time, not compile time. Only the null case would
    // survive.
    [InlineData(47d, false, true)]
    [InlineData(100d, false, true)]
    [InlineData(34d, true, false)]
    [InlineData(null, false, false)]
    public void EveryRowFormRendersWithoutThrowing(double? used, bool token, bool withReset) => wpf.Invoke(() =>
    {
        QuotaRowViewModel row = new(Window(used, token, withReset), colorBarsByUsage: true);
        row.Tick(Now);

        ControlLoadingTests.Measured(new QuotaRowView { DataContext = row, Width = 320 });
    });

    [Theory]
    [InlineData(ConnectionState.Connected)]
    [InlineData(ConnectionState.Stale)]
    [InlineData(ConnectionState.NotInstalled)]
    [InlineData(ConnectionState.Unavailable)]
    [InlineData(ConnectionState.Error)]
    [InlineData(ConnectionState.Waiting)]
    [InlineData(ConnectionState.Unsupported)]
    [InlineData(ConnectionState.Discovering)]
    public void EveryCardStateRendersWithoutThrowing(ConnectionState state) => wpf.Invoke(() =>
    {
        ProviderCardViewModel card = new(
            new ProviderDescriptor("Claude Code", "CC", new SilentProbe("Claude Code")),
            colorBarsByUsage: true,
            _ => { });
        card.Apply(Snapshot(state, [Window(47, false, true), Window(34, true, false)]), Now, FreshnessPolicy.Default);

        ControlLoadingTests.Measured(new ProviderCardView { DataContext = card, Width = 340 });
    });

    [Fact]
    public void TheUpdatedLineDropsItsSeparatorWhenThereIsNothingToTimestamp() => wpf.Invoke(() =>
    {
        // NotInstalled never has a RetrievedAt, so UpdatedText is null. Rendering the separator
        // regardless would leave a dangling "Not installed ·" - and on a machine where one of the
        // two providers simply is not present, that is the first thing the user ever sees.
        ProviderCardViewModel card = new(
            new ProviderDescriptor("Codex", "CX", new SilentProbe("Codex")),
            colorBarsByUsage: true,
            _ => { });
        card.Apply(Snapshot(ConnectionState.NotInstalled, []), Now, FreshnessPolicy.Default);

        ProviderCardView view = ControlLoadingTests.Measured(new ProviderCardView { DataContext = card, Width = 340 });

        Assert.Equal(Visibility.Collapsed, ((TextBlock)view.FindName("UpdatedLine")).Visibility);
    });

    [Fact]
    public void TheUpdatedLineKeepsItsSeparatorWhenThereIsATimestamp() => wpf.Invoke(() =>
    {
        ProviderCardViewModel card = new(
            new ProviderDescriptor("Claude Code", "CC", new SilentProbe("Claude Code")),
            colorBarsByUsage: true,
            _ => { });
        card.Apply(Snapshot(ConnectionState.Connected, [Window(47d, false, true)]), Now, FreshnessPolicy.Default);

        ProviderCardView view = ControlLoadingTests.Measured(new ProviderCardView { DataContext = card, Width = 340 });

        TextBlock updated = (TextBlock)view.FindName("UpdatedLine");
        Assert.Equal(Visibility.Visible, updated.Visibility);
        Assert.Equal("· Updated 0s ago", updated.Text);
    });

    [Fact]
    public void ACardWithNoWindowsStillRenders() => wpf.Invoke(() =>
    {
        ProviderCardViewModel card = new(
            new ProviderDescriptor("Codex", "CX", new SilentProbe("Codex")),
            colorBarsByUsage: false,
            _ => { });
        card.Apply(Snapshot(ConnectionState.Connected, []), Now, FreshnessPolicy.Default);

        FrameworkElement view = ControlLoadingTests.Measured(new ProviderCardView { DataContext = card, Width = 340 });
        Assert.True(view.ActualHeight > 0);
    });
}
