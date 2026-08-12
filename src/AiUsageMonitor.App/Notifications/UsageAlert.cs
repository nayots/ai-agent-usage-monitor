namespace AiUsageMonitor.App.Notifications;

/// <summary>What happened. Drives the copy and, for one kind only, whether the shell makes a sound.</summary>
public enum UsageAlertKind
{
    /// <summary>A window climbed past a rung of <see cref="Domain.QuotaMilestones.Ladder"/>.</summary>
    Milestone,

    /// <summary>A window reached 100%. The one alert worth interrupting for.</summary>
    LimitReached,

    /// <summary>A window that had been at 80% or above is back below it.</summary>
    Recovered,

    /// <summary>A provider that was answering has stopped.</summary>
    ProviderFailed,

    /// <summary>A provider that had stopped is answering again.</summary>
    ProviderRecovered
}

/// <summary>
/// One thing worth saying, already written. The watcher composes these; the window hands them to
/// the shell verbatim, so this is user-facing copy and is held to that standard.
/// <para>
/// Nothing here may carry a provider's error string, response body, header or path. A failing
/// provider produces an alert that says a provider is failing and nothing else - the card in the
/// widget is where a reason belongs, behind a deliberate look, not on a toast that appears
/// unbidden over whatever the user was doing.
/// </para>
/// </summary>
public sealed record UsageAlert(UsageAlertKind Kind, string Title, string Text)
{
    /// <summary>
    /// Every alert is silent except the one saying a limit is spent. PRD §4.6 asks for calm
    /// desktop behaviour, and a ding every ten percent is the opposite of it; but the moment work
    /// actually stops is worth hearing, because that is the one the user needs before they discover
    /// it the hard way.
    /// </summary>
    public bool IsSilent => Kind != UsageAlertKind.LimitReached;
}
