using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using AiUsageMonitor.App.Interop;
using AiUsageMonitor.Infrastructure.Providers;
using AiUsageMonitor.Infrastructure.Settings;
using AiUsageMonitor.Infrastructure.Updates;

namespace AiUsageMonitor.App.ViewModels;

/// <summary>
/// The settings window's view model. Every property reads through to
/// <see cref="SettingsService.Current"/> and writes through <see cref="SettingsService.Update"/>,
/// so there is no working copy to get out of step and no Apply button to forget.
/// </summary>
public sealed class SettingsViewModel : ObservableObject, IDisposable
{
    // No 60 here. It is below AppSettings.MinimumRefreshSeconds, so choosing it would silently
    // resolve to 120 and leave the settings window showing a cadence the application is not using.
    private static readonly int[] RefreshPresets = [120, 300, 600];
    private static readonly int[] StalePresets = [60, 120, 300, 600, 1800, 3600];

    // Evening and morning, an hour apart. Deliberately not the full 24: a schedule offering 03:00
    // as a start is offering a shape nobody wants, and a hand-edited file can hold anything anyway.
    private static readonly int[] QuietStartPresets = [1080, 1140, 1200, 1260, 1320, 1380];
    private static readonly int[] QuietEndPresets = [300, 360, 420, 480, 540, 600];

    private bool _isConfirmingReset;
    private string? _resetResultText;

    private readonly SettingsService _settings;
    private readonly StartupRegistration _startup;
    private readonly IReadOnlyList<ProviderDescriptor> _providers;
    private readonly bool _globalHotkeyUnavailable;
    private readonly UpdateCheckService _updates;
    private readonly Func<DateTimeOffset> _clock;
    private bool _isCheckingForUpdates;

