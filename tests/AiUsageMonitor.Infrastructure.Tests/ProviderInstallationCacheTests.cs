using AiUsageMonitor.Infrastructure.Providers;

namespace AiUsageMonitor.Infrastructure.Tests;

public class ProviderInstallationCacheTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 18, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AnEmptyCacheHasNothingToReuse()
    {
        ProviderInstallationCache cache = new();

        Assert.False(cache.TryGet(Start, out _, out _));
    }

    [Fact]
    public void AStoredInstallationIsReusedWithinItsLifetime()
    {
        ProviderInstallationCache cache = new(TimeSpan.FromMinutes(30));
        cache.Store(new ProviderInstallation(@"C:\bin\claude.exe", "2.1.233"), Start);

        Assert.True(cache.TryGet(Start.AddMinutes(29), out ProviderInstallation installation, out TimeSpan age));
        Assert.Equal(@"C:\bin\claude.exe", installation.ExecutablePath);
        Assert.Equal("2.1.233", installation.Version);
        Assert.Equal(TimeSpan.FromMinutes(29), age);
    }

    [Fact]
    public void AStoredInstallationExpiresExactlyAtItsLifetime()
    {
        ProviderInstallationCache cache = new(TimeSpan.FromMinutes(30));
        cache.Store(new ProviderInstallation(@"C:\bin\claude.exe", "2.1.233"), Start);

        Assert.False(cache.TryGet(Start.AddMinutes(30), out _, out _));
    }

    /// <summary>
    /// "Not installed" is a finding, not an absence of one, so it is cached like any other. This is
    /// the case the lifetime is felt in: a provider installed during that window stays invisible
    /// until it lapses or the user asks for a re-check.
    /// </summary>
    [Fact]
    public void ANotInstalledFindingIsCachedLikeAnyOther()
    {
        ProviderInstallationCache cache = new(TimeSpan.FromMinutes(30));
        cache.Store(new ProviderInstallation(null, null), Start);

        Assert.True(cache.TryGet(Start.AddMinutes(5), out ProviderInstallation installation, out _));
        Assert.Null(installation.ExecutablePath);
    }

    [Fact]
    public void InvalidateForcesTheNextLookToHitTheMachine()
    {
        ProviderInstallationCache cache = new(TimeSpan.FromMinutes(30));
        cache.Store(new ProviderInstallation(@"C:\bin\claude.exe", "2.1.233"), Start);

        cache.Invalidate();

        Assert.False(cache.TryGet(Start.AddMinutes(1), out _, out _));
    }

    /// <summary>
    /// A backwards clock - a manual change, a DST transition, a resume from sleep - must expire the
    /// entry rather than pin it. An entry that appears to come from the future would otherwise be
    /// trusted until the clock caught up with it.
    /// </summary>
    [Fact]
    public void AnEntryFromTheFutureIsTreatedAsExpired()
    {
        ProviderInstallationCache cache = new(TimeSpan.FromMinutes(30));
        cache.Store(new ProviderInstallation(@"C:\bin\claude.exe", "2.1.233"), Start);

        Assert.False(cache.TryGet(Start.AddMinutes(-1), out _, out _));
    }

    [Fact]
    public void TheDefaultLifetimeIsThirtyMinutes() =>
        Assert.Equal(TimeSpan.FromMinutes(30), new ProviderInstallationCache().Lifetime);
}
