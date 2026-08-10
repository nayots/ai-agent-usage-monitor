namespace AiUsageMonitor.Poc.Providers.Claude;

/// <summary>
/// Redacted, real-shaped Claude Code statusLine payload, retained purely as parser regression test
/// data for <see cref="Domain.DuckTypedQuotaExtractor"/>. The statusLine JSON contract itself was
/// evaluated and rejected as a mechanism (push-only, requires config modification, stale when idle -
/// see PRD ss11.1); this sample is kept only because it is a real recorded shape that proves the
/// shared parser still handles it, including correctly excluding context_window.used_percentage.
/// This is byte-identical to the repo's checked-in <c>fixtures/claude-statusline-sample.json</c>; it
/// is embedded here as well so the POC never depends on the process's working directory to find it.
/// Every value shown in the report using this data is labelled "PARSER REGRESSION TEST (recorded
/// sample)" - never presented as live.
/// </summary>
public static class ClaudeFixtures
{
    public const string StatusLineSampleJson =
        """
        {
          "session_id": "REDACTED", "version": "2.1.226",
          "model": { "id": "claude-haiku-4-5", "display_name": "Haiku 4.5" },
          "cost": { "total_cost_usd": 0.0379152, "total_duration_ms": 8612, "total_api_duration_ms": 3998, "total_lines_added": 0, "total_lines_removed": 0 },
          "context_window": { "total_input_tokens": 37365, "total_output_tokens": 217, "context_window_size": 200000, "used_percentage": 19, "remaining_percentage": 81 },
          "exceeds_200k_tokens": false,
          "rate_limits": {
            "five_hour": { "used_percentage": 47, "resets_at": 1786385400 },
            "seven_day": { "used_percentage": 92, "resets_at": 1786374000 }
          }
        }
        """;
}
