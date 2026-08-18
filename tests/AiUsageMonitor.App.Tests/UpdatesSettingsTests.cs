using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using AiUsageMonitor.App.Interop;
using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;
using AiUsageMonitor.Infrastructure.Settings;
using AiUsageMonitor.Infrastructure.Updates;

namespace AiUsageMonitor.App.Tests;

public sealed class UpdatesSettingsTests
{
    private const string ScratchKey = @"Software\AiUsageMonitor\tests\UpdatesVm";

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

    [Fact]
    public void The_check_button_is_always_pressable()
    {
        // It used to refuse for a minute after every press, and a button that disables itself for
        // no visible reason reads as broken. Nothing about a check makes a second one unsafe.
        SettingsViewModel model = Model(Service(out _));

        Assert.True(model.CheckForUpdatesCommand.CanExecute(null));

        model.CheckForUpdatesCommand.Execute(null);

        Assert.True(model.CheckForUpdatesCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_running_check_is_visible_and_stops_being_visible_when_it_finishes()
    {
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        SettingsViewModel model = Model(Service(out _, gate.Task));

        Assert.False(model.IsCheckingForUpdates);

        Task running = model.CheckForUpdatesAsync();
        Assert.True(model.IsCheckingForUpdates);

        gate.SetResult();
        await running;

        Assert.False(model.IsCheckingForUpdates);
    }

    [Fact]
    public async Task A_failed_check_still_clears_the_spinner()
    {
        SettingsViewModel model = Model(Service(out _, status: HttpStatusCode.ServiceUnavailable));

        await model.CheckForUpdatesAsync();

        Assert.False(model.IsCheckingForUpdates);
    }

    private static UpdateCheckService Service(
        out StubHandler handler,
        Task? hold = null,
        HttpStatusCode status = HttpStatusCode.OK)
    {
        handler = new StubHandler(status) { Hold = hold };
        return new UpdateCheckService("0.1.3", new GitHubReleaseClient(handler));
    }

    private static SettingsViewModel Model(UpdateCheckService updates)
    {
        string path = Path.Combine(Path.GetTempPath(), "aium-upd-" + Guid.NewGuid().ToString("N"), "settings.json");

        return new SettingsViewModel(
            new SettingsService(new AppSettingsStore(path), AppSettings.Default),
            new StartupRegistration(ScratchKey, "AiUsageMonitorTest", null),
            resetPosition: () => { },
            recheckProviders: () => { },
            providers: Providers,
            updates: updates,
            clock: () => Now);
    }

    private sealed class SilentProbe(string name) : IProviderProbe
    {
        public string Name => name;
        public string Mechanism => "fake";
        public MechanismTier Tier => MechanismTier.Official;
        public Task<ProviderSnapshot> ProbeAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private static IReadOnlyList<ProviderDescriptor> Providers =>
    [
        new ProviderDescriptor("claude-code", "Claude Code", "CC", new SilentProbe("Claude Code"))
    ];

    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        /// <summary>Held open by the test that needs a check to still be running when it looks.</summary>
        public Task? Hold { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Hold is not null)
            {
                await Hold.ConfigureAwait(false);
            }

            return status == HttpStatusCode.OK
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"tag_name":"v0.1.4"}""", Encoding.UTF8, "application/json")
                }
                : new HttpResponseMessage(status);
        }
    }
}
