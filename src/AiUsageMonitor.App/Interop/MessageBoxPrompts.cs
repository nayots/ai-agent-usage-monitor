using System.Windows;

namespace AiUsageMonitor.App.Interop;

/// <summary>
/// The real prompts. A plain <see cref="MessageBox"/> rather than a styled window on purpose: this
/// runs before the theme is applied and before any window exists, and a takeover offer that could
/// not be drawn would be worse than a plain one that can.
/// </summary>
internal sealed class MessageBoxPrompts : IInstancePrompts
{
    private const string Title = "AI Usage Monitor";

    public bool ConfirmReplace(RunningInstance running) =>
        MessageBox.Show(
            $"AI Usage Monitor v{running.Version} is already running from another location:\n\n"
            + $"{running.ExecutablePath}\n\n"
            + "Replace it with this copy?",
            Title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ReportReplaceFailed() =>
        MessageBox.Show(
            "The running copy could not be replaced.\n\nClose it from its tray icon and try again.",
            Title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
}
