using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AiUsageMonitor.Infrastructure.Updates;

/// <summary>What one lookup did.</summary>
public enum ReleaseLookupOutcome
{
    /// <summary>A version was read.</summary>
    Succeeded,

    /// <summary>The stored ETag still matched, so nothing changed and nothing was re-read.</summary>
    NotModified,

    /// <summary>Nothing usable came back. Never a reason to throw, and never a reason to guess.</summary>
    Failed
}

/// <summary>
/// One lookup's result. <see cref="FailureReason"/> is user-facing copy and must never carry a URL,
/// a response body, a header or an exception message.
/// </summary>
public sealed record ReleaseLookup(
    ReleaseLookupOutcome Outcome,
    ReleaseVersion? Version,
    string? ETag,
    string? FailureReason);

/// <summary>
/// Asks GitHub's public release feed what the latest release is, and answers with a version or a
/// failure. Knows nothing about cadence, settings or the running build - see
/// <see cref="UpdateCheckService"/> for those.
/// <para>
/// This is the documented public REST API, not the "website scraping" PRD §23 forbids, and it is
/// unauthenticated: there is no token, and there must never be one. §23's prohibition is on
/// transmitting usage data, diagnostics or settings to a third party, and this request transmits
/// none of the three - which is true only for as long as <see cref="UserAgent"/> stays constant.
/// </para>
/// </summary>
public sealed class GitHubReleaseClient
{
    /// <summary>The only host this type may reach. Hardcoded, never configuration, never a redirect.</summary>
    public const string LatestReleaseApiUrl =
        "https://api.github.com/repos/nayots/ai-agent-usage-monitor/releases/latest";

    /// <summary>
    /// Where the user is sent. A compile-time constant on purpose - see the note beside
    /// <c>WidgetWindow.ProjectUrl</c>: a browser must never be launched at a URL that arrived over
    /// the network, which rules out the response's own <c>html_url</c>.
    /// </summary>
    public const string ReleasePageUrl =
        "https://github.com/nayots/ai-agent-usage-monitor/releases/latest";

    /// <summary>
    /// Constant, and carrying no version. GitHub requires a User-Agent; it does not require an
    /// informative one. Putting the running build here would transmit a piece of the user's
    /// configuration to a third party, which is exactly what PRD §23 forbids. Do not "improve" it.
    /// </summary>
    private const string UserAgent = "AiUsageMonitor";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly HttpClient Shared = CreateClient();

    private readonly HttpClient _client;

    public GitHubReleaseClient(HttpMessageHandler? handler = null) =>
        _client = handler is null ? Shared : CreateClient(handler);

    private static HttpClient CreateClient() =>
        CreateClient(new HttpClientHandler());

    private static HttpClient CreateClient(HttpMessageHandler handler)
    {
        if (handler is HttpClientHandler httpHandler)
        {
            // Never follow a redirect: api.github.com is the only permitted destination, and a
            // redirect is the one way a response could choose where this application connects.
            httpHandler.AllowAutoRedirect = false;
            httpHandler.UseCookies = false;
        }

        return new HttpClient(handler) { Timeout = Timeout };
    }

    public async Task<ReleaseLookup> FetchLatestAsync(string? etag, CancellationToken cancellationToken)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, LatestReleaseApiUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.ParseAdd(UserAgent);

            if (!string.IsNullOrWhiteSpace(etag))
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            }

            using HttpResponseMessage response =
                await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new ReleaseLookup(ReleaseLookupOutcome.NotModified, null, etag, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                // Spec D8. A 403 with the rate-limit headers and a 429 land here with everything
                // else: a failed check, retried on the ordinary schedule. The status number is not
                // shown to the user - it would explain nothing they can act on.
                return Failure("GitHub could not be asked about releases just now.");
            }

            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            ReleaseVersion? version = ReleaseVersion.Parse(ReadTagName(body));

            return version is null
                ? Failure("The latest release did not name a version this application understands.")
                : new ReleaseLookup(ReleaseLookupOutcome.Succeeded, version, response.Headers.ETag?.ToString(), null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The client's own 10s timeout, not the caller's token. A caller who cancelled gets the
            // exception - the guard is what tells the two apart.
            return Failure("The check for updates timed out.");
        }
        catch (HttpRequestException)
        {
            return Failure("The check for updates could not reach github.com.");
        }
    }

    private static string? ReadTagName(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("tag_name", out JsonElement tag)
                && tag.ValueKind == JsonValueKind.String
                    ? tag.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ReleaseLookup Failure(string reason) =>
        new(ReleaseLookupOutcome.Failed, null, null, reason);
}
