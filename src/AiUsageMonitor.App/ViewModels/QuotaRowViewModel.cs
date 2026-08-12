using System.Globalization;
using AiUsageMonitor.Domain;

namespace AiUsageMonitor.App.ViewModels;

/// <summary>
/// One provider-reported quota window, as the row renders it. Nothing here interprets a
/// provider's semantics: the label, the identifier and the percentage are the provider's, and a
/// field the provider did not supply stays null so the view omits it rather than drawing a
/// placeholder (PRD §7.3).
/// </summary>
public sealed class QuotaRowViewModel : ObservableObject
{
    private readonly QuotaWindow _window;
    private string? _countdownText;
    private double? _elapsedFraction;
    private bool _isStale;

    public QuotaRowViewModel(QuotaWindow window, bool colorBarsByUsage)
    {
        _window = window;
        ColorBarsByUsage = colorBarsByUsage;
    }

    public string Label => QuotaOrdering.DisplayLabel(_window);

    /// <summary>
    /// True when the label is the provider's raw identifier because it resolved to no duration.
    /// The view renders these in a monospace chip so a provider term is never mistaken for a
    /// label this application authored (PRD §7.2 item 10).
    /// </summary>
    public bool IsProviderToken => _window.LabelIsProviderToken;

    /// <summary>
    /// The provider's own identifier for this window. Rows are rebuilt from scratch on every
    /// snapshot, so anything that has to remember something about a window across snapshots - the
    /// alert watcher's high-water rung, for one - has to key off this rather than off the row
    /// instance or its position in the list.
    /// </summary>
    public string Id => _window.Id;

    /// <summary>The provider's identifier stays reachable for every window, resolved or not.</summary>
    public string IdentifierTooltip => $"identifier: {_window.Id}";

    public double? UsedPercent => _window.UsedPercent;

    public string? UsedText => _window.UsedPercent is double used
        ? Math.Round(used, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "%"
        : null;

    /// <summary>
    /// 100% used is the exhausted treatment whether or not bar tone by usage is on, per PRD §16.1.
    /// Neither provider reports a separate rate-limit-reached flag today, so the provider's own
    /// percentage is the signal — nothing is inferred beyond it.
    /// </summary>
    public bool IsExhausted => _window.UsedPercent >= 100;

    public bool ColorBarsByUsage { get; }

    public string? CountdownText { get => _countdownText; private set => Set(ref _countdownText, value); }

    public double? ElapsedFraction { get => _elapsedFraction; private set => Set(ref _elapsedFraction, value); }

    public bool IsStale { get => _isStale; set => Set(ref _isStale, value); }

    public string AccessibleName
    {
        get
        {
            string usage = UsedText is null ? "usage not reported" : UsedText + " used";
            string reset = CountdownText is null ? "no reset time reported" : "resets in " + CountdownText;
            return $"{Label}, {usage}, {reset}";
        }
    }

    /// <summary>
    /// Recomputes the locally derived values. Countdown and elapsed marker advance from the last
    /// known reset timestamp and never cost a provider call (PRD §14).
    /// </summary>
    public void Tick(DateTimeOffset now)
    {
        CountdownText = QuotaFormatting.FormatCountdown(_window.TimeUntilReset(now));
        ElapsedFraction = _window.ElapsedFraction(now);
        Raise(nameof(AccessibleName));
    }
}