    public SettingsViewModel(
        SettingsService settings,
        StartupRegistration startup,
        Action resetPosition,
        Action recheckProviders,
        IReadOnlyList<ProviderDescriptor> providers,
        bool globalHotkeyUnavailable = false,
        UpdateCheckService? updates = null,
        Func<DateTimeOffset>? clock = null)
    {
        // A disabled fallback rather than a null check on every read: a test or a harness that does
        // not care about updates still gets a page that renders and a button that refuses.
        _updates = updates ?? new UpdateCheckService("unknown") { Enabled = false };
        _clock = clock ?? (() => DateTimeOffset.Now);
        _updates.StatusChanged += OnUpdateStatusChanged;
        _settings = settings;
        _startup = startup;
        _providers = providers;
        _globalHotkeyUnavailable = globalHotkeyUnavailable;

        // No CanExecute. The button is always pressable: while a check is running it shows a
        // spinner instead, and a press after one has finished is a legitimate re-check.
        CheckForUpdatesCommand = new RelayCommand(() => _ = CheckForUpdatesAsync());

        OpenReleasePageCommand = new RelayCommand(OpenReleasePage);

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

        MiniDocks =
        [
            Dock("Top", MiniDock.Top),
            Dock("Bottom", MiniDock.Bottom)
        ];

        RefreshIntervals = Durations(
            "refresh",
            RefreshPresets,
            (int)settings.Current.RefreshInterval.TotalSeconds,
            seconds => _settings.Update(s => s with { RefreshIntervalSeconds = seconds }),
            () => (int)_settings.Current.RefreshInterval.TotalSeconds);

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

        // No OpenLogsCommand or OpenDiagnosticsCommand. Diagnostics is a page of the settings shell
        // now rather than a window this view model can open, and the logs folder is offered on that
        // shell's application diagnostics page, against DiagnosticsViewModel.OpenLogsCommand.
        ResetPositionCommand = new RelayCommand(resetPosition);
        RecheckProvidersCommand = new RelayCommand(recheckProviders);

        // Two presses, and no modal. There is no dialog anywhere in this application, and the
        // widget hides itself when focus leaves the process - a message box is a window of its own
        // and would be arguing with that. The confirmation is the button turning into a question.
        ResetSettingsCommand = new RelayCommand(() =>
        {
            ResetResultText = null;
            IsConfirmingReset = true;
        });
        CancelResetCommand = new RelayCommand(() => IsConfirmingReset = false);
        ConfirmResetCommand = new RelayCommand(() => ResetSettings(resetPosition));

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

    public bool MiniMode
    {
        get => _settings.Current.MiniMode;
        set => _settings.Update(s => s with { MiniMode = value });
    }

    public string MiniModeHintText =>
        "A one-line strip pinned to a screen edge, above other windows. Click it to bring the full widget back.";

    public string QuietHoursSummaryText =>
        $"Milestone alerts are held back between {TimeLabel(_settings.Current.QuietHoursStartMinutes)} "
        + $"and {TimeLabel(_settings.Current.QuietHoursEndMinutes)}. Reaching 100% still notifies.";

    public bool GlobalHotkeyEnabled
    {
        get => _settings.Current.GlobalHotkeyEnabled;
        set => _settings.Update(s => s with { GlobalHotkeyEnabled = value });
    }

    /// <summary>
    /// Spec D3. Off stops every unattended request; <see cref="CheckForUpdatesCommand"/> keeps
    /// working, because that one the user asked for.
    /// </summary>
    public bool UpdateCheckEnabled
    {
        get => _settings.Current.UpdateCheckEnabled;
        set
        {
            _settings.Update(s => s with { UpdateCheckEnabled = value });
            _updates.Enabled = value;
        }
    }

    public string UpdateDisclosureText =>
        "Asks github.com once a day whether a newer release exists. The request is anonymous: it "
        + "sends nothing about you, this machine, or your providers, and no usage data ever leaves "
        + "this computer.";

    public string UpdateStatusText => UpdateCopy.StatusText(_updates.Status);

    public string UpdateLastCheckedText =>
        UpdateCopy.LastCheckedText(_updates.Status.LastCheckedUtc, _clock());

    public bool HasUpdate => _updates.Status.Availability == UpdateAvailability.UpdateAvailable;

    /// <summary>
    /// Whether a check is running, so the page can show a spinner where the button was. The press
    /// itself is never refused: the service shares one request, so pressing again while this is
    /// true would join the check already running rather than start a second one.
    /// </summary>
    public bool IsCheckingForUpdates
    {
        get => _isCheckingForUpdates;
        private set => Set(ref _isCheckingForUpdates, value);
    }

    /// <summary>
    /// Exposed as a task so a test can await the check rather than sleep on it. The command
    /// discards it, which is the one place fire-and-forget is right - a button press has no
    /// caller to return to.
    /// </summary>
    public async Task CheckForUpdatesAsync()
    {
        IsCheckingForUpdates = true;

        try
        {
            await _updates
                .CheckAsync(manual: true, _clock(), CancellationToken.None)
                .ConfigureAwait(true);
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    public RelayCommand CheckForUpdatesCommand { get; }

    public RelayCommand OpenReleasePageCommand { get; }

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

    public IReadOnlyList<ChoiceViewModel> MiniDocks { get; }

    public ObservableCollection<ChoiceViewModel> RefreshIntervals { get; }

    public ObservableCollection<ChoiceViewModel> StaleThresholds { get; }

    public ObservableCollection<ChoiceViewModel> AlertThresholdChoices { get; }

    public ObservableCollection<ChoiceViewModel> QuietHoursStarts { get; }

    public ObservableCollection<ChoiceViewModel> QuietHoursEnds { get; }

    public ObservableCollection<ProviderPreferenceViewModel> ProviderPreferences { get; } = [];

    public string ProviderPreferencesHintText => "Hidden providers are not polled, and do not appear in the notification-area icon.";

    /// <summary>
    /// Whether the reset button has been pressed once and is waiting to be confirmed. Not persisted
    /// and not carried across a settings window being closed: an unanswered question is not a state
    /// worth remembering.
    /// </summary>
    public bool IsConfirmingReset
    {
        get => _isConfirmingReset;
        private set => Set(ref _isConfirmingReset, value);
    }

    /// <summary>What the reset did, including where the previous settings went. Null until one runs.</summary>
    public string? ResetResultText
    {
        get => _resetResultText;
        private set => Set(ref _resetResultText, value);
    }

    public RelayCommand ResetSettingsCommand { get; }

    public RelayCommand ConfirmResetCommand { get; }

    public RelayCommand CancelResetCommand { get; }

    public RelayCommand ResetPositionCommand { get; }

    public RelayCommand RecheckProvidersCommand { get; }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _settings.PersistenceStateChanged -= OnPersistenceStateChanged;
        _updates.StatusChanged -= OnUpdateStatusChanged;
    }

    private void OnUpdateStatusChanged(object? sender, UpdateStatus status)
    {
        Raise(nameof(UpdateStatusText));
        Raise(nameof(UpdateLastCheckedText));
        Raise(nameof(HasUpdate));
    }

    /// <summary>
    /// A compile-time constant destination, never a URL from the response - spec D6, and the same
    /// rule the footer's GitHub link already follows. A machine with no registered browser is not
    /// a reason to take the settings window down.
    /// </summary>
    private static void OpenReleasePage()
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(GitHubReleaseClient.ReleasePageUrl)
                {
                    UseShellExecute = true
                });
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private ChoiceViewModel Theme(string label, ThemePreference preference) => new(
        label,
        (int)preference,
        "theme",
        () => (int)_settings.Current.Theme,
        value => _settings.Update(s => s with { Theme = (ThemePreference)value }));

    private ChoiceViewModel Dock(string label, MiniDock dock) => new(
        label,
        (int)dock,
        "mini-dock",
        () => (int)_settings.Current.MiniDock,
        value => _settings.Update(s => s with { MiniDock = (MiniDock)value }));

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
    /// <summary>
    /// Puts every application setting back to its default (PRD §19). Provider configuration is not
    /// stored by this application and is not touched by any of this.
    /// <para>
    /// Three things happen that the settings file alone cannot do. The startup entry is cleared,
    /// because it lives in the registry rather than in the file and would otherwise be read back on
    /// the next load and silently undo its own reset. The widget is re-centred, because its position
    /// has just been forgotten and a widget that stayed put would be the one visible thing the reset
    /// appeared to miss. And the result names the backup, because a destructive action that cannot
    /// be undone is one users are right to distrust.
    /// </para>
    /// </summary>
    private void ResetSettings(Action resetPosition)
    {
        string? backup = _settings.Reset();
        _startup.Disable();
        resetPosition();

        IsConfirmingReset = false;
        ResetResultText = backup is null
            ? "Settings are back to their defaults. There were no previous settings to save."
            : "Settings are back to their defaults. The previous ones were kept beside the settings "
              + $"file as {Path.GetFileName(backup)}.";
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        Raise(null);

        RefreshAlertThresholdChoices();

        foreach (ChoiceViewModel choice in Themes
            .Concat(Densities)
            .Concat(MiniDocks)
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
