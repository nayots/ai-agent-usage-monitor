using System.Globalization;
using System.Linq;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Theming;

namespace AiUsageMonitor.App.ViewModels;

/// <summary>What the band of a frame is showing, which is decided by whether a number would say anything.</summary>
public enum TrayFrameKind
{
    /// <summary>A percentage worth printing. The only kind whose band shows figures.</summary>
    Reading,

    /// <summary>At or beyond the limit. The figure is always 100, so the band shows the name instead.</summary>
    AtLimit,

    /// <summary>The provider's mechanism failed. There is no figure, and which tool broke is the question.</summary>
    Failed,

    /// <summary>Nothing retrieved yet. There is no figure <em>yet</em>, and never a zero.</summary>
    Waiting
}

/// <param name="Monogram">The provider's two letters, from <c>ProviderDescriptor.Monogram</c>.</param>
/// <param name="Digits">The figures to print, or null when the band shows the name.</param>
/// <param name="UsedPercent">Bar fill. Null draws bare track - never a zero-width fill.</param>
/// <param name="Fill">The bar's band, resolved by the same selector the widget's own rows use.</param>
public readonly record struct TrayGlyphFrame(
    string Monogram,
    string? Digits,
    double? UsedPercent,
    QuotaBarFill Fill,
    TrayFrameKind Kind)
{
    /// <summary>
    /// True for the three kinds whose band always carries the name. They take no part in the
    /// name-then-number alternation, which is why a parked glyph holds perfectly still.
    /// </summary>
    public bool NamesItselfAlways => Kind is not TrayFrameKind.Reading;
}

/// <summary>
/// One frame per provider the user can see, in card order. Reads the cards rather than the
/// providers, so a hidden card hides its frame and a stale card greys here exactly as it does on
/// screen.
/// </summary>
public sealed class TrayGlyphState
{
    public static readonly TrayGlyphState Empty = new([]);

    public TrayGlyphState(IReadOnlyList<TrayGlyphFrame> frames) => Frames = frames;

    public IReadOnlyList<TrayGlyphFrame> Frames { get; }

    /// <summary>False when there is nothing truthful to draw; the caller keeps the static icon.</summary>
    public bool HasContent => Frames.Count > 0;

    public bool Matches(TrayGlyphState other) => Frames.SequenceEqual(other.Frames);

    public static TrayGlyphState From(IEnumerable<ProviderCardViewModel> cards)
    {
        List<TrayGlyphFrame> frames = [];

        foreach (ProviderCardViewModel card in cards)
        {
            // A tool that is not on this machine has no quota, so it gets no turn - even when its
            // card is visible. Sixteen pixels cannot afford a rotation slot that reports nothing.
            if (card.IsHiddenByFilter || card.State is ConnectionState.NotInstalled or ConnectionState.Unsupported)
            {
                continue;
            }

            if (card.State is ConnectionState.Error or ConnectionState.Unavailable)
            {
                frames.Add(new(card.Monogram, null, null, QuotaBarFill.Accent, TrayFrameKind.Failed));
                continue;
            }

            QuotaRowViewModel? worst = card.Windows
                .Where(row => row.UsedPercent is not null)
                .OrderByDescending(row => row.UsedPercent)
                .FirstOrDefault();

            if (worst?.UsedPercent is not double used)
            {
                frames.Add(new(card.Monogram, null, null, QuotaBarFill.Accent, TrayFrameKind.Waiting));
                continue;
            }

            QuotaBarFill fill = QuotaBarFillSelector.Select(used, limitReached: false, worst.ColorBarsByUsage, worst.IsStale);

            frames.Add(used >= QuotaBarFillSelector.ExhaustedBandStartPercent
                ? new(card.Monogram, null, used, fill, TrayFrameKind.AtLimit)
                : new(card.Monogram, DigitsFor(used), used, fill, TrayFrameKind.Reading));
        }

        return new TrayGlyphState(frames);
    }

    /// <summary>
    /// Rounded exactly as the card rounds it, so the glyph and the row never disagree by a point.
    /// A reading below 100 that rounds to 100 says 99 instead, so the glyph never claims a limit
    /// the user has not hit - the limit has its own frame and its own drawing.
    /// </summary>
    private static string DigitsFor(double used)
    {
        string rounded = Math.Round(used, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        return rounded == "100" ? "99" : rounded;
    }
}
