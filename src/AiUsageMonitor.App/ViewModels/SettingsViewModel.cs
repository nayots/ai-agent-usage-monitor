using System.Collections.ObjectModel;
using AiUsageMonitor.App.Interop;
using AiUsageMonitor.Infrastructure.Settings;

namespace AiUsageMonitor.App.ViewModels;

/// <summary>
/// The settings window's view model. Every property reads through to
/// <see cref="SettingsService.Current"/> and writes through <see cref="SettingsService.Update"/>,
/// so there is no working copy to get out of step and no Apply button to forget.
/// </summary>
public sealed class SettingsViewModel : ObservableObject, IDisposable
{
    private static readonly int[] RefreshPresets = [15, 30, 60, 120, 300, 600];
    private static readonly int[] StalePresets = [60, 120, 300, 600, 1800, 3600];

    private readonly SettingsService _settings;
    private readonly StartupRegistration _startup;
    private readonly bool _globalHotkeyUnavailable;

    public SettingsViewModel(
        SettingsService settings,
        StartupRegistration startup,
        Action resetPosition,
        Action recheckProviders,
        Action openLogs,
        bool globalHotkeyUnavailable = false)
    {
        _settings = settings;
        _startup = startup;
        _globalHotkeyUnavailable = globalHotkeyUnavailable;

        Themes =
        [
            Theme("System", ThemePreference.System),
            Theme("Light", ThemePreference.Light),
            Theme("Dark", ThemePreference.Dark)
        ];

        Densities =
        [
            Density("Standard", WidgetDensity.Normal),
            Density("Compact", WidgetDensity.Compact)
        ];

        RefreshIntervals = Durations(
            "refresh",
            RefreshPresets,
            settings.Current.RefreshIntervalSeconds,
            seconds => _settings.Update(s => s with { RefreshIntervalSeconds = seconds }),
            () => _settings.Current.RefreshIntervalSeconds);

        StaleThresholds = Durations(
            "stale",
            StalePresets,
            settings.Current.StaleAfterSeconds,
            seconds => _settings.Update(s => s with { StaleAfterSeconds = seconds }),
            () => _settings.Current.StaleAfterSeconds);

        ResetPositionCommand = new RelayCommand(resetPosition);
        RecheckProvidersCommand = new RelayCommand(recheckProviders);
        OpenLogsCommand = new RelayCommand(openLogs);

        _settings.Changed += OnSettingsChanged;
        _settings.PersistenceStateChanged += OnPersistenceStateChanged;
    }

    // No AlwaysOnTop here. Pinning lives on the title bar alone: it is session state, and a
    // settings window is where someone goes to change how the widget works from now on - the wrong
    // place to offer something that is forgotten when the app closes.

    public bool ColorBarsByUsage
    {
        get => _settings.Current.ColorBarsByUsage;
        set => _settings.Update(s => s with { ColorBarsByUsage = value });
    }

    public bool NotifyOnQuotaEvents
    {
        get => _settings.Current.NotifyOnQuotaEvents;
        set => _settings.Update(s => s with { NotifyOnQuotaEvents = value });
    }

    public bool GlobalHotkeyEnabled
    {
        get => _settings.Current.GlobalHotkeyEnabled;
        set => _settings.Update(s => s with { GlobalHotkeyEnabled = value });
    }

    public string GlobalHotkeyLabel => "Ctrl+Alt+Q";

    public string? GlobalHotkeyUnavailableReason => _globalHotkeyUnavailable
        ? "Unavailable: another application already uses this shortcut."
        : null;

    public bool HasGlobalHotkeyWarning => GlobalHotkeyUnavailableReason is not null;

    public bool ShowUnavailableProviders
    {
        get => _settings.Current.ShowUnavailableProviders;
        set => _settings.Update(s => s with { ShowUnavailableProviders = value });
    }

    /// <summary>
    /// Reads from the registry rather than from the settings file, because the registry is where
    /// the fact lives. A settings file copied to another machine, or an app the user moved, would
    /// otherwise show a checkbox that does not match what Windows will actually do.
    /// </summary>
    public bool StartWithWindows
    {
        get => _startup.IsEnabled;
        set
        {
            if (value)
            {
                _startup.Enable();
            }
            else
            {
                _startup.Disable();
            }

            _settings.Update(s => s with { StartWithWindows = _startup.IsEnabled });
            Raise();
        }
    }

    public bool CanStartWithWindows => _startup.IsSupported;

    public bool HasPersistenceWarning => _settings.PersistenceFailed;

    public bool HasStaleThresholdWarning => _settings.Current.StaleAfter <= _settings.Current.RefreshInterval;

    public string StaleThresholdWarningText => "Cards will always look stale — this is shorter than the refresh interval.";

    public string PersistenceWarningText => "Changes apply to this session only — the settings file could not be saved.";

    public string? StartWithWindowsUnavailableReason => _startup.IsSupported
        ? null
        : "Unavailable: this build cannot determine its own location.";

    public IReadOnlyList<ChoiceViewModel> Themes { get; }

    public IReadOnlyList<ChoiceViewModel> Densities { get; }

    public ObservableCollection<ChoiceViewModel> RefreshIntervals { get; }

    public ObservableCollection<ChoiceViewModel> StaleThresholds { get; }

    public RelayCommand ResetPositionCommand { get; }

    public RelayCommand RecheckProvidersCommand { get; }

    public RelayCommand OpenLogsCommand { get; }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _settings.PersistenceStateChanged -= OnPersistenceStateChanged;
    }

    private ChoiceViewModel Theme(string label, ThemePreference preference) => new(
        label,
        (int)preference,
        "theme",
        () => (int)_settings.Current.Theme,
        value => _settings.Update(s => s with { Theme = (ThemePreference)value }));

    private ChoiceViewModel Density(string label, WidgetDensity density) => new(
        label,
        (int)density,
        "density",
        () => (int)_settings.Current.Density,
        value => _settings.Update(s => s with { Density = (WidgetDensity)value }));

    /// <summary>
    /// The presets, plus <paramref name="current"/> when a hand-edited settings file holds
    /// something else. A value the user typed into the file deliberately must not vanish because
    /// this window offers a shorter list.
    /// </summary>
    private static ObservableCollection<ChoiceViewModel> Durations(
        string groupName,
        IReadOnlyList<int> presets,
        int current,
        Action<int> write,
        Func<int> read)
    {
        List<int> values = [.. presets];

        if (!values.Contains(current))
        {
            values.Add(current);
            values.Sort();
        }

        return [.. values.Select(seconds => new ChoiceViewModel(DurationLabel(seconds), seconds, groupName, read, write))];
    }

    private static string DurationLabel(int seconds) => seconds < 60
        ? seconds + "s"
        : seconds % 60 == 0 ? seconds / 60 + "m" : seconds / 60 + "m " + seconds % 60 + "s";

    /// <summary>
    /// A null property name tells WPF every binding on this object is out of date, which is exactly
    /// true: a settings change can come from anywhere, and every property here is a projection of
    /// the same record. The choice lists are separate objects and are refreshed by hand.
    /// </summary>
    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        Raise(null);

        foreach (ChoiceViewModel choice in Themes.Concat(Densities).Concat(RefreshIntervals).Concat(StaleThresholds))
        {
            choice.Refresh();
        }
    }

    private void OnPersistenceStateChanged(object? sender, EventArgs e) =>
        Raise(nameof(HasPersistenceWarning));
}
