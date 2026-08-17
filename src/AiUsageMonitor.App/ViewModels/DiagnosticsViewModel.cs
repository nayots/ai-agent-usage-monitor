using System.Globalization;
using System.Text;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Diagnostics;
using AiUsageMonitor.Infrastructure.Providers;
using AiUsageMonitor.Infrastructure.Refresh;

namespace AiUsageMonitor.App.ViewModels;

/// <summary>Projects provider and application facts into the diagnostics screen and clipboard bundle.</summary>
public sealed class DiagnosticsViewModel : ObservableObject
{
    /// <summary>
    /// The title of the section that is about this application rather than about a provider. Named
    /// rather than repeated as a literal because the settings shell keys a page kind off it: the
    /// application page is the only one that offers the logs folder.
    /// </summary>
    public const string ApplicationSectionTitle = "Application";

    public const string EmptyValue = "—";

    private readonly IReadOnlyList<ProviderCardViewModel> _cards;
    private readonly IReadOnlyList<ProviderDescriptor> _providers;
    private readonly ProviderRefreshService _refresh;
    private readonly EnvironmentReport _environment;
    private readonly StartupReport _startup;
    private readonly string _themeDescription;
    private readonly string _displayScalingDescription;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<string> _copyToClipboard;
    private IReadOnlyList<DiagnosticSection> _sections = [];
    private string? _copyConfirmationText;

    public DiagnosticsViewModel(
        IReadOnlyList<ProviderCardViewModel> cards,
        IReadOnlyList<ProviderDescriptor> providers,
        ProviderRefreshService refresh,
        EnvironmentReport environment,
        StartupReport startup,
        string themeDescription,
        string displayScalingDescription,
        Func<DateTimeOffset> clock,
        Action<string> copyToClipboard,
        Action openLogs)
    {
        _cards = cards;
        _providers = providers;
        _refresh = refresh;
        _environment = environment;
        _startup = startup;
        _themeDescription = themeDescription;
        _displayScalingDescription = displayScalingDescription;
        _clock = clock;
        _copyToClipboard = copyToClipboard;
        CopyCommand = new RelayCommand(Copy);
        OpenLogsCommand = new RelayCommand(openLogs);
        Rebuild();
    }

    public IReadOnlyList<DiagnosticSection> Sections
    {
        get => _sections;
        private set => Set(ref _sections, value);
    }

    public RelayCommand CopyCommand { get; }

    public RelayCommand OpenLogsCommand { get; }

    public string CopyHintText => "Copying replaces your user folder and user name with placeholders. Credentials are never read into this screen.";

    public string? CopyConfirmationText
    {
        get => _copyConfirmationText;
        private set => Set(ref _copyConfirmationText, value);
    }

    /// <summary>Re-projects all currently held card and polling state without starting a provider call.</summary>
    public void Rebuild()
    {
        DateTimeOffset now = _clock();
        List<DiagnosticSection> sections = [];

        foreach (ProviderDescriptor provider in _providers)
        {
            ProviderCardViewModel? card = _cards.FirstOrDefault(candidate => candidate.DisplayName == provider.DisplayName);
            sections.Add(BuildProviderSection(provider, card, now));
        }

        sections.Add(BuildApplicationSection());
        Sections = sections;
    }

    /// <summary>Builds the complete redacted plain-text representation of the diagnostics screen.</summary>
    public string BuildBundle()
    {
        Rebuild();
        StringBuilder builder = new();
        builder.AppendLine("Quota Monitor diagnostics");
        builder.AppendLine(LocalInstant(_clock()));

        foreach (DiagnosticSection section in Sections)
        {
            builder.AppendLine();
            builder.AppendLine(section.Title);
            if (section.Subtitle is not null)
            {
                builder.AppendLine(section.Subtitle);
            }

            foreach (DiagnosticField field in section.Fields)
            {
                builder.Append(field.Label).Append(": ").AppendLine(field.Value);
            }

            foreach (string line in section.Lines)
            {
                builder.AppendLine(line);
            }
        }

        return DiagnosticRedaction.Redact(builder.ToString())!;
    }

    private DiagnosticSection BuildProviderSection(ProviderDescriptor provider, ProviderCardViewModel? card, DateTimeOffset now)
    {
        ProviderSnapshot? snapshot = card?.LatestSnapshot;
        ProviderActivity activity = _refresh.ActivityFor(provider, now);
        List<DiagnosticField> fields =
        [
            new("Installed", snapshot is null ? EmptyValue : YesNo(snapshot.Installed)),
            new("Executable", snapshot?.ExecutablePath ?? "Not detected"),
            new("Version", snapshot?.Version ?? "Not reported"),
            new("Connection state", ConnectionStateText.Label(card?.State ?? ConnectionState.Discovering)),
            new("Freshness", Freshness(snapshot, card, now)),
            new("Mechanism", snapshot?.Mechanism ?? provider.Probe.Mechanism),
            new("Mechanism tier", Tier(snapshot?.Tier ?? provider.Probe.Tier)),
            new("Update model", snapshot?.UpdateModel ?? EmptyValue),
            new("First-party network call", provider.Probe.MakesFirstPartyNetworkCall
                ? "Yes — this application calls the provider's own host over TLS"
                : "No — this application reads the local machine only"),
            new("Capabilities", Capabilities(snapshot)),
            new("Last discovery", InstantAndAge(activity.LastAttemptStartedAt, now) ?? EmptyValue),
            new("Last successful refresh", InstantAndAge(activity.LastSuccessAt, now) ?? "Never"),
            new("Next attempt", card?.IsHiddenByUser == true
                ? "Not scheduled — hidden by the user"
                : NextAttempt(activity.NextAttemptAt, now)),
            new("Last attempt trigger", activity.LastTrigger?.ToString() ?? EmptyValue),
            new("Last outcome", activity.LastOutcome ?? EmptyValue),
            new("Last attempt duration", activity.LastDuration is TimeSpan duration
                ? $"{duration.TotalMilliseconds:0} ms"
                : EmptyValue),
            new("Consecutive failures", activity.ConsecutiveFailures.ToString(CultureInfo.InvariantCulture)),
            new("Consecutive throttles", activity.ConsecutiveThrottles.ToString(CultureInfo.InvariantCulture)),
            new("Next attempt reason", NextAttemptReason(activity.NextAttemptSource)),
            new("Requests joined or suppressed", activity.SuppressedRequests.ToString(CultureInfo.InvariantCulture)),
            new("Lifecycle refreshes coalesced", activity.CoalescedLifecycleRefreshes.ToString(CultureInfo.InvariantCulture)),
            new("In flight", YesNo(activity.IsInFlight)),
            new("Last error", snapshot?.Error ?? "None"),
            new("Quota windows", snapshot is null ? "None reported" : snapshot.Windows.Count == 0 ? "None reported" : snapshot.Windows.Count.ToString(CultureInfo.InvariantCulture))
        ];

        List<string> lines = [];
        if (snapshot is not null)
        {
            lines.AddRange(snapshot.Windows.Select(WindowLine));
            lines.AddRange(snapshot.Notes.Select(note => "note: " + note));
        }

        return new DiagnosticSection(provider.DisplayName, null, fields, lines);
    }

