using System.Globalization;
using System.IO;
using AiUsageMonitor.App.Interop;
using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;
using AiUsageMonitor.Infrastructure.Settings;

namespace AiUsageMonitor.App.Tests;

public class SettingsViewModelTests
{
    private const string ScratchKey = @"Software\AiUsageMonitor\tests\SettingsVm";

    private sealed class SilentProbe(string name) : IProviderProbe
    {
        public string Name => name;
        public string Mechanism => "fake";
        public MechanismTier Tier => MechanismTier.Official;
        public Task<ProviderSnapshot> ProbeAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private static IReadOnlyList<ProviderDescriptor> Providers =>
    [
        new ProviderDescriptor("claude-code", "Claude Code", "CC", new SilentProbe("Claude Code")),
        new ProviderDescriptor("codex", "Codex", "CX", new SilentProbe("Codex"))
    ];

    private static SettingsViewModel Model(
        out SettingsService service,
        AppSettings? initial = null,
        bool globalHotkeyUnavailable = false)
    {
        string path = Path.Combine(Path.GetTempPath(), "aium-vm-" + Guid.NewGuid().ToString("N"), "settings.json");
        service = new SettingsService(new AppSettingsStore(path), initial ?? AppSettings.Default);
        return new SettingsViewModel(
            service,
            new StartupRegistration(ScratchKey, "AiUsageMonitorTest", null),
            resetPosition: () => { },
            recheckProviders: () => { },
            providers: Providers,
            globalHotkeyUnavailable: globalHotkeyUnavailable);
    }

    [Fact]
    public void ATogglePersistsThroughTheService()
    {
        SettingsViewModel model = Model(out SettingsService service);

        model.ColorBarsByUsage = false;

        Assert.False(service.Current.ColorBarsByUsage);
    }

    /// <summary>
    /// Pinning is offered by the title bar and nowhere else, because it lasts only as long as the
    /// session and a settings window promises the opposite. The window itself is checked in
    /// <c>ViewLoadingTests.TheSettingsWindowOffersNoPinning</c>; this is the half that would let a
    /// checkbox be added back without anyone noticing it had somewhere to bind to.
    /// </summary>
    [Fact]
    public void TheSettingsViewModelHasNoPinningToBindTo() =>
        Assert.Null(typeof(SettingsViewModel).GetProperty("AlwaysOnTop"));

    [Fact]
    public void QuotaNotificationsAreOnByDefault()
    {
        SettingsViewModel model = Model(out _);

        Assert.True(model.NotifyOnQuotaEvents);
    }

    [Fact]
    public void TheQuotaNotificationsTogglePersistsThroughTheService()
    {
        SettingsViewModel model = Model(out SettingsService service);

        model.NotifyOnQuotaEvents = false;

        Assert.False(service.Current.NotifyOnQuotaEvents);
    }

    [Fact]
    public void TheGlobalHotkeyTogglePersistsThroughTheService()
    {
        SettingsViewModel model = Model(out SettingsService service);

        model.GlobalHotkeyEnabled = false;

        Assert.False(service.Current.GlobalHotkeyEnabled);
    }

    [Fact]
    public void ARegisteredGlobalHotkeyHasNoWarning()
    {
        SettingsViewModel model = Model(out _, globalHotkeyUnavailable: false);

        Assert.Equal("Ctrl+Alt+Q", model.GlobalHotkeyLabel);
        Assert.False(model.HasGlobalHotkeyWarning);
        Assert.Null(model.GlobalHotkeyUnavailableReason);
    }

    [Fact]
    public void AnUnavailableGlobalHotkeyExplainsTheConflict()
    {
        SettingsViewModel model = Model(out _, globalHotkeyUnavailable: true);

        Assert.True(model.HasGlobalHotkeyWarning);
        Assert.Equal("Unavailable: another application already uses this shortcut.", model.GlobalHotkeyUnavailableReason);
    }

    [Fact]
    public void AQuotaNotificationsChangeMadeElsewhereIsReflectedBack()
    {
        SettingsViewModel model = Model(out SettingsService service);

        service.Update(s => s with { NotifyOnQuotaEvents = false });

        Assert.False(model.NotifyOnQuotaEvents);
    }

