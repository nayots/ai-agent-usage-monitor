using System.Text.Json;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers.Claude;

namespace AiUsageMonitor.Infrastructure.Tests;

public sealed class ClaudeScopedLimitsTests
{
    [Fact]
    public void AnEntryWithKindAndScopeGetsACompositeId()
    {
        IReadOnlyList<QuotaWindow> windows = Normalize();

        Assert.Contains(windows, window => window.Id == "weekly_scoped_opus");
    }

    [Fact]
    public void AnEntryWithNoScopeUsesItsKindAlone()
    {
        IReadOnlyList<QuotaWindow> windows = Normalize();

        Assert.Contains(windows, window => window.Id == "session");
        Assert.DoesNotContain(windows, window => window.Id == "limits[0]");
    }

    [Fact]
    public void NoProducedIdContainsAnArrayIndex()
    {
        IReadOnlyList<QuotaWindow> windows = Normalize();

        Assert.DoesNotContain(windows, window => window.Id.Contains("limits[", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEntryWithNoKindIsSkipped()
    {
        IReadOnlyList<QuotaWindow> windows = Normalize();

        Assert.DoesNotContain(windows, window => window.UsedPercent == 5.0);
    }

    [Fact]
    public void AnEntryWithNoPercentIsSkipped()
    {
        IReadOnlyList<QuotaWindow> windows = Normalize();

        Assert.DoesNotContain(windows, window => window.Id == "no_percent");
    }

    [Fact]
    public void AnExplicitZeroPercentIsSurfacedAsZero()
    {
        IReadOnlyList<QuotaWindow> windows = Normalize();

        QuotaWindow window = Assert.Single(windows, window => window.Id == "weekly_scoped_sonnet");
        Assert.Equal(0.0, window.UsedPercent);
    }

    [Fact]
    public void AMissingResetStaysMissing()
    {
        IReadOnlyList<QuotaWindow> windows = Normalize();

        QuotaWindow window = Assert.Single(windows, window => window.Id == "monthly_scoped_haiku");
        Assert.Null(window.ResetsAt);
        Assert.True(window.IsPartial);
    }

    [Fact]
    public void AnInactiveEntryIsStillNormalized()
    {
        IReadOnlyList<QuotaWindow> windows = Normalize();

        QuotaWindow window = Assert.Single(windows, window => window.Id == "weekly_all");
        Assert.Equal("false", window.Extra["claude.is_active"]);
    }

    [Fact]
    public void AnUnrecognisedKindKeepsItsRawToken()
    {
        IReadOnlyList<QuotaWindow> windows = Normalize();

        QuotaWindow window = Assert.Single(windows, window => window.Id == "session");
        Assert.Equal("session", window.Label);
        Assert.True(window.LabelIsProviderToken);
    }

    [Fact]
    public void ExtraNeverCarriesRawResponseContent()
    {
        IReadOnlyList<QuotaWindow> windows = Normalize();
        var allowedValues = new HashSet<string>(StringComparer.Ordinal)
        {
            "session", "weekly_all", "weekly_scoped", "monthly_scoped", "weekly", "monthly",
            "opus", "sonnet", "haiku", "true", "false", "limits"
        };

        Assert.All(
            windows.SelectMany(window => window.Extra.Values),
            value => Assert.Contains(value, allowedValues));
        Assert.DoesNotContain(windows.SelectMany(window => window.Extra.Values), value => value is "normal" or "warning" or "synthetic");
    }

    [Fact]
    public void OrderContinuesFromTheWindowsAlreadyFound()
    {
        IReadOnlyList<QuotaWindow> windows = Normalize(ExistingWindows(3));

        Assert.Equal(3, windows[0].Order);
    }

    [Fact]
    public void NormalizeDoesNotMutateItsInput()
    {
        IReadOnlyList<QuotaWindow> alreadyFound = ExistingWindows(2);
        QuotaWindow[] before = alreadyFound.ToArray();

        _ = Normalize(alreadyFound);

        Assert.Equal(before, alreadyFound);
        Assert.Same(before[0], alreadyFound[0]);
    }

    [Fact]
    public void AMissingOrNonArrayLimitsPropertyYieldsNoCandidates()
    {
        using JsonDocument missing = JsonDocument.Parse("{}");
        using JsonDocument nonArray = JsonDocument.Parse("""{"limits":5}""");

        Assert.Empty(ClaudeScopedLimits.Normalize(missing.RootElement, []));
        Assert.Empty(ClaudeScopedLimits.Normalize(nonArray.RootElement, []));
    }

    private static IReadOnlyList<QuotaWindow> Normalize(IReadOnlyList<QuotaWindow>? alreadyFound = null)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(FixturePath));
        return ClaudeScopedLimits.Normalize(document.RootElement, alreadyFound ?? []);
    }

    private static IReadOnlyList<QuotaWindow> ExistingWindows(int count) => Enumerable.Range(0, count)
        .Select(index => new QuotaWindow(
            $"existing_{index}",
            $"existing_{index}",
            1.0,
            null,
            null,
            index,
            true,
            new Dictionary<string, string>(),
            true))
        .ToList();

    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "claude-usage-limits-sample.json");
}
