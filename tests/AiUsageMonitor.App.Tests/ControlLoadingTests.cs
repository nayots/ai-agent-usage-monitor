using System.Windows;
using System.Windows.Controls;
using AiUsageMonitor.App.Controls;
using AiUsageMonitor.Domain;

namespace AiUsageMonitor.App.Tests;

/// <summary>
/// These exist because a clean build and a green suite once shipped an application that opened no
/// window: a dependency property registered with a null default for a value type throws inside a
/// static initializer, which surfaces only when XAML first references the control.
/// </summary>
[Collection("wpf")]
public class ControlLoadingTests(WpfFixture wpf)
{
    [Fact]
    public void EveryControlTypeInitialisesAndAppliesItsStyle() => wpf.Invoke(() =>
    {
        foreach (FrameworkElement control in (FrameworkElement[])
        [
            new QuotaBar(),
            new StateGlyph(),
            new StateChip { Label = "Connected", State = ConnectionState.Connected },
            new TierBadge { Tier = MechanismTier.Unofficial }
        ])
        {
            Measured(control);
        }
    });

    [Theory]
    [InlineData("Themes/Light.xaml")]
    [InlineData("Themes/Dark.xaml")]
    [InlineData("Themes/HighContrast.xaml")]
    public void EveryThemeDictionaryLoadsAsRealXaml(string path) => wpf.Invoke(() =>
    {
        ResourceDictionary dictionary = new()
        {
            Source = new Uri($"pack://application:,,,/AiUsageMonitor.App;component/{path}", UriKind.Absolute)
        };

        Assert.NotEmpty(dictionary.Keys);
    });

    [Fact]
    public void AQuotaBarRendersEveryBandWithoutThrowing() => wpf.Invoke(() =>
    {
        foreach (double? used in (double?[])[null, 0, 25, 74, 75, 99, 100, 150])
        {
            Measured(new QuotaBar { UsedPercent = used, ElapsedFraction = 0.5, Width = 300 });
        }
    });

    /// <summary>Measure and arrange force the template to expand and OnRender to be reachable.</summary>
    internal static T Measured<T>(T element) where T : FrameworkElement
    {
        Border host = new() { Child = element };
        host.Measure(new Size(360, 520));
        host.Arrange(new Rect(0, 0, 360, 520));
        host.UpdateLayout();
        return element;
    }
}