    [Fact]
    public void AChangeMadeElsewhereIsReflectedBack()
    {
        SettingsViewModel model = Model(out SettingsService service);

        service.Update(s => s with { ColorBarsByUsage = false });

        Assert.False(model.ColorBarsByUsage);
    }

    [Fact]
    public void SelectingARefreshIntervalWritesItsValue()
    {
        SettingsViewModel model = Model(out SettingsService service);

        ChoiceViewModel choice = model.RefreshIntervals.Single(c => c.Value == 300);
        choice.IsSelected = true;

        Assert.Equal(300, service.Current.RefreshIntervalSeconds);
        Assert.True(model.RefreshIntervals.Single(c => c.Value == 300).IsSelected);
    }

    [Theory]
    [InlineData(60, 300, true)]
    [InlineData(300, 60, false)]
    [InlineData(60, 60, true)]
    public void TheStaleThresholdWarningReflectsTheClampedSettings(int staleAfterSeconds, int refreshIntervalSeconds, bool expected)
    {
        SettingsViewModel model = Model(
            out _,
            AppSettings.Default with
            {
                StaleAfterSeconds = staleAfterSeconds,
                RefreshIntervalSeconds = refreshIntervalSeconds
            });

        Assert.Equal(expected, model.HasStaleThresholdWarning);
        Assert.Equal("Cards will always look stale — this is shorter than the refresh interval.", model.StaleThresholdWarningText);
    }

    [Fact]
    public void DeselectingAChoiceChangesNothing()
    {
        // Radio buttons report both sides of a move. Acting on the deselection would write the
        // outgoing value back over the incoming one, and which one won would depend on event order.
        SettingsViewModel model = Model(out SettingsService service);
        int before = service.Current.RefreshIntervalSeconds;

        model.RefreshIntervals.Single(c => c.Value == before).IsSelected = false;

        Assert.Equal(before, service.Current.RefreshIntervalSeconds);
    }

    [Fact]
    public void AHandEditedValueOutsideThePresetsSurvivesAsItsOwnChoice()
    {
        SettingsViewModel model = Model(out _, AppSettings.Default with { RefreshIntervalSeconds = 45 });

        ChoiceViewModel selected = model.RefreshIntervals.Single(c => c.IsSelected);

        Assert.Equal(45, selected.Value);
    }

    [Fact]
    public void EveryThemeIsOfferedAndTheCurrentOneIsSelected()
    {
        SettingsViewModel model = Model(out SettingsService service);

        Assert.Equal(3, model.Themes.Count);
        model.Themes.Single(c => c.Value == (int)ThemePreference.Dark).IsSelected = true;

        Assert.Equal(ThemePreference.Dark, service.Current.Theme);
    }

    [Fact]
    public void WithoutAKnownExecutableStartWithWindowsIsOfferedButDisabled()
    {
        SettingsViewModel model = Model(out _);

        Assert.False(model.CanStartWithWindows);
        Assert.False(model.StartWithWindows);
        Assert.False(string.IsNullOrWhiteSpace(model.StartWithWindowsUnavailableReason));
    }

    [Fact]
    public void TheActionsCallWhatTheyClaimTo()
    {
        string path = Path.Combine(Path.GetTempPath(), "aium-vm-" + Guid.NewGuid().ToString("N"), "settings.json");
        SettingsService service = new(new AppSettingsStore(path), AppSettings.Default);
        int reset = 0, recheck = 0;
        SettingsViewModel model = new(
            service,
            new StartupRegistration(ScratchKey, "AiUsageMonitorTest", null),
            resetPosition: () => reset++,
            recheckProviders: () => recheck++,
            providers: Providers);

        model.ResetPositionCommand.Execute(null);
        model.RecheckProvidersCommand.Execute(null);

        Assert.Equal(1, reset);
        Assert.Equal(1, recheck);
    }

