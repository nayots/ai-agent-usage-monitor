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

    [Fact]
    public void ACandidateMatchingATopLevelWindowIsSuppressed()
    {
        IReadOnlyList<QuotaWindow> windows = Normalize([ExtractedWindow("five_hour")]);

        Assert.DoesNotContain(windows, window => window.Id == "session");
    }

    [Fact]
    public void ADuplicateIsSuppressedDespiteADifferentName()
    {
        QuotaWindow fiveHour = ExtractedWindow("five_hour");

        Assert.NotEqual("session", fiveHour.Id);
        Assert.DoesNotContain(Normalize([fiveHour]), window => window.Id == "session");
    }

    [Fact]
    public void AnInactiveDuplicateIsAlsoSuppressed()
    {
        IReadOnlyList<QuotaWindow> windows = Normalize([ExtractedWindow("seven_day")]);

        Assert.DoesNotContain(windows, window => window.Id == "weekly_all");
    }

    [Fact]
    public void AGenuinelyNewScopedCandidateSurvives()
    {
        IReadOnlyList<QuotaWindow> windows = Normalize([ExtractedWindow("seven_day")]);

        Assert.Contains(windows, window => window.Id == "weekly_scoped_opus");
    }

    [Fact]
    public void ACandidateWithAnUnknownResetIsNeverTreatedAsADuplicate()
    {
        IReadOnlyList<QuotaWindow> windows = Normalize(ExtractedWindows());

        Assert.Contains(windows, window => window.Id == "monthly_scoped_haiku");
    }

    [Fact]
    public void SuppressionNeverRemovesAnAlreadyFoundWindow()
    {
        IReadOnlyList<QuotaWindow> alreadyFound = ExtractedWindows();
        QuotaWindow[] before = alreadyFound.ToArray();

        _ = Normalize(alreadyFound);

        Assert.Equal(before, alreadyFound);
    }

    [Fact]
    public void SurvivorsAreNumberedContiguouslyAfterTheExistingWindows()
    {
        using JsonDocument document = JsonDocument.Parse(
            """
            {
              "limits": [
                { "kind": "duplicate", "percent": 1, "resets_at": "2026-08-17T20:50:00Z" },
                { "kind": "new_one", "percent": 2, "resets_at": "2026-08-17T21:50:00Z" },
                { "kind": "new_two", "percent": 3, "resets_at": null }
              ]
            }
            """);
        IReadOnlyList<QuotaWindow> alreadyFound =
        [
            Window("duplicate", 1.0, new DateTimeOffset(2026, 8, 17, 20, 50, 0, TimeSpan.Zero), 0),
            Window("existing_one", 4.0, null, 1),
            Window("existing_two", 5.0, null, 2),
        ];

        IReadOnlyList<QuotaWindow> windows = ClaudeScopedLimits.Normalize(document.RootElement, alreadyFound);

        Assert.Equal([3, 4], windows.Select(window => window.Order));
    }

    [Fact]
    public void TheOrderOfSurvivorsFollowsTheArrayOrder()
    {
        IReadOnlyList<QuotaWindow> first = Normalize(ExtractedWindows());
        IReadOnlyList<QuotaWindow> second = Normalize(ExtractedWindows());

        Assert.Equal(first.Select(window => window.Id), second.Select(window => window.Id));
        Assert.Equal(["weekly_scoped_opus", "weekly_scoped_sonnet", "monthly_scoped_haiku"], first.Select(window => window.Id));
    }

    private static IReadOnlyList<QuotaWindow> Normalize(IReadOnlyList<QuotaWindow>? alreadyFound = null)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(FixturePath));
        return ClaudeScopedLimits.Normalize(document.RootElement, alreadyFound ?? []);
    }

    private static IReadOnlyList<QuotaWindow> ExistingWindows(int count) => Enumerable.Range(0, count)
        .Select(index => Window($"existing_{index}", 1.0, null, index))
        .ToList();

    private static IReadOnlyList<QuotaWindow> ExtractedWindows()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(FixturePath));
        return DuckTypedQuotaExtractor.Extract(document.RootElement);
    }

    private static QuotaWindow ExtractedWindow(string id) => Assert.Single(ExtractedWindows(), window => window.Id == id);

    private static QuotaWindow Window(string id, double usedPercent, DateTimeOffset? resetsAt, int order) => new(
        id,
        id,
        usedPercent,
        resetsAt,
        null,
        order,
        true,
        new Dictionary<string, string>(),
        true);

    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "claude-usage-limits-sample.json");
}
