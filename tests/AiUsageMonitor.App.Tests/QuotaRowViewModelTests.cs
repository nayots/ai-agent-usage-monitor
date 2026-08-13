using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Domain;

namespace AiUsageMonitor.App.Tests;

public class QuotaRowViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private static QuotaWindow Window(
        string id = "five_hour",
        string label = "5-hour window",
        double? usedPercent = 47,
        DateTimeOffset? resetsAt = null,
        TimeSpan? duration = null,
        bool labelIsProviderToken = false,
        IReadOnlyDictionary<string, string>? extra = null) => new(
            Id: id,
            Label: label,
            UsedPercent: usedPercent,
            ResetsAt: resetsAt,
            WindowDuration: duration,
            Order: 0,
            IsPartial: resetsAt is null || duration is null,
            Extra: extra ?? new Dictionary<string, string>(),
            LabelIsProviderToken: labelIsProviderToken);

    private static QuotaRowViewModel Row(QuotaWindow window, bool colorBarsByUsage = true)
    {
        QuotaRowViewModel row = new(window, colorBarsByUsage);
        row.Tick(Now);
        return row;
    }

    [Fact]
    public void CompleteRowRendersLabelPercentageAndCountdown()
    {
        QuotaRowViewModel row = Row(Window(resetsAt: Now.AddMinutes(295), duration: TimeSpan.FromHours(5)));

        Assert.Equal("5-hour window", row.Label);
        Assert.Equal("47%", row.UsedText);
        Assert.Equal("4h 55m", row.CountdownText);
        Assert.NotNull(row.ElapsedFraction);
    }

    [Fact]
    public void PartialRowShowsNoCountdownAndNoMarker()
    {
        QuotaRowViewModel row = Row(Window(id: "nimbus_quill", label: "nimbus_quill", usedPercent: 34, labelIsProviderToken: true));

        Assert.Equal("34%", row.UsedText);
        Assert.Null(row.CountdownText);
        Assert.Null(row.ElapsedFraction);
        Assert.True(row.IsProviderToken);
    }

    [Fact]
    public void AbsentUsageIsAbsentRatherThanZero()
    {
        QuotaRowViewModel row = Row(Window(usedPercent: null));

        Assert.Null(row.UsedText);
        Assert.Null(row.UsedPercent);
    }

    [Fact]
    public void TheRowDetailListsEveryKnownFactInDisplayOrder()
    {
        DateTimeOffset resetsAt = new(2026, 8, 11, 18, 30, 0, TimeSpan.Zero);
        QuotaRowViewModel row = new(
            Window(
                id: "five_hour",
                resetsAt: resetsAt,
                duration: TimeSpan.FromHours(5),
                extra: new Dictionary<string, string>
                {
                    ["source"] = "response",
                    ["slot"] = "primary"
                }),
            colorBarsByUsage: true,
            mechanism: "Codex CLI");

        Assert.Equal(
            string.Join(
                Environment.NewLine,
                "identifier: five_hour",
                "mechanism: Codex CLI",
                $"resets at: {resetsAt.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture)}",
                "window duration: 5h 00m",
                "source: response",
                "slot: primary"),
            row.DetailText);
    }

    [Fact]
    public void TheRowDetailOmitsMissingValuesAndExplainsPartialData()
    {
        QuotaRowViewModel partial = Row(Window(
            id: "nimbus_quill",
            label: "nimbus_quill",
            usedPercent: null,
            extra: new Dictionary<string, string>()));
        QuotaRowViewModel complete = Row(Window(
            resetsAt: Now.AddHours(1),
            duration: TimeSpan.FromHours(5)));

        Assert.Equal(
            string.Join(Environment.NewLine,
                "identifier: nimbus_quill",
                "partial data: the provider did not supply a reset time or a window duration"),
            partial.DetailText);
        Assert.DoesNotContain("resets at", partial.DetailText);
        Assert.DoesNotContain("null", partial.DetailText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\n0\n", partial.DetailText);
        Assert.DoesNotContain(": " + Environment.NewLine, partial.DetailText);
        Assert.DoesNotContain("partial data", complete.DetailText);
        Assert.DoesNotContain("mechanism:", partial.DetailText);
    }

    [Fact]
    public void TheAccessibleNameIncludesPartialDataAndTheExactResetInstant()
    {
        DateTimeOffset resetsAt = Now.AddHours(1);
        QuotaRowViewModel row = Row(Window(resetsAt: resetsAt, duration: null));

        Assert.Equal(
            $"5-hour window, 47% used, resets in 1h 00m, partial data, resets at {resetsAt.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture)}",
            row.AccessibleName);
    }

    [Fact]
    public void AMarkerAppearsOnlyWhenTheDurationIsKnown()
    {
        Assert.Null(Row(Window(resetsAt: Now.AddHours(1))).ElapsedFraction);
        Assert.NotNull(Row(Window(resetsAt: Now.AddHours(1), duration: TimeSpan.FromHours(5))).ElapsedFraction);
    }

    [Fact]
    public void OneHundredPercentIsExhaustedRegardlessOfTheColourSetting()
    {
        Assert.True(Row(Window(usedPercent: 100), colorBarsByUsage: true).IsExhausted);
        Assert.True(Row(Window(usedPercent: 100), colorBarsByUsage: false).IsExhausted);
        Assert.False(Row(Window(usedPercent: 99)).IsExhausted);
    }

    [Fact]
    public void TickAdvancesTheCountdownWithoutTouchingTheProvider()
    {
        QuotaRowViewModel row = Row(Window(resetsAt: Now.AddMinutes(10), duration: TimeSpan.FromHours(1)));
        Assert.Equal("10m 00s", row.CountdownText);

        row.Tick(Now.AddMinutes(5));
        Assert.Equal("5m 00s", row.CountdownText);
    }

    [Fact]
    public void TheAccessibleNameSpellsOutTheDirectionOfThePercentage()
    {
        QuotaRowViewModel row = Row(Window(resetsAt: Now.AddMinutes(295), duration: TimeSpan.FromHours(5)));

        Assert.Equal(
            $"5-hour window, 47% used, resets in 4h 55m, resets at {Now.AddMinutes(295).ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture)}",
            row.AccessibleName);
    }

    [Fact]
    public void TheAccessibleNameSaysWhatIsMissingRatherThanImplyingZero()
    {
        QuotaRowViewModel row = Row(Window(id: "nimbus_quill", label: "nimbus_quill", usedPercent: null, labelIsProviderToken: true));

        Assert.Equal("nimbus_quill, usage not reported, no reset time reported, partial data", row.AccessibleName);
    }
}
