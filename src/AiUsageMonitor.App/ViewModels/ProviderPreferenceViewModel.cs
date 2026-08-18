using System.Collections.ObjectModel;
using AiUsageMonitor.Infrastructure.Providers;
using AiUsageMonitor.Infrastructure.Settings;

namespace AiUsageMonitor.App.ViewModels;

public sealed class ProviderPreferenceViewModel : ObservableObject
{
    // 0 is "follow the global interval". No 60: it is below AppSettings.MinimumRefreshSeconds and
    // would resolve to 120 while still displaying as the user's choice.
    private static readonly int[] IntervalPresets = [0, 120, 300, 600];

    private readonly SettingsService _settings;
    private readonly Action<ProviderPreferenceViewModel, int> _move;

    public ProviderPreferenceViewModel(
        ProviderDescriptor provider,
        SettingsService settings,
        Action<ProviderPreferenceViewModel, int> move)
    {
        Key = provider.Key;
        DisplayName = provider.DisplayName;
        _settings = settings;
        _move = move;

        int current = settings.Current.RefreshSecondsOverrideFor(Key) is int seconds
            ? Math.Clamp(seconds, AppSettings.MinimumRefreshSeconds, 3600)
            : 0;
        Intervals = Durations(
            $"interval-{Key}",
            current,
            seconds => SetInterval(seconds),
            () => _settings.Current.RefreshSecondsOverrideFor(Key) is int value
                ? Math.Clamp(value, AppSettings.MinimumRefreshSeconds, 3600)
                : 0);

        MoveUpCommand = new RelayCommand(() => _move(this, -1), () => CanMoveUp);
        MoveDownCommand = new RelayCommand(() => _move(this, 1), () => CanMoveDown);
    }

    public string Key { get; }

    public string DisplayName { get; }

    public bool IsVisible
    {
        get => !_settings.Current.IsProviderHidden(Key);
        set => _settings.Update(settings => settings with
        {
            HiddenProviders = value
                ? settings.HiddenProviders.Where(key => !StringComparer.OrdinalIgnoreCase.Equals(key, Key)).ToArray()
                : settings.IsProviderHidden(Key) ? settings.HiddenProviders : [.. settings.HiddenProviders, Key]
        });
    }

    public ObservableCollection<ChoiceViewModel> Intervals { get; }

    public RelayCommand MoveUpCommand { get; }

    public RelayCommand MoveDownCommand { get; }

    public bool CanMoveUp { get; private set; }

    public bool CanMoveDown { get; private set; }

    public void Refresh()
    {
        Raise(nameof(IsVisible));
        Raise(nameof(CanMoveUp));
        Raise(nameof(CanMoveDown));
        MoveUpCommand.RaiseCanExecuteChanged();
        MoveDownCommand.RaiseCanExecuteChanged();

        foreach (ChoiceViewModel interval in Intervals)
        {
            interval.Refresh();
        }
    }

    internal void SetMoveAvailability(bool canMoveUp, bool canMoveDown)
    {
        CanMoveUp = canMoveUp;
        CanMoveDown = canMoveDown;
    }

    private void SetInterval(int seconds) => _settings.Update(settings =>
    {
        Dictionary<string, int> overrides = new(settings.ProviderRefreshSeconds, StringComparer.OrdinalIgnoreCase);

        if (seconds == 0)
        {
            overrides.Remove(Key);
        }
        else
        {
            overrides[Key] = seconds;
        }

        return settings with { ProviderRefreshSeconds = overrides };
    });

    private static ObservableCollection<ChoiceViewModel> Durations(
        string groupName,
        int current,
        Action<int> write,
        Func<int> read)
    {
        List<int> values = [.. IntervalPresets];

        if (!values.Contains(current))
        {
            values.Add(current);
            values.Sort();
        }

        return [.. values.Select(seconds => new ChoiceViewModel(DurationLabel(seconds), seconds, groupName, read, write))];
    }

    private static string DurationLabel(int seconds) => seconds == 0
        ? "Shared"
        : seconds < 60
            ? seconds + "s"
            : seconds % 60 == 0 ? seconds / 60 + "m" : seconds / 60 + "m " + seconds % 60 + "s";
}
