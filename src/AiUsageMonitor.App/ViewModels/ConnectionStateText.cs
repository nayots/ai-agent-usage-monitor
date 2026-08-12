using AiUsageMonitor.Domain;

namespace AiUsageMonitor.App.ViewModels;

/// <summary>
/// The word shown beside each state's glyph. Every glyph is paired with its word everywhere it
/// appears: colour alone never communicates state (PRD §10).
/// </summary>
public static class ConnectionStateText
{
    public static string Label(ConnectionState state) => state switch
    {
        ConnectionState.NotInstalled => "Not installed",
        ConnectionState.Discovering => "Discovering",
        ConnectionState.Waiting => "Waiting",
        ConnectionState.Connected => "Connected",
        ConnectionState.Stale => "Stale",
        ConnectionState.Unavailable => "Unavailable",
        ConnectionState.Unsupported => "Unsupported",
        ConnectionState.Error => "Error",
        _ => state.ToString()
    };
}
