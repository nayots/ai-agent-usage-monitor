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
