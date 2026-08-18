using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AiUsageMonitor.Infrastructure.Updates;

namespace AiUsageMonitor.Infrastructure.Tests;

public sealed class GitHubReleaseClientTests
{
    private const string Body = """{"tag_name":"v0.1.4","name":"0.1.4"}""";

    [Fact]
    public async Task Reads_the_tag_name_as_a_version()
    {
        StubHandler handler = new(_ => Json(HttpStatusCode.OK, Body));
        GitHubReleaseClient client = new(handler);

        ReleaseLookup lookup = await client.FetchLatestAsync(etag: null, CancellationToken.None);

        Assert.Equal(ReleaseLookupOutcome.Succeeded, lookup.Outcome);
        Assert.Equal(ReleaseVersion.Parse("0.1.4"), lookup.Version);
        Assert.Null(lookup.FailureReason);
    }

    [Fact]
    public async Task Sends_a_constant_user_agent_carrying_no_version()
    {
        // Spec D2. The constant is what keeps this request outside PRD §23's prohibition on
        // transmitting settings to a third party. Adding the running version "for diagnostics"
        // would put the feature inside it, so this assertion is deliberately strict.
        StubHandler handler = new(_ => Json(HttpStatusCode.OK, Body));
        GitHubReleaseClient client = new(handler);

        await client.FetchLatestAsync(etag: null, CancellationToken.None);

        Assert.Equal("AiUsageMonitor", handler.Request!.Headers.UserAgent.ToString());
        Assert.Null(handler.Request.Headers.Authorization);
        Assert.Equal(string.Empty, handler.Request.RequestUri!.Query);
        Assert.Equal(GitHubReleaseClient.LatestReleaseApiUrl, handler.Request.RequestUri.ToString());
    }

    [Fact]
    public async Task Sends_the_stored_etag_and_reports_not_modified()
    {
        StubHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.NotModified));
        GitHubReleaseClient client = new(handler);

        ReleaseLookup lookup = await client.FetchLatestAsync("\"abc\"", CancellationToken.None);

        Assert.Equal("\"abc\"", handler.Request!.Headers.IfNoneMatch.ToString());
        Assert.Equal(ReleaseLookupOutcome.NotModified, lookup.Outcome);
        Assert.Equal("\"abc\"", lookup.ETag);
    }

    [Fact]
    public async Task Returns_the_response_etag_on_success()
    {
        StubHandler handler = new(_ =>
        {
            HttpResponseMessage response = Json(HttpStatusCode.OK, Body);
            response.Headers.ETag = new EntityTagHeaderValue("\"xyz\"");
            return response;
        });

        ReleaseLookup lookup = await new GitHubReleaseClient(handler)
            .FetchLatestAsync(etag: null, CancellationToken.None);

        Assert.Equal("\"xyz\"", lookup.ETag);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Treats_every_unsuccessful_status_as_a_failed_check(HttpStatusCode status)
    {
        // Spec D8: a rate-limited check is a failed check. It is not ThrottleAdvice and gets no
        // escalating backoff - that vocabulary belongs to provider quota reads.
        StubHandler handler = new(_ => new HttpResponseMessage(status));

        ReleaseLookup lookup = await new GitHubReleaseClient(handler)
            .FetchLatestAsync(etag: null, CancellationToken.None);

        Assert.Equal(ReleaseLookupOutcome.Failed, lookup.Outcome);
        Assert.Null(lookup.Version);
        Assert.False(string.IsNullOrWhiteSpace(lookup.FailureReason));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"name":"0.1.4"}""")]
    [InlineData("""{"tag_name":"latest"}""")]
    [InlineData("""{"tag_name":null}""")]
    [InlineData("""{"tag_name":123}""")]
    public async Task Fails_rather_than_guessing_when_the_body_names_no_usable_version(string body)
    {
        StubHandler handler = new(_ => Json(HttpStatusCode.OK, body));

        ReleaseLookup lookup = await new GitHubReleaseClient(handler)
            .FetchLatestAsync(etag: null, CancellationToken.None);

        Assert.Equal(ReleaseLookupOutcome.Failed, lookup.Outcome);
        Assert.Null(lookup.Version);
    }

    [Fact]
    public async Task Turns_a_network_failure_into_a_result_not_an_exception()
    {
        StubHandler handler = new(_ => throw new HttpRequestException("no network"));

        ReleaseLookup lookup = await new GitHubReleaseClient(handler)
            .FetchLatestAsync(etag: null, CancellationToken.None);

        Assert.Equal(ReleaseLookupOutcome.Failed, lookup.Outcome);
    }

    [Fact]
    public async Task Turns_a_timeout_into_a_result_not_an_exception()
    {
        StubHandler handler = new(_ => throw new TaskCanceledException("timed out"));

        ReleaseLookup lookup = await new GitHubReleaseClient(handler)
            .FetchLatestAsync(etag: null, CancellationToken.None);

        Assert.Equal(ReleaseLookupOutcome.Failed, lookup.Outcome);
    }

    [Fact]
    public async Task Lets_a_caller_cancellation_propagate()
    {
        StubHandler handler = new(_ => Json(HttpStatusCode.OK, Body));
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new GitHubReleaseClient(handler).FetchLatestAsync(null, cancelled.Token));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;
            return Task.FromResult(respond(request));
        }
    }
}
