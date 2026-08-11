using System.Text.Json.Serialization;

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

    public bool AlwaysOnTop { get; init; }

    public bool StartWithWindows { get; init; }

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

    public static AppSettings Default { get; } = new();
}
