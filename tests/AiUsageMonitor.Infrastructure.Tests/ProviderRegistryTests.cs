using AiUsageMonitor.Infrastructure.Providers;

namespace AiUsageMonitor.Infrastructure.Tests;

public class ProviderRegistryTests
{
    [Fact]
    public void RegistersEveryProviderThisBuildKnows()
    {
        IReadOnlyList<ProviderDescriptor> providers = ProviderRegistry.CreateDefault();

        Assert.Equal(["Claude Code", "Codex", "Cursor"], providers.Select(p => p.DisplayName));
    }

    [Fact]
    public void EveryKeyIsDistinct()
    {
        // Keys index settings: provider order, hidden providers and per-provider intervals are all
        // keyed on this string, so a collision would silently merge two providers' settings.
        IReadOnlyList<ProviderDescriptor> providers = ProviderRegistry.CreateDefault();

        Assert.Equal(
            providers.Count,
            providers.Select(p => p.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void EveryMonogramIsDistinct()
    {
        // The monogram is the only thing telling two cards apart in mini mode.
        IReadOnlyList<ProviderDescriptor> providers = ProviderRegistry.CreateDefault();

        Assert.Equal(
            providers.Count,
            providers.Select(p => p.Monogram).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void EveryDescriptorMatchesItsProbesOwnName()
    {
        // A descriptor whose display name has drifted from the probe's would put one name on the
        // card and a different one in diagnostics for the same provider.
        foreach (ProviderDescriptor provider in ProviderRegistry.CreateDefault())
        {
            Assert.Equal(provider.DisplayName, provider.Probe.Name);
        }
    }

    [Fact]
    public void EveryDescriptorHasAMonogram()
    {
        foreach (ProviderDescriptor provider in ProviderRegistry.CreateDefault())
        {
            Assert.False(string.IsNullOrWhiteSpace(provider.Monogram));
        }
    }
}
