using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
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

    /// <summary>
    /// The unofficial mark is a filled triangle, per the design's CSS border triangle. Stroking it
    /// instead is what made it read as a chevron riding above the capitals: a 1px pen straddles the
    /// path, so the base landed on a half-pixel and faded out while the miter join at the apex
    /// spiked a pixel above the box. Both symptoms follow from the shape outgrowing its 7x6
    /// allotment, so that is what this asserts.
    /// </summary>
    [Fact]
    public void TheUnofficialMarkIsFilledAndKeepsItsSevenBySixShape() => wpf.Invoke(() =>
    {
        TierBadge badge = Measured(new TierBadge { Tier = MechanismTier.Unofficial });
        Polygon triangle = Descendants(badge).OfType<Polygon>().Single();

        Assert.NotNull(triangle.Fill);
        Assert.Null(triangle.Stroke);
        Assert.Equal(new Size(7, 6), triangle.DesiredSize);
    });

    /// <summary>
    /// The triangle is drawn a pixel above where layout puts it. Centring a box centres a disc but
    /// not a triangle: the mass of an up-pointing one is at its base, and its centroid is a third of
    /// its height below the box's middle. Measured against the ink of the capitals beside it, the
    /// box-centred triangle sat 1.2px low - visible at this size - and the lift brings it to 0.2px,
    /// matching the OFFICIAL dot. Asserted because a deleted transform changes nothing that builds,
    /// throws, or measures differently; it only looks wrong.
    /// </summary>
    [Fact]
    public void TheUnofficialMarkIsLiftedOffTheCentreOfItsBox() => wpf.Invoke(() =>
    {
        TierBadge badge = Measured(new TierBadge { Tier = MechanismTier.Unofficial });
        Polygon triangle = Descendants(badge).OfType<Polygon>().Single();
        TranslateTransform lift = Assert.IsType<TranslateTransform>(triangle.RenderTransform);

        Assert.Equal(-1, lift.Y);
        Assert.Equal(0, lift.X);
    });

    /// <summary>The disc is symmetric about its centre, so it takes no optical correction.</summary>
    [Fact]
    public void TheOfficialMarkIsNotLifted() => wpf.Invoke(() =>
    {
        TierBadge badge = Measured(new TierBadge { Tier = MechanismTier.Official });
        Ellipse dot = Descendants(badge).OfType<Ellipse>().Single();

        Assert.Equal(Transform.Identity, dot.RenderTransform);
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            yield return child;

            foreach (DependencyObject descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

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
