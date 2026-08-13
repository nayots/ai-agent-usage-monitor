using System.Collections.ObjectModel;
using System.Globalization;
using AiUsageMonitor.App.Interop;
using AiUsageMonitor.Infrastructure.Providers;
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

    // Evening and morning, an hour apart. Deliberately not the full 24: a schedule offering 03:00
    // as a start is offering a shape nobody wants, and a hand-edited file can hold anything anyway.
    private static readonly int[] QuietStartPresets = [1080, 1140, 1200, 1260, 1320, 1380];
    private static readonly int[] QuietEndPresets = [300, 360, 420, 480, 540, 600];

    private readonly SettingsService _settings;
    private readonly StartupRegistration _startup;
    private readonly IReadOnlyList<ProviderDescriptor> _providers;
    private readonly bool _globalHotkeyUnavailable;

    public SettingsViewModel(
        SettingsService settings,
        StartupRegistration startup,
        Action resetPosition,
        Action recheckProviders,
        Action openLogs,
        Action openDiagnostics,
        IReadOnlyList<ProviderDescriptor> providers,
        bool globalHotkeyUnavailable = false)
    {
        _settings = settings;
        _startup = startup;
        _providers = providers;
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

        AlertThresholdChoices = BuildAlertThresholdChoices();
        QuietHoursStarts = QuietTimes("quiet-start", QuietStartPresets, settings.Current.QuietHoursStartMinutes,
            minutes => _settings.Update(s => s with { QuietHoursStartMinutes = minutes }),
            () => _settings.Current.QuietHoursStartMinutes);
        QuietHoursEnds = QuietTimes("quiet-end", QuietEndPresets, settings.Current.QuietHoursEndMinutes,
            minutes => _settings.Update(s => s with { QuietHoursEndMinutes = minutes }),
            () => _settings.Current.QuietHoursEndMinutes);

        foreach (ProviderDescriptor provider in ProviderOrdering.Apply(_providers, settings.Current.ProviderOrder))
        {
            ProviderPreferences.Add(new ProviderPreferenceViewModel(provider, _settings, MoveProvider));
        }
        UpdateProviderMoveAvailability();

        ResetPositionCommand = new RelayCommand(resetPosition);
        RecheckProvidersCommand = new RelayCommand(recheckProviders);
        OpenLogsCommand = new RelayCommand(openLogs);
        OpenDiagnosticsCommand = new RelayCommand(openDiagnostics);

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

    public bool QuietHoursEnabled
    {
        get => _settings.Current.QuietHoursEnabled;
        set => _settings.Update(s => s with { QuietHoursEnabled = value });
    }

    public string AlertThresholdHintText => "100% always notifies, and is the only alert that makes a sound.";

    public string QuietHoursSummaryText =>
        $"Milestone alerts are held back between {TimeLabel(_settings.Current.QuietHoursStartMinutes)} "
        + $"and {TimeLabel(_settings.Current.QuietHoursEndMinutes)}. Reaching 100% still notifies.";

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

    public ObservableCollection<ChoiceViewModel> AlertThresholdChoices { get; }

    public ObservableCollection<ChoiceViewModel> QuietHoursStarts { get; }

    public ObservableCollection<ChoiceViewModel> QuietHoursEnds { get; }

    public ObservableCollection<ProviderPreferenceViewModel> ProviderPreferences { get; } = [];

    public string ProviderPreferencesHintText => "Hidden providers are not polled, and do not appear in the notification-area icon.";

    public RelayCommand ResetPositionCommand { get; }

    public RelayCommand RecheckProvidersCommand { get; }

    public RelayCommand OpenLogsCommand { get; }

    public RelayCommand OpenDiagnosticsCommand { get; }

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
    /// The clock presets, plus <paramref name="current"/> when a hand-edited file holds something
    /// else - the same rule as <see cref="Durations"/>, for the same reason.
    /// </summary>
    private static ObservableCollection<ChoiceViewModel> QuietTimes(
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

        return [.. values.Select(minutes => new ChoiceViewModel(TimeLabel(minutes), minutes, groupName, read, write))];
    }

    /// <summary>
    /// Rendered through the current culture, so a 12-hour locale reads 10 PM rather than 22:00.
    /// Folded into the day first, because the value can come from a hand-edited file.
    /// </summary>
    private static string TimeLabel(int minutes) =>
        TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(((minutes % 1440) + 1440) % 1440))
            .ToString("t", CultureInfo.CurrentCulture);

    /// <summary>
    /// The presets, plus the user's own ladder when it matches none of them. A list typed into the
    /// settings file by hand is a deliberate choice, and a window that silently reselected the
    /// nearest preset would change how often someone is told without telling them.
    /// </summary>
    private ObservableCollection<ChoiceViewModel> BuildAlertThresholdChoices()
    {
        ObservableCollection<ChoiceViewModel> choices =
        [
            .. AlertThresholdPresets.All.Select(preset => new ChoiceViewModel(
                preset.Label,
                preset.Id,
                "thresholds",
                () => AlertThresholdPresets.IdFor(_settings.Current.EffectiveAlertThresholds),
                _ => _settings.Update(s => s with { AlertThresholds = preset.Thresholds })))
        ];

        IReadOnlyList<int> current = _settings.Current.EffectiveAlertThresholds;
        if (AlertThresholdPresets.IdFor(current) < 0)
        {
            // Read-only by construction: selecting it would write back what is already there, and
            // it disappears on the next rebuild once a real preset is chosen.
            choices.Add(new ChoiceViewModel(
                AlertThresholdPresets.CustomLabel(current),
                -1,
                "thresholds",
                () => AlertThresholdPresets.IdFor(_settings.Current.EffectiveAlertThresholds),
                _ => { }));
        }

        return choices;
    }

    /// <summary>
    /// The custom entry has to appear and disappear as the ladder changes, which no amount of
    /// refreshing an existing list can do - so this list, alone among the choice collections, can
    /// need rebuilding.
    /// <para>
    /// Rebuilt only when that entry actually differs, never on every settings change. Clearing the
    /// collection tears down the radio buttons in it, and a settings change can come from anywhere
    /// - including from the radio button being clicked at that moment, and from a theme toggle
    /// that has nothing to do with this list.
    /// </para>
    /// </summary>
    private void RefreshAlertThresholdChoices()
    {
        IReadOnlyList<int> current = _settings.Current.EffectiveAlertThresholds;
        string? wanted = AlertThresholdPresets.IdFor(current) < 0
            ? AlertThresholdPresets.CustomLabel(current)
            : null;
        ChoiceViewModel? custom = AlertThresholdChoices.FirstOrDefault(choice => choice.Value == -1);

        if (custom?.Label != wanted)
        {
            AlertThresholdChoices.Clear();

            foreach (ChoiceViewModel choice in BuildAlertThresholdChoices())
            {
                AlertThresholdChoices.Add(choice);
            }

            return;
        }

        foreach (ChoiceViewModel choice in AlertThresholdChoices)
        {
            choice.Refresh();
        }
    }

    /// <summary>
    /// A null property name tells WPF every binding on this object is out of date, which is exactly
    /// true: a settings change can come from anywhere, and every property here is a projection of
    /// the same record. The choice lists are separate objects and are refreshed by hand.
    /// </summary>
    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        Raise(null);

        RefreshAlertThresholdChoices();

        foreach (ChoiceViewModel choice in Themes
            .Concat(Densities)
            .Concat(RefreshIntervals)
            .Concat(StaleThresholds)
            .Concat(QuietHoursStarts)
            .Concat(QuietHoursEnds))
        {
            choice.Refresh();
        }

        RebuildProviderPreferences(settings);

        foreach (ProviderPreferenceViewModel provider in ProviderPreferences)
        {
            provider.Refresh();
        }
    }

    private void OnPersistenceStateChanged(object? sender, EventArgs e) =>
        Raise(nameof(HasPersistenceWarning));

    private void MoveProvider(ProviderPreferenceViewModel provider, int offset)
    {
        int index = ProviderPreferences.IndexOf(provider);
        int target = index + offset;
        if (target < 0 || target >= ProviderPreferences.Count)
        {
            return;
        }

        ProviderPreferences.Move(index, target);
        _settings.Update(settings => settings with { ProviderOrder = [.. ProviderPreferences.Select(preference => preference.Key)] });
    }

    private void RebuildProviderPreferences(AppSettings settings)
    {
        IReadOnlyList<ProviderDescriptor> ordered = ProviderOrdering.Apply(_providers, settings.ProviderOrder);
        for (int target = 0; target < ordered.Count; target++)
        {
            ProviderPreferenceViewModel preference = ProviderPreferences.Single(item =>
                StringComparer.OrdinalIgnoreCase.Equals(item.Key, ordered[target].Key));
            int current = ProviderPreferences.IndexOf(preference);
            if (current != target)
            {
                ProviderPreferences.Move(current, target);
            }
        }

        UpdateProviderMoveAvailability();
    }

    private void UpdateProviderMoveAvailability()
    {
        for (int index = 0; index < ProviderPreferences.Count; index++)
        {
            ProviderPreferences[index].SetMoveAvailability(index > 0, index < ProviderPreferences.Count - 1);
        }
    }
}
