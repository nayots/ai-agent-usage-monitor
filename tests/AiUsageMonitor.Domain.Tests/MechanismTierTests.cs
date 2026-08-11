using AiUsageMonitor.Domain;

namespace AiUsageMonitor.Domain.Tests;

public class MechanismTierTests
{
    [Fact]
    public void DefaultTierIsUnofficial()
    {
        // The zero value is deliberately the conservative one: a tier that was never
        // set, or that survived a default-initialised struct, must never read as
        // Official. PRD 4.1.1 - a value obtained unofficially must never be
        // presented as official.
        Assert.Equal(MechanismTier.Unofficial, default(MechanismTier));
    }

    [Fact]
    public void SnapshotCarriesTheTierItWasGiven()
    {
        ProviderSnapshot snapshot = new(
            ProviderName: "Codex",
            Installed: true,
            Version: "0.144.6",
            ExecutablePath: null,
            State: ConnectionState.Connected,
            Mechanism: "codex app-server",
            Tier: MechanismTier.Official,
            UpdateModel: "pull",
            Windows: [],
            RetrievedAt: DateTimeOffset.UnixEpoch,
            Error: null,
            Notes: []);

        Assert.Equal(MechanismTier.Official, snapshot.Tier);
    }
}
