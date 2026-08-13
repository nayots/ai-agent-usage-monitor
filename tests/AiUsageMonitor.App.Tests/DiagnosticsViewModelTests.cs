using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Diagnostics;
using AiUsageMonitor.Infrastructure.Providers;
using AiUsageMonitor.Infrastructure.Refresh;

namespace AiUsageMonitor.App.Tests;

public class DiagnosticsViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IncludesEveryProviderAndMakesMissingFactsExplicit()
    {
        TestProbe reported = new("Reported", MechanismTier.Official);
        TestProbe silent = new("Silent", MechanismTier.Unofficial);
        ProviderDescriptor first = new("Reported", "R", reported);
        ProviderDescriptor second = new("Silent", "S", silent);
        ProviderCardViewModel card = Card(first);
        card.Apply(Snapshot("Reported", reported, windows: []), Now, FreshnessPolicy.Default);

        DiagnosticsViewModel viewModel = ViewModel([card, Card(second)], [first, second]);

        Assert.Equal(["Reported", "Silent", "Application"], viewModel.Sections.Select(section => section.Title));
        DiagnosticSection section = viewModel.Sections[1];
        Assert.Equal(DiagnosticsViewModel.EmptyValue, Value(section, "Installed"));
        Assert.Equal("Not reported", Value(section, "Version"));
        Assert.Equal("None", Value(section, "Last error"));
    }

    [Fact]
    public void WindowLinesOmitUnknownPercentAndResetSegments()
    {
        TestProbe probe = new("Provider", MechanismTier.Official);
        ProviderDescriptor descriptor = new("Provider", "P", probe);
        ProviderCardViewModel card = Card(descriptor);
        QuotaWindow window = new("daily", "Daily", null, null, null, 0, false, new Dictionary<string, string>(), false);
        card.Apply(Snapshot("Provider", probe, windows: [window]), Now, FreshnessPolicy.Default);

        DiagnosticSection section = ViewModel([card], [descriptor]).Sections[0];
        string line = Assert.Single(section.Lines);

        Assert.DoesNotContain("0%", line);
        Assert.DoesNotContain("resets", line);
    }

    [Fact]
    public void StatesTierAndFirstPartyNetworkCallFromTheProbe()
    {
        TestProbe official = new("Official", MechanismTier.Official);
        TestProbe unofficial = new("Unofficial", MechanismTier.Unofficial, makesFirstPartyNetworkCall: true);
        ProviderDescriptor first = new("Official", "O", official);
        ProviderDescriptor second = new("Unofficial", "U", unofficial);

        DiagnosticsViewModel viewModel = ViewModel([Card(first), Card(second)], [first, second]);

        Assert.Equal("Official", Value(viewModel.Sections[0], "Mechanism tier"));
        Assert.Contains("Unofficial", Value(viewModel.Sections[1], "Mechanism tier"));
        Assert.StartsWith("Yes", Value(viewModel.Sections[1], "First-party network call"));
    }

    [Fact]
    public void BundleIsRedactedOnlyAfterAllLinesAreRendered()
    {
        TestProbe probe = new("Provider", MechanismTier.Official);
        ProviderDescriptor descriptor = new("Provider", "P", probe);
        ProviderCardViewModel card = Card(descriptor);
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        card.Apply(Snapshot("Provider", probe, notes: [$"Credential location: {profile}\\.provider"]), Now, FreshnessPolicy.Default);

        string bundle = ViewModel([card], [descriptor]).BuildBundle();

        Assert.Contains("%USERPROFILE%", bundle);
        Assert.DoesNotContain(profile, bundle, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer", bundle);
        Assert.DoesNotContain("sk-ant", bundle);
    }

    [Fact]
    public void CopySetsConfirmationAndSuppliesBundle()
    {
        string? copied = null;
        DiagnosticsViewModel viewModel = ViewModel([], [], copy: text => copied = text);

        viewModel.CopyCommand.Execute(null);

        Assert.Equal("Copied. Local paths are masked and no credentials are included.", viewModel.CopyConfirmationText);
        Assert.False(string.IsNullOrWhiteSpace(copied));
    }

    [Fact]
    public void RebuildProjectsTheCardCurrentState()
    {
        TestProbe probe = new("Provider", MechanismTier.Official);
        ProviderDescriptor descriptor = new("Provider", "P", probe);
        ProviderCardViewModel card = Card(descriptor);
        DiagnosticsViewModel viewModel = ViewModel([card], [descriptor]);

        card.Apply(Snapshot("Provider", probe, state: ConnectionState.Error, error: "Unavailable"), Now, FreshnessPolicy.Default);
        viewModel.Rebuild();

        Assert.Equal("Error", Value(viewModel.Sections[0], "Connection state"));
        Assert.Equal("Unavailable", Value(viewModel.Sections[0], "Last error"));
    }

    private static DiagnosticsViewModel ViewModel(
        IReadOnlyList<ProviderCardViewModel> cards,
        IReadOnlyList<ProviderDescriptor> providers,
        Action<string>? copy = null) => new(
            cards,
            providers,
            new ProviderRefreshService(providers, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1)),
            new EnvironmentReport("1.0", ".NET", "Windows", "C:\\logs", true, false),
            new StartupReport(Now, null),
            "System",
            "100%",
            () => Now,
            copy ?? (_ => { }),
            () => { });

    private static ProviderCardViewModel Card(ProviderDescriptor descriptor) => new(descriptor, true, _ => { });

    private static string Value(DiagnosticSection section, string label) =>
        Assert.Single(section.Fields, field => field.Label == label).Value;

    private static ProviderSnapshot Snapshot(
        string name,
        TestProbe probe,
        ConnectionState state = ConnectionState.Connected,
        IReadOnlyList<QuotaWindow>? windows = null,
        IReadOnlyList<string>? notes = null,
        string? error = null) => new(
            name,
            true,
            "1.0",
            "C:\\provider.exe",
            state,
            probe.Mechanism,
            probe.Tier,
            "pull (poll)",
            windows ?? [],
            Now,
            error,
            notes ?? []);

    private sealed class TestProbe(string name, MechanismTier tier, bool makesFirstPartyNetworkCall = false) : IProviderProbe
    {
        public string Name => name;
        public string Mechanism => "Test mechanism";
        public MechanismTier Tier => tier;
        public bool MakesFirstPartyNetworkCall => makesFirstPartyNetworkCall;
        public Task<ProviderSnapshot> ProbeAsync(CancellationToken ct) => throw new NotSupportedException();
    }
}
