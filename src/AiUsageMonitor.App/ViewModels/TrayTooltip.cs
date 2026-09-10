using System.Text;

namespace AiUsageMonitor.App.ViewModels;

/// <summary>
/// The hover text for whichever provider the icon is currently showing. This is where every figure
/// the glyph gave up now lives: the provider's name, then <em>all</em> of its windows rather than
/// the worst one, because one icon showing one provider gets the whole budget to itself.
/// </summary>
public static class TrayTooltip
{
    /// <summary>
    /// <c>NOTIFYICONDATA.szTip</c> is a fixed 128-character buffer and the marshaller throws on
    /// anything longer, so this is a hard limit rather than a style preference.
    /// </summary>
    public const int MaxLength = 127;

    private const string Break = "\r\n";

    public static string Compose(string providerName, IEnumerable<(string Label, string? Used, string? Resets)> windows)
    {
        StringBuilder text = new(providerName.Length > MaxLength ? providerName[..MaxLength] : providerName);

        foreach ((string label, string? used, string? resets) in windows)
        {
            string line = used is null
                ? $"{label} —"
                : resets is null ? $"{label} {used}" : $"{label} {used} · {resets}";

            // Whole lines only. A truncated line reads as a corrupted reading; a missing line just
            // reads as a shorter list, which is what it is.
            if (text.Length + Break.Length + line.Length > MaxLength)
            {
                break;
            }

            text.Append(Break).Append(line);
        }

        return text.ToString();
    }
}
