using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AiUsageMonitor.Infrastructure.Providers.Cursor;

/// <summary>
/// Reads Cursor's local <c>state.vscdb</c> - the SQLite database it inherits from VS Code's
/// globalStorage - for the access token Cursor itself stored, plus the non-secret facts beside it.
/// <para>
/// The database is opened READ-ONLY and is never written. Cursor holds it open in WAL mode while
/// it runs; a read-only connection sees committed data correctly, which was verified live against
/// a 4.2 MB write-ahead log. <c>immutable=1</c> is deliberately NOT used: it tells SQLite the file
/// cannot change, which would license it to ignore the log of a database that is actively being
/// written.
/// </para>
/// <para>
/// Three keys are read and three are deliberately refused. <c>cursorAuth/cachedEmail</c>,
/// <c>cursorAuth/cachedScopedProfile</c> and <c>cachedTeam.name</c> identify the user and their
/// organisation, and this application has no use for any of them.
/// <c>cursorAuth/refreshToken</c> is not read either - see PRD ss4.1.1, which forbids this
/// application from participating in a credential's lifecycle at all.
/// </para>
/// </summary>
public static class CursorStateStore
{
    private const string AccessTokenKey = "cursorAuth/accessToken";
    private const string MembershipTypeKey = "cursorAuth/stripeMembershipType";
    private const string CachedTeamKey = "cursorAuth/cachedTeam";

    /// <summary>
    /// <c>%APPDATA%\Cursor\User\globalStorage\state.vscdb</c>, resolved per-user at runtime - the
    /// release artifact has to run on a machine that is not the author's.
    /// </summary>
    public static string DefaultDatabasePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Cursor",
        "User",
        "globalStorage",
        "state.vscdb");

    /// <summary>
    /// The access token as a bare string, or null - never throws - for any failure at all
    /// (missing file, locked or corrupt database, absent row, empty value). The caller maps null
    /// onto <c>Unavailable</c>.
    /// <para>
    /// The token is returned bare and the printable facts come back in
    /// <paramref name="metadata"/>. Only the token's presence is ever recorded in
    /// <paramref name="notes"/>, literally "token: &lt;present, redacted&gt;" or
    /// "token: &lt;absent&gt;" - never its value, its length, or any claim inside it.
    /// </para>
    /// </summary>
    public static string? ReadAccessToken(string databasePath, List<string> notes, out CursorAccountMetadata metadata)
    {
        metadata = CursorAccountMetadata.Empty;

        if (!File.Exists(databasePath))
        {
            notes.Add($"No Cursor state database at {databasePath}. token: <absent>");
            return null;
        }

        try
        {
            using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly");
            connection.Open();

            string? token = ReadItem(connection, AccessTokenKey);
            if (string.IsNullOrWhiteSpace(token))
            {
                notes.Add("Cursor's state database has no stored access token. token: <absent>");
                return null;
            }

            metadata = new CursorAccountMetadata(
                MembershipType: NullIfBlank(ReadItem(connection, MembershipTypeKey)),
                TeamId: ReadTeamId(connection),
                AccessTokenExpiresAt: CursorJwt.TryReadExpiry(token));

            notes.Add("token: <present, redacted>");
            return token;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Exception TYPE only. A SqliteException's message can quote schema or row content,
            // and this database holds the user's email address two rows away from the token.
            notes.Add($"Cursor's state database could not be read ({ex.GetType().Name}). token: <absent>");
            metadata = CursorAccountMetadata.Empty;
            return null;
        }
    }

    private static string? ReadItem(SqliteConnection connection, string key)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM ItemTable WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    /// <summary>
    /// The team id from <c>cachedTeam</c>, or null. The sibling <c>name</c> property is the
    /// organisation's display name and is deliberately not read.
    /// </summary>
    private static long? ReadTeamId(SqliteConnection connection)
    {
        string? raw = ReadItem(connection, CachedTeamKey);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("teamId", out JsonElement teamId)
                && teamId.ValueKind == JsonValueKind.Number
                && teamId.TryGetInt64(out long value)
                    ? value
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
