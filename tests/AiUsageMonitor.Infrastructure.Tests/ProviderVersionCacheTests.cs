using AiUsageMonitor.Infrastructure.Providers;

namespace AiUsageMonitor.Infrastructure.Tests;

public sealed class ProviderVersionCacheTests
{
    [Fact]
    public void StoreThenGetWithTheSamePathAndTimestampHits()
    {
        var cache = new ProviderVersionCache();
        DateTime lastWrite = new(2026, 8, 13, 10, 0, 0, DateTimeKind.Utc);

        cache.Store("C:\\tools\\provider.exe", lastWrite, "1.2.3");

        Assert.True(cache.TryGet("C:\\tools\\provider.exe", lastWrite, out string version));
        Assert.Equal("1.2.3", version);
    }

    [Fact]
    public void LaterTimestampOrDifferentPathMisses()
    {
        var cache = new ProviderVersionCache();
        DateTime lastWrite = new(2026, 8, 13, 10, 0, 0, DateTimeKind.Utc);
        cache.Store("C:\\tools\\provider.exe", lastWrite, "1.2.3");

        Assert.False(cache.TryGet("C:\\tools\\provider.exe", lastWrite.AddSeconds(1), out _));
        Assert.False(cache.TryGet("C:\\tools\\other.exe", lastWrite, out _));
    }

    [Fact]
    public void PathComparisonIsCaseInsensitive()
    {
        var cache = new ProviderVersionCache();
        DateTime lastWrite = new(2026, 8, 13, 10, 0, 0, DateTimeKind.Utc);
        cache.Store("C:\\TOOLS\\Provider.exe", lastWrite, "1.2.3");

        Assert.True(cache.TryGet("c:\\tools\\provider.exe", lastWrite, out string version));
        Assert.Equal("1.2.3", version);
    }

    [Fact]
    public async Task ConcurrentStoresAndGetsDoNotThrow()
    {
        var cache = new ProviderVersionCache();
        DateTime lastWrite = new(2026, 8, 13, 10, 0, 0, DateTimeKind.Utc);

        Task[] work = Enumerable.Range(0, 20).Select(index => Task.Run(() =>
        {
            string path = $"C:\\tools\\provider-{index % 3}.exe";
            cache.Store(path, lastWrite, index.ToString());
            _ = cache.TryGet(path, lastWrite, out _);
        })).ToArray();

        await Task.WhenAll(work);
    }
}
