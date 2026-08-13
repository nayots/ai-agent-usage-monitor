namespace AiUsageMonitor.App.ViewModels;

/// <summary>Which body a sidebar entry shows. The shell's template selector switches on this.</summary>
public enum SettingsPageKind
{
    Appearance,
    Window,
    Providers,
    Notifications,
    Refresh,
    ProviderDiagnostics,
    ApplicationDiagnostics
}

/// <summary>One entry in the settings shell's sidebar, and the body it shows.</summary>
public sealed class SettingsPageViewModel(
    SettingsPageKind kind,
    string title,
    string groupTitle,
    Func<object?> content) : ObservableObject
{
    public SettingsPageKind Kind { get; } = kind;

    public string Title { get; } = title;

    public string GroupTitle { get; } = groupTitle;

    public bool IsDiagnostics =>
        Kind is SettingsPageKind.ProviderDiagnostics or SettingsPageKind.ApplicationDiagnostics;

    /// <summary>
    /// Resolved on every read, never captured. <c>DiagnosticsViewModel.Rebuild</c> replaces every
    /// section object rather than mutating it, and <c>Copy</c> calls it - so a page holding the
    /// instance it was built with would go on rendering an orphan: no exception and no blank pane,
    /// just values that have silently stopped tracking.
    /// </summary>
    public object? Content => content();

    /// <summary>Announces that <see cref="Content"/> now resolves to something else.</summary>
    public void ContentChanged() => Raise(nameof(Content));
}
