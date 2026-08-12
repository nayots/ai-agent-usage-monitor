using System.Globalization;
using System.Linq;
using AiUsageMonitor.App.Interop;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Theming;

namespace AiUsageMonitor.App.ViewModels;

/// <summary>
/// Everything the notification-area glyph shows, derived from the cards the widget is already
/// showing. Deliberately not a record: the generated equality would compare <see cref="Bars"/> by
/// reference and quietly report every rebuild as a change, which is the opposite of what
/// <see cref="Matches"/> exists for.
/// </summary>
public sealed class TrayGlyphState
{
    public static readonly TrayGlyphState Empty = new([], null, false, TrayOverlay.None);

    public TrayGlyphState(IReadOnlyList<TrayGlyphBar> bars, string? digits, bool digitsAreStale, TrayOverlay overlay)
    {
        Bars = bars;
        Digits = digits;
        DigitsAreStale = digitsAreStale;
        Overlay = overlay;
    }

    /// <summary>One bar per quota window, in card order, across every card the user can see.</summary>
    public IReadOnlyList<TrayGlyphBar> Bars { get; }

    /// <summary>The worst percentage, or null when there is nothing truthful to put there.</summary>
    public string? Digits { get; }

    /// <summary>True when the window that produced <see cref="Digits"/> is showing stale data.</summary>
    public bool DigitsAreStale { get; }

    public TrayOverlay Overlay { get; }

    /// <summary>
    /// False when the glyph would say nothing at all - no windows retrieved yet and no failure to
    /// report. The caller leaves the static icon in place rather than replacing it with a blank
    /// square.
    /// </summary>
    public bool HasContent => Bars.Count > 0 || Overlay != TrayOverlay.None;

    public bool Matches(TrayGlyphState other) =>
        Digits == other.Digits
        && DigitsAreStale == other.DigitsAreStale
        && Overlay == other.Overlay
        && Bars.SequenceEqual(other.Bars);

    /// <summary>
    /// Reads the cards, never the providers. Hiding an absent provider hides its bars too, bar
    /// tone follows the same selector the widget's own bars use, and a stale card's bars go grey
    /// here exactly as they do on screen - the glyph is the widget in sixteen pixels, not a second
    /// opinion about the same data.
    /// </summary>
    public static TrayGlyphState From(IEnumerable<ProviderCardViewModel> cards)
    {
        List<TrayGlyphBar> bars = [];
        double? highest = null;
        bool highestIsStale = false;
        bool exhausted = false;
        bool failing = false;

        foreach (ProviderCardViewModel card in cards)
        {
            if (card.IsHiddenByFilter)
            {
                continue;
            }

            failing |= card.State is ConnectionState.Error or ConnectionState.Unavailable;
            bool startsGroup = bars.Count > 0;

            foreach (QuotaRowViewModel row in card.Windows)
            {
                bars.Add(new TrayGlyphBar(
                    row.UsedPercent,
                    QuotaBarFillSelector.Select(row.UsedPercent, limitReached: false, row.ColorBarsByUsage, row.IsStale),
                    startsGroup));

                startsGroup = false;
                exhausted |= row.IsExhausted;

                if (row.UsedPercent is double used && (highest is not double best || used > best))
                {
                    highest = used;
                    highestIsStale = row.IsStale;
                }
            }
        }

        // An error outranks an exhausted window: it means the bars themselves may no longer be
        // true, which the user needs to know before they read anything else off the glyph.
        TrayOverlay overlay = failing
            ? TrayOverlay.Error
            : exhausted ? TrayOverlay.Alert : TrayOverlay.None;

        return new TrayGlyphState(bars, DigitsFor(highest), highestIsStale, overlay);
    }

    /// <summary>
    /// Rounded exactly as the card rounds it, so the glyph and the row never disagree by a point.
    /// Three characters do not fit in sixteen pixels and are dropped rather than abbreviated: at
    /// 100 the bar is already full and the alert triangle is already up, and below that - a 99.6
    /// that rounds to 100 - no number at all beats one that claims a limit the user has not hit.
    /// </summary>
    private static string? DigitsFor(double? highest)
    {
        if (highest is not double top)
        {
            return null;
        }

        string rounded = Math.Round(top, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        return rounded.Length <= 2 ? rounded : null;
    }
}
