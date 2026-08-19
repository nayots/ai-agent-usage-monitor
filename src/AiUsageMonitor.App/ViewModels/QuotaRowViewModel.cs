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
    private readonly bool _showPaceProjection;
    private DateTimeOffset? _lastTick;
    private string? _paceWarningText;

    public QuotaRowViewModel(
        QuotaWindow window,
        bool colorBarsByUsage,
        string? mechanism = null,
        bool showPaceProjection = true)
    {
        _window = window;
        ColorBarsByUsage = colorBarsByUsage;
        _showPaceProjection = showPaceProjection;
        DetailText = BuildDetailText(mechanism);
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

    /// <summary>The row's full detail, one fact per line, for the tooltip. Never null.</summary>
    public string DetailText { get; }

    public bool IsPartial => _window.IsPartial;

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

    public bool IsStale
    {
        get => _isStale;
        set
        {
            if (Set(ref _isStale, value))
            {
                RefreshPace();
            }
        }
    }

    public string? PaceWarningText
    {
        get => _paceWarningText;
        private set
        {
            if (Set(ref _paceWarningText, value))
            {
                Raise(nameof(HasPaceWarning));
                Raise(nameof(AccessibleName));
            }
        }
    }

    public bool HasPaceWarning => _paceWarningText is not null;

    public string AccessibleName
    {
        get
        {
            string usage = UsedText is null ? "usage not reported" : UsedText + " used";
            string reset = CountdownText is null ? "no reset time reported" : "resets in " + CountdownText;
            string exactReset = _window.ResetsAt is DateTimeOffset resetsAt
                ? ", resets at " + resetsAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                : string.Empty;
            string partial = IsPartial ? ", partial data" : string.Empty;
            string pace = _paceWarningText is null ? string.Empty : ", " + _paceWarningText;
            return $"{Label}, {usage}, {reset}{partial}{exactReset}{pace}";
        }
    }

    private string BuildDetailText(string? mechanism)
    {
        List<string> lines = [$"identifier: {_window.Id}"];

        if (!string.IsNullOrWhiteSpace(mechanism))
        {
            lines.Add($"mechanism: {mechanism}");
        }

        if (_window.ResetsAt is DateTimeOffset resetsAt)
        {
            lines.Add($"resets at: {resetsAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}");
        }

        if (_window.WindowDuration is not null)
        {
            lines.Add($"window duration: {QuotaFormatting.FormatCountdown(_window.WindowDuration)}");
        }

        if (IsPartial)
        {
            lines.Add("partial data: the provider did not supply a reset time or a window duration");
        }

        // Extra is safe to render only because every key and value is app-selected. Do not add
        // unreviewed provider data here: this tooltip would become a disclosure path.
        foreach ((string key, string value) in _window.Extra)
        {
            lines.Add($"{key}: {value}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Recomputes the locally derived values. Countdown and elapsed marker advance from the last
    /// known reset timestamp and never cost a provider call (PRD §14).
    /// </summary>
    public void Tick(DateTimeOffset now)
    {
        _lastTick = now;
        CountdownText = QuotaFormatting.FormatCountdown(_window.TimeUntilReset(now));
        ElapsedFraction = _window.ElapsedFraction(now);
        RefreshPace();
        Raise(nameof(AccessibleName));
    }

    private void RefreshPace()
    {
        if (!_showPaceProjection || _isStale || _lastTick is not DateTimeOffset now)
        {
            PaceWarningText = null;
            return;
        }

        PaceWarningText =
            QuotaPace.For(_window, now) is PaceProjection projection
            && QuotaFormatting.FormatProjectedShortfall(projection.Shortfall) is string shortfall
                ? $"At this pace, spent {shortfall} early"
                : null;
    }
}
