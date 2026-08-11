using System.Xml.Linq;

namespace AiUsageMonitor.Infrastructure.Tests;

public class ThemeDictionaryTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string ThemePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Themes", name + ".xaml");

    private static IReadOnlyList<string> KeysIn(string name) =>
        XDocument.Load(ThemePath(name))
            .Descendants()
            .Select(element => element.Attribute(Xaml + "Key")?.Value)
            .Where(key => key is not null)
            .Select(key => key!)
            .ToList();

    private static string ValueOf(string theme, string key) =>
        XDocument.Load(ThemePath(theme))
            .Descendants()
            .Single(element => element.Attribute(Xaml + "Key")?.Value == key)
            .Attribute("Color")!.Value;

    [Theory]
    [InlineData("Tokens")]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("HighContrast")]
    public void EveryDictionaryParsesAndDefinesKeys(string name) => Assert.NotEmpty(KeysIn(name));

    [Theory]
    [InlineData("Dark")]
    [InlineData("HighContrast")]
    public void EveryThemeDefinesTheSameKeysAsLight(string other)
    {
        HashSet<string> light = [.. KeysIn("Light")];
        HashSet<string> compared = [.. KeysIn(other)];

        Assert.Empty(light.Except(compared));
        Assert.Empty(compared.Except(light));
    }

    [Fact]
    public void NoKeyIsDefinedTwiceWithinADictionary()
    {
        foreach (string name in (string[])["Tokens", "Light", "Dark", "HighContrast"])
        {
            IReadOnlyList<string> keys = KeysIn(name);
            Assert.Equal(keys.Count, keys.Distinct().Count());
        }
    }

    [Theory]
    [InlineData("Light", "QuotaBarFillBrush", "#2B7CD3")]
    [InlineData("Dark", "QuotaBarFillBrush", "#3A96DD")]
    [InlineData("Light", "QuotaBarHighFillBrush", "#9A6600")]
    [InlineData("Dark", "QuotaBarHighFillBrush", "#B47F1E")]
    [InlineData("Light", "QuotaBarExhaustedFillBrush", "#C0453F")]
    [InlineData("Dark", "QuotaBarExhaustedFillBrush", "#E0685E")]
    [InlineData("Light", "ElapsedMarkerBrush", "#1B1B1B")]
    [InlineData("Dark", "ElapsedMarkerBrush", "#FFFFFF")]
    [InlineData("Light", "QuotaBarTrackBrush", "#E4E4E4")]
    [InlineData("Dark", "QuotaBarTrackBrush", "#3D3D3D")]
    public void ContrastCriticalColoursMatchTheApprovedTokens(string theme, string key, string expected) =>
        Assert.Equal(expected, ValueOf(theme, key), ignoreCase: true);

    [Fact]
    public void TheHighBandIsNotTheTextWeightWarningColour()
    {
        Assert.NotEqual(ValueOf("Dark", "QuotaBarHighFillBrush"), ValueOf("Dark", "StateWarnBrush"));
        Assert.NotEqual(ValueOf("Light", "QuotaBarHighFillBrush"), ValueOf("Light", "StateWarnBrush"));
    }
}
