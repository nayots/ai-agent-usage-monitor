using Xunit;

namespace AiUsageMonitor.Domain.Tests;

public class FreshnessPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly FreshnessPolicy FiveMinutes = new(TimeSpan.FromMinutes(5));

    [Fact]
    public void Evaluate_IsUnknown_WhenNothingHasEverBeenRetrieved()
    {
        // Never retrieved is not the same as stale. A provider that has not answered yet
        // is Waiting, and must not be presented as holding out-of-date data.
        Assert.Equal(FreshnessState.Unknown, FiveMinutes.Evaluate(retrievedAt: null, Now));
    }

    [Fact]
    public void Evaluate_IsFresh_WithinTheThreshold()
    {
        Assert.Equal(FreshnessState.Fresh, FiveMinutes.Evaluate(Now.AddMinutes(-4), Now));
    }

    [Fact]
    public void Evaluate_IsFresh_ExactlyAtTheThreshold()
    {
        // The boundary belongs to Fresh: a value becomes stale when it EXCEEDS the threshold.
        Assert.Equal(FreshnessState.Fresh, FiveMinutes.Evaluate(Now.AddMinutes(-5), Now));
    }

    [Fact]
    public void Evaluate_IsStale_PastTheThreshold()
    {
        Assert.Equal(FreshnessState.Stale, FiveMinutes.Evaluate(Now.AddMinutes(-6), Now));
    }

    [Fact]
    public void Evaluate_IsFresh_WhenTheTimestampIsInTheFuture()
    {
        // Clock skew and DST shifts produce future timestamps. Never report those as stale.
        Assert.Equal(FreshnessState.Fresh, FiveMinutes.Evaluate(Now.AddMinutes(3), Now));
    }

    [Fact]
    public void Default_UsesAConservativeFiveMinuteThreshold()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), FreshnessPolicy.Default.StaleAfter);
    }
}

public class ConnectionStateRulesTests
{
    [Fact]
    public void ApplyFreshness_DemotesConnectedToStale_WhenTheDataHasAged()
    {
        Assert.Equal(
            ConnectionState.Stale,
            ConnectionStateRules.ApplyFreshness(ConnectionState.Connected, FreshnessState.Stale));
    }

    [Fact]
    public void ApplyFreshness_LeavesConnectedAlone_WhenTheDataIsFresh()
    {
        Assert.Equal(
            ConnectionState.Connected,
            ConnectionStateRules.ApplyFreshness(ConnectionState.Connected, FreshnessState.Fresh));
    }

    [Theory]
    [InlineData(ConnectionState.Error)]
    [InlineData(ConnectionState.NotInstalled)]
    [InlineData(ConnectionState.Unsupported)]
    [InlineData(ConnectionState.Unavailable)]
    [InlineData(ConnectionState.Waiting)]
    [InlineData(ConnectionState.Discovering)]
    public void ApplyFreshness_NeverOverwritesANonConnectedState(ConnectionState state)
    {
        // Age must not mask a real failure. An Error that is also old is still an Error -
        // presenting it as merely Stale would imply recoverable data exists when it does not.
        Assert.Equal(state, ConnectionStateRules.ApplyFreshness(state, FreshnessState.Stale));
    }
}
