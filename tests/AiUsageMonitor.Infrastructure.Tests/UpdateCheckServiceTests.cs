using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AiUsageMonitor.Infrastructure.Updates;

namespace AiUsageMonitor.Infrastructure.Tests;

public sealed class UpdateCheckServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Reports_an_update_when_the_feed_is_ahead()
    {
        UpdateCheckService service = Service("0.1.3", "v0.1.4");

        UpdateStatus status = await service.CheckAsync(manual: true, Now, CancellationToken.None);

        Assert.Equal(UpdateAvailability.UpdateAvailable, status.Availability);
        Assert.Equal(ReleaseVersion.Parse("0.1.4"), status.Latest);
        Assert.Equal(ReleaseVersion.Parse("0.1.3"), status.Current);
        Assert.Equal(Now, status.LastCheckedUtc);
    }

    [Theory]
    [InlineData("0.1.3", "v0.1.3")]
    [InlineData("0.1.4", "v0.1.3")]
    public async Task Reports_current_when_the_feed_is_level_or_behind(string running, string tag)
    {
        UpdateStatus status = await Service(running, tag)
            .CheckAsync(manual: true, Now, CancellationToken.None);

        Assert.Equal(UpdateAvailability.Current, status.Availability);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    public async Task Never_claims_a_verdict_when_the_running_version_is_unreadable(string running)
    {
        // The application telling a user they are up to date when it cannot read its own version
        // is fabrication in the same sense a provider adapter is forbidden from.
        UpdateStatus status = await Service(running, "v0.1.4")
            .CheckAsync(manual: true, Now, CancellationToken.None);

        Assert.Equal(UpdateAvailability.Unknown, status.Availability);
    }

    [Fact]
    public async Task A_failed_check_yields_no_verdict_and_records_the_reason()
    {
        UpdateCheckService failing = Service("0.1.3", "v0.1.4", status: HttpStatusCode.ServiceUnavailable);

        UpdateStatus status = await failing.CheckAsync(manual: true, Now, CancellationToken.None);

        Assert.Equal(UpdateAvailability.Unknown, status.Availability);
        Assert.False(string.IsNullOrWhiteSpace(status.FailureReason));
    }

    [Fact]
    public async Task A_failure_after_a_success_keeps_the_version_it_had_found()
    {
        StubHandler handler = new(Body("v0.1.4"));
        UpdateCheckService service = new("0.1.3", new GitHubReleaseClient(handler));
        await service.CheckAsync(manual: true, Now, CancellationToken.None);

        handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        UpdateStatus status = await service.CheckAsync(manual: true, Now.AddDays(2), CancellationToken.None);

        // The verdict drops to Unknown - the application no longer knows this is still true - but
        // the version it found is kept, so the settings page can still name what it last saw.
        Assert.Equal(UpdateAvailability.Unknown, status.Availability);
        Assert.Equal(ReleaseVersion.Parse("0.1.4"), status.Latest);
    }

    [Fact]
    public async Task Keeps_the_verdict_across_a_not_modified_reply()
    {
        StubHandler handler = new(Body("v0.1.4"));
        UpdateCheckService service = new("0.1.3", new GitHubReleaseClient(handler));

        await service.CheckAsync(manual: true, Now, CancellationToken.None);
        handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotModified);

        UpdateStatus status = await service.CheckAsync(manual: true, Now.AddDays(1), CancellationToken.None);

        Assert.Equal(UpdateAvailability.UpdateAvailable, status.Availability);
        Assert.Equal(ReleaseVersion.Parse("0.1.4"), status.Latest);
        Assert.Equal(Now.AddDays(1), status.LastCheckedUtc);
    }

    [Fact]
    public async Task Stores_the_etag_and_sends_it_next_time()
    {
        UpdateCheckService service = Service("0.1.3", "v0.1.4", etag: "\"abc\"");

        await service.CheckAsync(manual: true, Now, CancellationToken.None);

        Assert.Equal("\"abc\"", service.ETag);
    }

    [Fact]
    public async Task Waits_out_the_startup_delay_then_checks_daily()
    {
        // Spec D7: the first check is delayed so it never competes with the first quota read.
        UpdateCheckService service = Service("0.1.3", "v0.1.4", startedAt: Now);

        Assert.False(service.IsDue(Now));
        Assert.False(service.IsDue(Now.AddSeconds(19)));
        Assert.True(service.IsDue(Now.AddSeconds(20)));

        await service.CheckAsync(manual: false, Now.AddSeconds(20), CancellationToken.None);

        Assert.False(service.IsDue(Now.AddHours(23)));
        Assert.True(service.IsDue(Now.AddHours(24).AddSeconds(20)));
    }

    [Fact]
    public void A_stale_last_check_still_waits_out_the_startup_delay()
    {
        // The case the plan originally missed: lastCheckedUtc + 24h is already in the past when a
        // machine has been off for days, which would make the check due on the first tick and race
        // the first quota read - exactly what the delay exists to prevent.
        UpdateCheckService service = Service(
            "0.1.3", "v0.1.4", startedAt: Now, lastCheckedUtc: Now.AddDays(-3));

        Assert.False(service.IsDue(Now));
        Assert.True(service.IsDue(Now.AddSeconds(20)));
    }

    [Fact]
    public void A_recent_last_check_survives_a_restart()
    {
        UpdateCheckService service = Service(
            "0.1.3", "v0.1.4", startedAt: Now, lastCheckedUtc: Now.AddHours(-1));

        Assert.False(service.IsDue(Now.AddSeconds(20)));
        Assert.True(service.IsDue(Now.AddHours(23)));
    }

    [Fact]
    public async Task Retries_an_hour_after_a_failure_rather_than_a_day()
    {
        UpdateCheckService service = Service("0.1.3", "v0.1.4", status: HttpStatusCode.ServiceUnavailable);

        await service.CheckAsync(manual: false, Now, CancellationToken.None);

        Assert.False(service.IsDue(Now.AddMinutes(59)));
        Assert.True(service.IsDue(Now.AddHours(1)));
    }

    [Fact]
    public void Is_never_due_while_disabled()
    {
        UpdateCheckService service = Service("0.1.3", "v0.1.4");
        service.Enabled = false;

        Assert.False(service.IsDue(Now));
        Assert.False(service.IsDue(Now.AddDays(7)));
    }

    [Fact]
    public async Task A_manual_check_works_while_disabled()
    {
        UpdateCheckService service = Service("0.1.3", "v0.1.4");
        service.Enabled = false;

        UpdateStatus status = await service.CheckAsync(manual: true, Now, CancellationToken.None);

        Assert.Equal(UpdateAvailability.UpdateAvailable, status.Availability);
    }

    [Fact]
    public async Task A_manual_check_is_refused_inside_its_cooldown()
    {
        UpdateCheckService service = Service("0.1.3", "v0.1.4");

        await service.CheckAsync(manual: true, Now, CancellationToken.None);

        Assert.False(service.CanCheckManually(Now.AddSeconds(59)));
        Assert.True(service.CanCheckManually(Now.AddSeconds(60)));
    }

    [Fact]
    public async Task Announces_only_when_the_status_actually_changed()
    {
        UpdateCheckService service = Service("0.1.3", "v0.1.3");
        int announcements = 0;
        service.StatusChanged += (_, _) => announcements++;

        await service.CheckAsync(manual: true, Now, CancellationToken.None);
        await service.CheckAsync(manual: true, Now, CancellationToken.None);

        Assert.Equal(1, announcements);
    }

    [Fact]
    public async Task Shares_one_check_rather_than_starting_a_second()
    {
        // The handler holds the first request open until both callers have arrived, so "still in
        // flight" is a fact rather than a bet on the first check being slower than two method
        // calls - a bet the release build loses.
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        StubHandler handler = new(Body("v0.1.4")) { Hold = gate.Task };
        UpdateCheckService service = new("0.1.3", new GitHubReleaseClient(handler));

        Task<UpdateStatus> first = service.CheckAsync(manual: false, Now, CancellationToken.None);
        Task<UpdateStatus> second = service.CheckAsync(manual: true, Now, CancellationToken.None);

        gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(await first, await second);
    }

    [Fact]
    public async Task Starts_a_second_check_after_one_that_finished_inline()
    {
        // The defect this guards was invisible in Debug and failed the release build. When a run
        // never suspends - every await already complete by the time it is reached - it finishes
        // inline, so its finally clears the in-flight slot *before* CheckAsync assigns it. The
        // completed task then stayed parked there for the life of the process and every later
        // check returned that stale answer without asking again. A handler that never yields
        // makes the timing deterministic instead of a race the faster build happens to lose.
        StubHandler handler = new(Body("v0.1.4")) { Yields = false };
        UpdateCheckService service = new("0.1.3", new GitHubReleaseClient(handler));

        await service.CheckAsync(manual: true, Now, CancellationToken.None);
        UpdateStatus second = await service.CheckAsync(manual: true, Now.AddDays(1), CancellationToken.None);

        Assert.Equal(2, handler.RequestCount);
        Assert.Equal(Now.AddDays(1), second.LastCheckedUtc);
    }

    private static UpdateCheckService Service(
        string running,
        string tag,
        HttpStatusCode status = HttpStatusCode.OK,
        string? etag = null,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? lastCheckedUtc = null)
    {
        StubHandler handler = new(_ =>
        {
            if (status != HttpStatusCode.OK)
            {
                return new HttpResponseMessage(status);
            }

            HttpResponseMessage response = Body(tag)(null!);

            if (etag is not null)
            {
                response.Headers.ETag = new EntityTagHeaderValue(etag);
            }

            return response;
        });

        return new UpdateCheckService(
            running,
            new GitHubReleaseClient(handler),
            lastCheckedUtc: lastCheckedUtc,
            startedAt: startedAt);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Body(string tag) => _ =>
        new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{"tag_name":"{{tag}}"}""", Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = respond;

        public int RequestCount { get; private set; }

        /// <summary>
        /// Off only for the inline-completion test, which needs the whole run to finish without
        /// ever suspending. Everywhere else this stays on.
        /// </summary>
        public bool Yields { get; init; } = true;

        /// <summary>
        /// Holds the request open until the test releases it. Set only by the shared-check test,
        /// which needs the first check to still be running when the second caller arrives - a
        /// bare <see cref="Task.Yield"/> leaves that to chance, and the release build is fast
        /// enough to finish the first run in the gap between the two calls.
        /// </summary>
        public Task? Hold { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;

            if (Hold is not null)
            {
                await Hold.ConfigureAwait(false);
            }
            else if (Yields)
            {
                await Task.Yield();
            }

            return Respond(request);
        }
    }
}
