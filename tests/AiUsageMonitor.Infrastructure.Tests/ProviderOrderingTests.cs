using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;

namespace AiUsageMonitor.Infrastructure.Tests;

public class ProviderOrderingTests
{
    private sealed class SilentProbe(string name) : IProviderProbe
    {
        public string Name => name;
        public string Mechanism => "fake";
        public MechanismTier Tier => MechanismTier.Official;
        public Task<ProviderSnapshot> ProbeAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private static ProviderDescriptor Provider(string key) => new(key, key, key[..1], new SilentProbe(key));

    [Fact]
    public void ApplyReversesProvidersNamedInTheOrder()
    {
        ProviderDescriptor claude = Provider("claude-code");
        ProviderDescriptor codex = Provider("codex");

        IReadOnlyList<ProviderDescriptor> result = ProviderOrdering.Apply([claude, codex], ["codex", "claude-code"]);

        Assert.Equal([codex, claude], result);
    }

    [Fact]
    public void ApplyMovesOneNamedProviderFirstAndKeepsTheRestInRegistryOrder()
    {
        ProviderDescriptor claude = Provider("claude-code");
        ProviderDescriptor codex = Provider("codex");
        ProviderDescriptor other = Provider("other");

        IReadOnlyList<ProviderDescriptor> result = ProviderOrdering.Apply([claude, codex, other], ["codex"]);

        Assert.Equal([codex, claude, other], result);
    }

    [Fact]
    public void ApplyIgnoresMissingKeysAndReturnsEveryRegisteredProviderOnce()
    {
        ProviderDescriptor claude = Provider("claude-code");
        ProviderDescriptor codex = Provider("codex");

        IReadOnlyList<ProviderDescriptor> result = ProviderOrdering.Apply([claude, codex], ["removed", "codex"]);

        Assert.Equal([codex, claude], result);
    }

    [Fact]
    public void ApplyWithAnEmptyOrderReturnsTheOriginalInstancesInRegistryOrder()
    {
        ProviderDescriptor claude = Provider("claude-code");
        ProviderDescriptor codex = Provider("codex");
        ProviderDescriptor[] providers = [claude, codex];

        IReadOnlyList<ProviderDescriptor> result = ProviderOrdering.Apply(providers, []);

        Assert.Same(providers, result);
        Assert.Equal([claude, codex], result);
    }

    [Fact]
    public void ApplyIncludesADuplicateKeyOnlyOnce()
    {
        ProviderDescriptor claude = Provider("claude-code");
        ProviderDescriptor codex = Provider("codex");

        IReadOnlyList<ProviderDescriptor> result = ProviderOrdering.Apply([claude, codex], ["codex", "Codex", "claude-code"]);

        Assert.Equal([codex, claude], result);
    }
}
