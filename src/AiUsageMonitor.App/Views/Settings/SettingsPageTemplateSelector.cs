using System.Windows;
using System.Windows.Controls;
using AiUsageMonitor.App.ViewModels;

namespace AiUsageMonitor.App.Views.Settings;

/// <summary>
/// Picks the body for the page on screen. A DataType-keyed template cannot do it: all five settings
/// pages carry the same <see cref="SettingsViewModel"/> instance as their content, so the type says
/// nothing about which page it is.
/// </summary>
public sealed class SettingsPageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Appearance { get; set; }
    public DataTemplate? Window { get; set; }
    public DataTemplate? Providers { get; set; }
    public DataTemplate? Notifications { get; set; }
    public DataTemplate? Refresh { get; set; }
    public DataTemplate? Updates { get; set; }
    public DataTemplate? Diagnostics { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => item switch
    {
        SettingsPageViewModel { Kind: SettingsPageKind.Appearance } => Appearance,
        SettingsPageViewModel { Kind: SettingsPageKind.Window } => Window,
        SettingsPageViewModel { Kind: SettingsPageKind.Providers } => Providers,
        SettingsPageViewModel { Kind: SettingsPageKind.Notifications } => Notifications,
        SettingsPageViewModel { Kind: SettingsPageKind.Refresh } => Refresh,
        SettingsPageViewModel { Kind: SettingsPageKind.Updates } => Updates,
        SettingsPageViewModel { IsDiagnostics: true } => Diagnostics,
        _ => null
    };
}
