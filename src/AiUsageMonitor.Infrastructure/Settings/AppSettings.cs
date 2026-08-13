using System.Text.Json.Serialization;
using AiUsageMonitor.Domain;

namespace AiUsageMonitor.Infrastructure.Settings;

/// <summary>Which theme the user asked for. <see cref="System"/> follows the OS.</summary>
public enum ThemePreference
{
    System,
    Light,
    Dark
}

/// <summary>Widget density. Compact drops metadata in the order recorded in docs/design/rationale.md.</summary>
public enum WidgetDensity
{
    Normal,
    Compact
}

/// <summary>
/// Application-owned settings (PRD §19). Provider-owned configuration is never stored here and
/// is never modified by this application.
/// </summary>
public sealed record AppSettings
{
    public ThemePreference Theme { get; init; } = ThemePreference.System;

    /// <summary>
    /// Bar tone by usage band, bounded by PRD §16.1. On by default. When off, every bar below
    /// 100% uses the single accent fill; 100% still renders as exhausted either way.
    /// </summary>
    public bool ColorBarsByUsage { get; init; } = true;

    /// <summary>
    /// Notification-area balloons when a quota window crosses a milestone, reaches its limit, comes
    /// back, or a provider stops reporting. On by default: a widget that is hidden most of the time
    /// cannot tell anyone anything, and the milestones are the one thing worth interrupting for.
    /// <para>
    /// This gates delivery only. Off does not stop the alerts being computed - see
    /// <c>WidgetWindow.DeliverAlerts</c> for why that distinction is load-bearing.
    /// </para>
    /// </summary>
    public bool NotifyOnQuotaEvents { get; init; } = true;

    /// <summary>
    /// Whether the widget stays above other windows, and with it the exemption from the focus-loss
    /// dismissal in PRD §17.
    /// <para>
    /// Deliberately not persisted. It is the only setting here that answers "what am I doing right
    /// now" rather than "how do I want this to work": pinning is what someone does while watching a
    /// quota drain during a long run, and a pin that outlived the run would leave the widget on top
    /// of everything the next day, with no memory of having asked for it. It is still carried on
    /// this record rather than held beside it, so the title bar's pin and the settings window's
    /// checkbox stay two views of one value.
    /// </para>
    /// </summary>
    [JsonIgnore]
    public bool AlwaysOnTop { get; init; }

    public bool StartWithWindows { get; init; }

    /// <summary>Ctrl+Alt+Q shows or hides the widget from anywhere. On by default.</summary>
    public bool GlobalHotkeyEnabled { get; init; } = true;

    public WidgetDensity Density { get; init; } = WidgetDensity.Normal;

    /// <summary>PRD §15: an unavailable provider keeps its card unless the user hides it.</summary>
    public bool ShowUnavailableProviders { get; init; } = true;

    /// <summary>Persisted as a plain number so the settings file stays readable and hand-editable.</summary>
    public int StaleAfterSeconds { get; init; } = 300;

    /// <summary>
    /// <see cref="StaleAfterSeconds"/> clamped to a range that cannot produce nonsense: a
    /// zero threshold would mark every snapshot stale on arrival, and an unbounded one would
    /// never mark anything stale at all. Clamped rather than rejected because a hand-edited
    /// settings file must never prevent the application from starting.
    /// </summary>
    [JsonIgnore]
    public TimeSpan StaleAfter => TimeSpan.FromSeconds(Math.Clamp(StaleAfterSeconds, 30, 3600));

    /// <summary>Persisted as a plain number so the settings file stays readable and hand-editable.</summary>
    public int RefreshIntervalSeconds { get; init; } = 60;

    /// <summary>
    /// <see cref="RefreshIntervalSeconds"/> clamped for the same reason as <see cref="StaleAfter"/>:
    /// a hand-edited settings file must never stop the application starting, and a zero-second
    /// interval would poll a provider in a tight loop.
    /// </summary>
    [JsonIgnore]
    public TimeSpan RefreshInterval => TimeSpan.FromSeconds(Math.Clamp(RefreshIntervalSeconds, 15, 3600));

    /// <summary>
    /// The percentages worth a balloon. Persisted as a plain array of numbers so the file stays
    /// hand-editable; read through <see cref="EffectiveAlertThresholds"/>, never directly.
    /// </summary>
    public IReadOnlyList<int> AlertThresholds { get; init; } = QuotaMilestones.Ladder;

    /// <summary>
    /// <see cref="AlertThresholds"/> made safe to use. Derived, never written to the file - a
    /// sanitized copy on disk would quietly rewrite what the user typed there.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<int> EffectiveAlertThresholds => QuotaMilestones.Sanitize(AlertThresholds);

    /// <summary>
    /// Whether milestone balloons are held back overnight. Off by default: a widget that goes quiet
    /// without being asked to is a widget that looks broken.
    /// </summary>
    public bool QuietHoursEnabled { get; init; }

    /// <summary>Minutes from local midnight, stored plainly so the file stays hand-editable. 22:00.</summary>
    public int QuietHoursStartMinutes { get; init; } = 1320;

    /// <inheritdoc cref="QuietHoursStartMinutes"/>
    public int QuietHoursEndMinutes { get; init; } = 420;

    /// <summary>The three fields above as the value that knows how to answer questions about them.</summary>
    [JsonIgnore]
    public QuietHours QuietHours => new(QuietHoursEnabled, QuietHoursStartMinutes, QuietHoursEndMinutes);

    public IReadOnlyList<string> ProviderOrder { get; init; } = [];

    public IReadOnlyList<string> HiddenProviders { get; init; } = [];

    public IReadOnlyDictionary<string, int> ProviderRefreshSeconds { get; init; } =
        new Dictionary<string, int>();

    public bool IsProviderHidden(string providerKey) =>
        HiddenProviders.Contains(providerKey, StringComparer.OrdinalIgnoreCase);

    public int? RefreshSecondsOverrideFor(string providerKey)
    {
        foreach ((string key, int seconds) in ProviderRefreshSeconds)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(key, providerKey))
            {
                return seconds == 0 ? null : seconds;
            }
        }

        return null;
    }

    public TimeSpan RefreshIntervalFor(string providerKey) =>
        RefreshSecondsOverrideFor(providerKey) is int seconds
            ? TimeSpan.FromSeconds(Math.Clamp(seconds, 15, 3600))
            : RefreshInterval;

    /// <summary>
    /// Last known window position, or null on a first run. Null rather than 0: a widget that has
    /// never been placed must be centred, and 0,0 is a real position a user could have chosen
    /// (PRD §17).
    /// </summary>
    public double? WindowLeft { get; init; }

    /// <inheritdoc cref="WindowLeft"/>
    public double? WindowTop { get; init; }

    /// <summary>
    /// Whether the "the widget is in the notification area" balloon has been shown. Application
    /// state rather than a preference, and deliberately not offered in the settings window: it
    /// answers "has this user been told once", which no one needs to configure.
    /// </summary>
    public bool TrayHintShown { get; init; }

    public static AppSettings Default { get; } = new();
}
