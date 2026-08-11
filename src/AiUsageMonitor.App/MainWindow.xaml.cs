using System.Windows;
using System.Windows.Controls;
using AiUsageMonitor.App.Theming;
using AiUsageMonitor.Infrastructure.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace AiUsageMonitor.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Seed the controls from the settings startup already loaded. Setting IsChecked in XAML
        // instead would fire Checked during InitializeComponent and re-apply a hardcoded
        // preference over the user's - a view silently overwriting loaded state at construction.
        AppSettings settings = ((App)Application.Current).Services.GetRequiredService<AppSettings>();
        ColorBarsCheckBox.IsChecked = settings.ColorBarsByUsage;

        RadioButton selected = settings.Theme switch
        {
            ThemePreference.Light => LightThemeRadioButton,
            ThemePreference.Dark => DarkThemeRadioButton,
            _ => SystemThemeRadioButton
        };
        selected.IsChecked = true;
    }

    private void ThemeRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { IsChecked: true } button)
        {
            return;
        }

        ThemePreference preference = (button.Tag as string) switch
        {
            "Light" => ThemePreference.Light,
            "Dark" => ThemePreference.Dark,
            _ => ThemePreference.System
        };

        ((App)Application.Current).Services.GetRequiredService<ThemeManager>().Apply(preference);
    }
}
