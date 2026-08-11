using System.Windows;
using AiUsageMonitor.Infrastructure.Settings;
using AiUsageMonitor.Infrastructure.Theming;
using Microsoft.Win32;

namespace AiUsageMonitor.App.Theming;

/// <summary>
/// Owns the one theme dictionary in <see cref="Application.Resources"/> that changes, and
/// replaces it in place so the invariant dictionaries around it are never disturbed.
/// </summary>
public sealed class ThemeManager : IDisposable
{
    private readonly Application _application;
    private ResourceDictionary? _active;
    private ThemePreference _preference = ThemePreference.System;

    public ThemeManager(Application application)
    {
        _application = application;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public ThemeVariant Current { get; private set; } = ThemeVariant.Light;

    /// <summary>Raised after <see cref="Current"/> changes, on the UI thread.</summary>
    public event EventHandler? Changed;

    public void Apply(ThemePreference preference)
    {
        _preference = preference;
        ThemeVariant variant = ThemeResolver.Resolve(preference, SystemTheme.UsesLightTheme, SystemTheme.IsHighContrast);

        ResourceDictionary replacement = new()
        {
            Source = new Uri($"pack://application:,,,/Themes/{variant}.xaml", UriKind.Absolute)
        };

        if (_active is not null)
        {
            _application.Resources.MergedDictionaries.Remove(_active);
        }

        _application.Resources.MergedDictionaries.Add(replacement);
        _active = replacement;
        Current = variant;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.Accessibility))
        {
            return;
        }

        _application.Dispatcher.BeginInvoke(() => Apply(_preference));
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
}