    private DiagnosticSection BuildApplicationSection()
    {
        string logging = _environment.LogDirectoryWritable
            ? "Writing to " + _environment.LogDirectory
            : "Not writing — the log folder is not writable · " + _environment.LogDirectory;
        string startup = "Succeeded · " + LocalInstant(_startup.StartedAt);
        if (_startup.SettingsWereUnreadable)
        {
            startup += " · the settings file could not be read and was backed up";
        }

        return new DiagnosticSection(
            ApplicationSectionTitle,
            "This application never requests administrator rights.",
            [
                new("Application version", _environment.ApplicationVersion),
                new(".NET runtime", _environment.RuntimeVersion),
                new("Windows", _environment.OperatingSystem),
                new("Theme", _themeDescription),
                new("Display scaling", _displayScalingDescription),
                new("Logging", logging),
                new("Last startup", startup),
                new("Privileges", _environment.IsElevated ? "Administrator" : "Standard user")
            ],
            []);
    }

    private void Copy()
    {
        _copyToClipboard(BuildBundle());
        CopyConfirmationText = "Copied. Local paths are masked and no credentials are included.";
    }

    private static string Freshness(ProviderSnapshot? snapshot, ProviderCardViewModel? card, DateTimeOffset now)
    {
        if (snapshot?.RetrievedAt is not DateTimeOffset retrieved)
        {
            return "Never retrieved";
        }

        string value = card?.State == ConnectionState.Stale ? "Stale" : "Current";
        return value + " · " + RelativeTime.FormatAge(now - retrieved);
    }

    private static string Capabilities(ProviderSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return EmptyValue;
        }

        IReadOnlyList<QuotaWindow> windows = snapshot.Windows;
        return string.Join("; ",
            $"reports {windows.Count} quota window(s)",
            windows.Any(window => window.ResetsAt is not null) ? "reset times: reported" : "reset times: not reported",
            windows.Any(window => window.WindowDuration is not null) ? "window durations: reported" : "window durations: not reported");
    }

    private static string WindowLine(QuotaWindow window)
    {
        StringBuilder builder = new StringBuilder(window.Id).Append(" · ").Append(window.Label);
        if (window.UsedPercent is double used)
        {
            builder.Append(" · ").Append(Math.Round(used, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)).Append('%');
        }

        if (window.ResetsAt is DateTimeOffset resetsAt)
        {
            builder.Append(" · resets ").Append(LocalInstant(resetsAt));
        }

        if (window.WindowDuration is TimeSpan duration)
        {
            builder.Append(" · window ").Append(QuotaFormatting.FormatCountdown(duration));
        }

        if (window.IsPartial)
        {
            builder.Append(" · partial data");
        }

        foreach ((string key, string value) in window.Extra)
        {
            builder.Append(" · ").Append(key).Append(": ").Append(value);
        }

        return builder.ToString();
    }

    private static string NextAttempt(DateTimeOffset? nextAttempt, DateTimeOffset now) => nextAttempt is DateTimeOffset scheduled
        ? LocalInstant(scheduled) + " · in " + QuotaFormatting.FormatCountdown(scheduled - now)
        : "As soon as the next poll is due";

    private static string NextAttemptReason(NextAttemptSource source) => source switch
    {
        NextAttemptSource.Interval => "Normal polling interval",
        NextAttemptSource.FailureBackoff => "Backing off after repeated failures",
        NextAttemptSource.ProviderThrottle => "The provider asked this app to wait",
        NextAttemptSource.ApplicationThrottle => "Waiting after repeated throttling",
        _ => "Normal polling interval"
    };

    private static string? InstantAndAge(DateTimeOffset? instant, DateTimeOffset now) => instant is DateTimeOffset value
        ? LocalInstant(value) + " · " + RelativeTime.FormatAge(now - value)
        : null;

    private static string LocalInstant(DateTimeOffset instant) => instant.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    private static string Tier(MechanismTier tier) => tier == MechanismTier.Official
        ? "Official"
        : "Unofficial — undocumented, may break without notice";

    private static string YesNo(bool value) => value ? "Yes" : "No";
}
