using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AiUsageMonitor.Domain;

namespace AiUsageMonitor.Infrastructure.Providers.Claude;

/// <summary>
/// Claude Code provider probe using the ONLY verified mechanism authorised by PRD ss4.1.1
/// ("Unofficial Mechanism Policy") and ss11: Claude Code's own undocumented usage endpoint,
/// authenticated with the same local OAuth token Claude Code itself writes to
/// <c>%USERPROFILE%\.claude\.credentials.json</c>. This mechanism can be queried on demand and does
/// not go stale merely because no interactive session is running. The statusLine JSON contract was
/// evaluated as an alternative and rejected (push-only, session-only, requires config modification -
/// see PRD ss11.1); it is not used by this application.
///
/// This mechanism is UNOFFICIAL and undocumented - it is not a published Anthropic API, carries no
/// stability guarantee, and must be labelled as such everywhere it surfaces. It must fail safe into
/// an explicit <see cref="ConnectionState"/> rather than fabricate data if it breaks. Per ss4.1.1 /
/// ss23, the OAuth token is used strictly in-memory, only against Anthropic's own first-party host,
/// and is never logged, persisted, echoed in an exception, or placed anywhere in the returned
/// snapshot (not even <see cref="ProviderSnapshot.Notes"/> or a <see cref="QuotaWindow.Extra"/>
/// dictionary). This app never refreshes, rewrites, or otherwise manages the token's lifecycle - that
/// remains entirely Claude Code's own responsibility.
/// </summary>
public sealed class ClaudeOAuthUsageProbe : IProviderProbe
{
    public string Name => "Claude Code";

    // Hard constraint: this is the ONLY network destination this probe (or the whole program) may
    // reach. It is hardcoded, never derived from configuration, a redirect, or provider input.
    private const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
    private const string UserAgent = "ai-agent-usage-monitor-poc/0.1";
    private const string AnthropicBetaHeaderValue = "oauth-2025-04-20";

    private const string Mechanism = "Anthropic OAuth usage endpoint (UNOFFICIAL/undocumented)";
    private const string UpdateModel = "pull (poll)";

