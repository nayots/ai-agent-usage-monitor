using AiUsageMonitor.Infrastructure.Providers;

namespace AiUsageMonitor.Infrastructure.Tests;

public class ProviderRegistryTests
{
    [Fact]
    public void RegistersBothProviders()
    {
        IReadOnlyList<ProviderDescriptor> providers = ProviderRegistry.CreateDefault();

        Assert.Equal(["Claude Code", "Codex"], providers.Select(p => p.DisplayName));
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