    [Fact]
    public void TheDensityChoicesOfferStandardAndCompactAndWriteThrough()
    {
        SettingsViewModel model = Model(out SettingsService service);
        Assert.Equal(new[] { "Standard", "Compact" }, model.Densities.Select(choice => choice.Label));
        Assert.True(model.Densities[0].IsSelected);
        model.Densities[1].IsSelected = true;
        Assert.Equal(WidgetDensity.Compact, service.Current.Density);
    }

    [Fact]
    public void ADensityChangeMadeElsewhereMovesTheRadio()
    {
        SettingsViewModel model = Model(out SettingsService service);
        service.Update(s => s with { Density = WidgetDensity.Compact });
        Assert.False(model.Densities[0].IsSelected);
        Assert.True(model.Densities[1].IsSelected);
    }

    [Fact]
    public void PersistenceWarningFollowsTheSettingsServiceWithoutExposingFailureDetails()
    {
        string path = Path.Combine(Path.GetTempPath(), "aium-vm-" + Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(path);
        SettingsService service = new(new AppSettingsStore(path), AppSettings.Default);
        SettingsViewModel model = new(service, new StartupRegistration(ScratchKey, "AiUsageMonitorTest", null), () => { }, () => { }, Providers);

        model.ColorBarsByUsage = false;

        Assert.True(model.HasPersistenceWarning);
        Assert.Equal("Changes apply to this session only — the settings file could not be saved.", model.PersistenceWarningText);
        Assert.DoesNotContain(path, model.PersistenceWarningText);
        Assert.DoesNotContain("IOException", model.PersistenceWarningText);
        model.Dispose();
    }

    [Fact]
    public void MiniModeWritesThroughToTheService()
    {
        SettingsViewModel model = Model(out SettingsService service);

        Assert.False(model.MiniMode);
        model.MiniMode = true;

        Assert.True(service.Current.MiniMode);
        model.Dispose();
    }

    [Fact]
    public void SelectingTheBottomEdgeWritesIt()
    {
        SettingsViewModel model = Model(out SettingsService service);

        model.MiniDocks.Single(choice => choice.Label == "Bottom").IsSelected = true;

        Assert.Equal(MiniDock.Bottom, service.Current.MiniDock);
        model.Dispose();
    }

    /// <summary>
    /// The mode can be entered from the tray menu or from a click on the strip, so this window has
    /// to follow a change it did not make.
    /// </summary>
    [Fact]
    public void TheDockChoicesFollowAnExternallyChangedSetting()
    {
        SettingsViewModel model = Model(out SettingsService service);

        service.Update(s => s with { MiniDock = MiniDock.Bottom });

        Assert.Equal("Bottom", model.MiniDocks.Single(choice => choice.IsSelected).Label);
        model.Dispose();
    }

    [Fact]
    public void TheFullLadderIsSelectedByDefault()
    {
        SettingsViewModel model = Model(out _);

        ChoiceViewModel selected = model.AlertThresholdChoices.Single(choice => choice.IsSelected);

        Assert.Equal("Every milestone", selected.Label);
        model.Dispose();
    }

    [Fact]
    public void SelectingAPresetWritesItsThresholds()
    {
        SettingsViewModel model = Model(out SettingsService service);

        model.AlertThresholdChoices.Single(choice => choice.Label == "100% only").IsSelected = true;

        Assert.Equal([100], service.Current.AlertThresholds);
        model.Dispose();
    }

    /// <summary>
    /// A ladder typed into the settings file is a deliberate choice. It is shown by name and stays
    /// selected, rather than being silently snapped to whichever preset looks closest.
    /// </summary>
    [Fact]
    public void AHandEditedLadderIsShownAsItsOwnChoiceAndStaysSelected()
    {
        SettingsViewModel model = Model(out _, AppSettings.Default with { AlertThresholds = [75, 90, 100] });

        ChoiceViewModel selected = model.AlertThresholdChoices.Single(choice => choice.IsSelected);

        Assert.Equal("Custom (75, 90, 100%)", selected.Label);
        model.Dispose();
    }

    /// <summary>
    /// And it goes away once a real preset is chosen - it exists to represent a value, not to be a
    /// permanent fifth option.
    /// </summary>
    [Fact]
    public void TheCustomChoiceDisappearsOnceAPresetIsChosen()
    {
        SettingsViewModel model = Model(out _, AppSettings.Default with { AlertThresholds = [75, 90, 100] });

        model.AlertThresholdChoices.Single(choice => choice.Label == "90 and 100%").IsSelected = true;

        Assert.DoesNotContain(model.AlertThresholdChoices, choice => choice.Label.StartsWith("Custom"));
        Assert.Equal("90 and 100%", model.AlertThresholdChoices.Single(choice => choice.IsSelected).Label);
        model.Dispose();
    }

    [Fact]
    public void TheThresholdHintNamesTheOneAlertThatAlwaysSpeaks()
    {
        SettingsViewModel model = Model(out _);

        Assert.Equal("100% always notifies, and is the only alert that makes a sound.", model.AlertThresholdHintText);
        model.Dispose();
    }

    [Fact]
    public void QuietHoursAreOffUntilAskedFor()
    {
        SettingsViewModel model = Model(out SettingsService service);

        Assert.False(model.QuietHoursEnabled);

        model.QuietHoursEnabled = true;

        Assert.True(service.Current.QuietHoursEnabled);
        model.Dispose();
    }

    [Fact]
    public void SelectingAStartWritesItsMinutes()
    {
        SettingsViewModel model = Model(out SettingsService service);

        model.QuietHoursStarts.Single(choice => choice.Value == 1380).IsSelected = true;

        Assert.Equal(1380, service.Current.QuietHoursStartMinutes);
        model.Dispose();
    }

    [Fact]
    public void SelectingAnEndWritesItsMinutes()
    {
        SettingsViewModel model = Model(out SettingsService service);

        model.QuietHoursEnds.Single(choice => choice.Value == 540).IsSelected = true;

        Assert.Equal(540, service.Current.QuietHoursEndMinutes);
        model.Dispose();
    }

    /// <summary>
    /// The same rule the duration lists follow: a value from a hand-edited file appears in its
    /// sorted place rather than vanishing because this window offers a shorter list.
    /// </summary>
    [Fact]
    public void AHandEditedStartTimeAppearsInItsSortedPlace()
    {
        SettingsViewModel model = Model(out _, AppSettings.Default with { QuietHoursStartMinutes = 1290 });

        Assert.Equal(7, model.QuietHoursStarts.Count);
        Assert.Equal(1290, model.QuietHoursStarts[4].Value);
        Assert.True(model.QuietHoursStarts[4].IsSelected);
        model.Dispose();
    }

    /// <summary>
    /// The two groups must not share a name. WPF scopes radio buttons by GroupName rather than by
    /// container, so one name would make choosing a start deselect the end.
    /// </summary>
    [Fact]
    public void EveryRadioGroupInThisWindowHasItsOwnName()
    {
        SettingsViewModel model = Model(out _);

        string[] groups =
        [
            model.RefreshIntervals[0].GroupName,
            model.StaleThresholds[0].GroupName,
            model.AlertThresholdChoices[0].GroupName,
            model.QuietHoursStarts[0].GroupName,
            model.QuietHoursEnds[0].GroupName
        ];

        Assert.Equal(groups.Length, groups.Distinct().Count());
        model.Dispose();
    }

    [Fact]
    public void TheQuietHoursSummaryNamesBothEndsAndTheExceptionToTheRule()
    {
        SettingsViewModel model = Model(out _, AppSettings.Default with
        {
            QuietHoursEnabled = true,
            QuietHoursStartMinutes = 1320,
            QuietHoursEndMinutes = 420
        });

        string summary = model.QuietHoursSummaryText;

        Assert.Contains(new TimeOnly(22, 0).ToString("t", CultureInfo.CurrentCulture), summary);
        Assert.Contains(new TimeOnly(7, 0).ToString("t", CultureInfo.CurrentCulture), summary);
        Assert.Contains("Reaching 100% still notifies.", summary);
        model.Dispose();
    }
}