    // Message text is mandated verbatim by the task's hard constraints - do not alter.
    private const string TokenRejectedMessage =
        "OAuth token rejected or expired — run any Claude Code session to refresh it";

    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            // Never follow a redirect anywhere - api.anthropic.com is the only permitted destination.
            AllowAutoRedirect = false,
        };

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
    }

    public async Task<ProviderSnapshot> ProbeAsync(CancellationToken ct)
    {
        var notes = new List<string>();

        string credentialsPath = GetCredentialsPath();
        bool credentialsFileExists = File.Exists(credentialsPath);

        // The token lives only in this local variable for the lifetime of this method call. It is
        // read once, used once to build the Authorization header below, and then never touched
        // again - it is never assigned into a field, Notes, Extra, or any exception message.
        string? token = ReadAccessToken(credentialsPath, credentialsFileExists, notes);
        if (token is null)
        {
            // Missing file / missing claudeAiOauth.accessToken -> Unavailable, never an exception.
            return Snapshot(credentialsFileExists, ConnectionState.Unavailable, [], null, null, notes);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("anthropic-beta", AnthropicBetaHeaderValue);
            request.Headers.UserAgent.ParseAdd(UserAgent);

            using HttpResponseMessage response = await Client.SendAsync(request, ct).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                notes.Add($"HTTP {(int)response.StatusCode} ({response.StatusCode}) received from the usage endpoint.");
                return Snapshot(true, ConnectionState.Error, [], null, TokenRejectedMessage, notes);
            }

            if (!response.IsSuccessStatusCode)
            {
                // Never the raw body - top-level JSON key names only, never values.
                IReadOnlyList<string> keys = TryGetTopLevelKeys(body);
                notes.Add(keys.Count > 0
                    ? $"Response top-level JSON keys: {string.Join(", ", keys)}."
                    : "Response body was not a JSON object (or was empty) - no keys to report.");

                string error = $"Unexpected HTTP {(int)response.StatusCode} ({response.StatusCode}) from the usage endpoint.";
                return Snapshot(true, ConnectionState.Error, [], null, error, notes);
            }

            using JsonDocument doc = JsonDocument.Parse(body);

            // Reuse the shared, provider-neutral extractor rather than a bespoke parser. It already
            // handles this endpoint's dialect (utilization + ISO-8601 resets_at) as well as
            // statusLine's (used_percentage + unix seconds) with no code change needed here.
            IReadOnlyList<QuotaWindow> windows = DuckTypedQuotaExtractor.Extract(doc.RootElement);

            notes.Add($"{windows.Count} quota window(s) discovered.");

            IReadOnlyList<string> unhumanized = windows
                .Where(w => w.LabelIsProviderToken)
                .Select(w => w.Id)
                .ToList();
            notes.Add(unhumanized.Count > 0
                ? $"Window key(s) the extractor could not humanise into a friendly \"N unit\" label: {string.Join(", ", unhumanized)}."
                : "Every discovered window key humanised cleanly into a friendly \"N unit\" label.");

            return Snapshot(true, ConnectionState.Connected, windows, DateTimeOffset.UtcNow, null, notes);
        }
        catch (JsonException)
        {
            return Snapshot(true, ConnectionState.Error, [], null, "Response body was not valid JSON.", notes);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The caller's token was not the one that fired - this is the HttpClient's own 10s
            // Timeout, not caller-requested cancellation (which is left to propagate normally).
            return Snapshot(
                true,
                ConnectionState.Error,
                [],
                null,
                $"Request to the usage endpoint timed out after {Client.Timeout.TotalSeconds:0}s.",
                notes);
        }
        catch (HttpRequestException ex)
        {
            // HttpRequestException messages describe connection/TLS/DNS failures only - they never
            // echo request headers, so the token cannot leak through this path.
            return Snapshot(true, ConnectionState.Error, [], null, $"HTTP request failed: {ex.Message}", notes);
        }
    }

    private ProviderSnapshot Snapshot(
        bool installed,
        ConnectionState state,
        IReadOnlyList<QuotaWindow> windows,
        DateTimeOffset? retrievedAt,
        string? error,
        List<string> notes) =>
        new(
            ProviderName: Name,
            Installed: installed,
            Version: null,
            ExecutablePath: null,
            State: state,
            Mechanism: Mechanism,
            Tier: MechanismTier.Unofficial,
            UpdateModel: UpdateModel,
            Windows: windows,
            RetrievedAt: retrievedAt,
            Error: error,
            Notes: notes);

    private static string GetCredentialsPath()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".claude", ".credentials.json");
    }

    /// <summary>
    /// Reads <c>claudeAiOauth.accessToken</c> from the local credential store. Returns null - never
    /// throws - for any failure (missing file, unreadable, malformed JSON, missing property, empty
    /// value); the caller maps that into <see cref="ConnectionState.Unavailable"/>. Only ever records
    /// the presence/absence of the token in <paramref name="notes"/> - literally "token: &lt;present,
    /// redacted&gt;" or "token: &lt;absent&gt;" - never the value, its prefix, or its length.
    /// </summary>
    private static string? ReadAccessToken(string credentialsPath, bool fileExists, List<string> notes)
    {
        if (!fileExists)
        {
            notes.Add($"No credentials file at {credentialsPath}. token: <absent>");
            return null;
        }

        try
        {
            string json = File.ReadAllText(credentialsPath);
            using JsonDocument doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out JsonElement oauth)
                || oauth.ValueKind != JsonValueKind.Object)
            {
                notes.Add("credentials.json has no \"claudeAiOauth\" object. token: <absent>");
                return null;
            }

            if (!oauth.TryGetProperty("accessToken", out JsonElement tokenEl)
                || tokenEl.ValueKind != JsonValueKind.String)
            {
                notes.Add("claudeAiOauth.accessToken not found or not a string. token: <absent>");
                return null;
            }

            string? token = tokenEl.GetString();
            if (string.IsNullOrWhiteSpace(token))
            {
                notes.Add("claudeAiOauth.accessToken was empty. token: <absent>");
                return null;
            }

            notes.Add("token: <present, redacted>");
            return token;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Exception type name only - never ex.Message, which for a JsonException could echo a
            // parse-position snippet of file content.
            notes.Add($"credentials.json could not be read or parsed ({ex.GetType().Name}). token: <absent>");
            return null;
        }
    }

    /// <summary>Top-level JSON object key names only - never values, never the raw body.</summary>
    private static IReadOnlyList<string> TryGetTopLevelKeys(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                ? doc.RootElement.EnumerateObject().Select(p => p.Name).ToList()
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
