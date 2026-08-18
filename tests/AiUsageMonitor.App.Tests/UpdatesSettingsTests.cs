using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Infrastructure.Updates;

namespace AiUsageMonitor.App.Tests;

public sealed class UpdatesSettingsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_sidebar_offers_updates_as_its_own_page()
    {
        // Spec D10: not folded into Refresh. "Refresh" already means provider polling cadence here.
        Assert.Contains(SettingsPageKind.Updates, Enum.GetValues<SettingsPageKind>());
    }

    [Fact]
    public void States_that_nothing_is_known_before_a_check_has_run()
    {
        UpdateCheckService service = new("0.1.3");

        Assert.Equal(UpdateAvailability.Unknown, service.Status.Availability);
    }

    [Fact]
    public void Names_the_newer_version_from_parsed_numbers()
    {
        string text = UpdateCopy.StatusText(new UpdateStatus(
            UpdateAvailability.UpdateAvailable,
            ReleaseVersion.Parse("0.1.3"),
            ReleaseVersion.Parse("0.1.4"),
            Now,
            null));

        Assert.Contains("0.1.4", text);
    }

    [Fact]
    public void Says_up_to_date_only_when_it_actually_knows()
    {
        string current = UpdateCopy.StatusText(new UpdateStatus(
            UpdateAvailability.Current, ReleaseVersion.Parse("0.1.3"), ReleaseVersion.Parse("0.1.3"), Now, null));
        string unknown = UpdateCopy.StatusText(new UpdateStatus(
            UpdateAvailability.Unknown, ReleaseVersion.Parse("0.1.3"), null, null, "no network"));

        Assert.Contains("up to date", current, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("up to date", unknown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reports_a_failure_reason_when_there_is_one()
    {
        string text = UpdateCopy.StatusText(new UpdateStatus(
            UpdateAvailability.Unknown, ReleaseVersion.Parse("0.1.3"), null, null, "The check timed out."));

        Assert.Equal("The check timed out.", text);
    }

    [Fact]
    public void Says_when_the_last_check_ran()
    {
        Assert.Equal("Not checked yet", UpdateCopy.LastCheckedText(null, Now));
        Assert.Equal("Checked just now", UpdateCopy.LastCheckedText(Now.AddSeconds(-5), Now));
        Assert.Equal("Checked 5 minutes ago", UpdateCopy.LastCheckedText(Now.AddMinutes(-5), Now));
        Assert.Equal("Checked 1 hour ago", UpdateCopy.LastCheckedText(Now.AddHours(-1), Now));
        Assert.Equal("Checked 2 days ago", UpdateCopy.LastCheckedText(Now.AddDays(-2), Now));
    }

    [Fact]
    public void The_tray_item_names_the_version_from_parsed_numbers()
    {
        string text = UpdateCopy.TrayText(new UpdateStatus(
            UpdateAvailability.UpdateAvailable,
            ReleaseVersion.Parse("0.1.3"),
            ReleaseVersion.Parse("0.1.4"),
            Now,
            null));

        Assert.Equal("Update available (0.1.4)", text);
    }
}
