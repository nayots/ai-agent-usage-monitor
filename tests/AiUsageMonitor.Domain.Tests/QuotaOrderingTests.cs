using Xunit;

namespace AiUsageMonitor.Domain.Tests;

public class QuotaOrderingTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void InProviderOrder_PreservesProviderOrder_NotDuration()
    {
        // Verified 2026-08-10: seven_day reset SOONER than five_hour on the same account.
        // Sorting by assumed duration or by countdown would reorder them wrongly. PRD SS7.3.
        QuotaWindow fiveHour = QuotaWindowTests.Window(
            id: "five_hour", resetsAt: Now.AddHours(3).AddMinutes(12),
            windowDuration: TimeSpan.FromHours(5), order: 0);
        QuotaWindow sevenDay = QuotaWindowTests.Window(
            id: "seven_day", resetsAt: Now.AddHours(4).AddMinutes(55),
            windowDuration: TimeSpan.FromDays(7), order: 1);

        IReadOnlyList<QuotaWindow> ordered = QuotaOrdering.InProviderOrder(new[] { sevenDay, fiveHour });

        Assert.Equal(new[] { "five_hour", "seven_day" }, ordered.Select(w => w.Id).ToArray());
    }

    [Fact]
    public void InProviderOrder_IsStable_WhenOrdersCollide()
    {
        QuotaWindow first = QuotaWindowTests.Window(id: "a", order: 0);
        QuotaWindow second = QuotaWindowTests.Window(id: "b", order: 0);

        IReadOnlyList<QuotaWindow> ordered = QuotaOrdering.InProviderOrder(new[] { first, second });

        Assert.Equal(new[] { "a", "b" }, ordered.Select(w => w.Id).ToArray());
    }

    [Fact]
    public void InProviderOrder_HandlesAnEmptySequence()
    {
        // A provider reporting zero windows is a valid state, not an error. PRD SS7.2 item 11.
        Assert.Empty(QuotaOrdering.InProviderOrder(Array.Empty<QuotaWindow>()));
    }

    [Fact]
    public void DisplayLabel_UsesTheLabel_WhenOneExists()
    {
        Assert.Equal("5 hour", QuotaOrdering.DisplayLabel(QuotaWindowTests.Window(id: "five_hour") with { Label = "5 hour" }));
    }

    [Fact]
    public void DisplayLabel_FallsBackToTheIdentifier_WhenTheLabelIsBlank()
    {
        // Never render an empty row. The raw identifier is always better than nothing.
        QuotaWindow window = QuotaWindowTests.Window(id: "codex") with { Label = "   " };

        Assert.Equal("codex", QuotaOrdering.DisplayLabel(window));
    }

    [Fact]
    public void DisplayLabel_NeverInventsALabel_ForAnUnknownWindow()
    {
        QuotaWindow window = QuotaWindowTests.Window(id: "nimbus_quill")
            with { Label = "nimbus_quill", LabelIsProviderToken = true };

        Assert.Equal("nimbus_quill", QuotaOrdering.DisplayLabel(window));
    }
}
