using AiUsageMonitor.Infrastructure.Providers.Cursor;
using Microsoft.Data.Sqlite;

namespace AiUsageMonitor.Infrastructure.Tests;

public sealed class CursorStateStoreTests
{
    /// <summary>
    /// A stand-in for Cursor's globalStorage database: the one table this application reads,
    /// with whatever rows the test cares about.
    /// </summary>
    private static string WriteDatabase(TempDirectory directory, params (string Key, string Value)[] rows)
    {
        string path = directory.File("state.vscdb");
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        using (SqliteCommand create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE ItemTable (key TEXT UNIQUE ON CONFLICT REPLACE, value BLOB)";
            create.ExecuteNonQuery();
        }

        foreach ((string key, string value) in rows)
        {
            using SqliteCommand insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO ItemTable (key, value) VALUES ($k, $v)";
            insert.Parameters.AddWithValue("$k", key);
            insert.Parameters.AddWithValue("$v", value);
            insert.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
        return path;
    }

    [Fact]
    public void ReadsTokenAndTheThreeNonSecretFacts()
    {
        using var directory = new TempDirectory();
        string path = WriteDatabase(
            directory,
            ("cursorAuth/accessToken", "header.payload.signature"),
            ("cursorAuth/stripeMembershipType", "enterprise"),
            ("cursorAuth/cachedTeam", """{"teamId":13589081,"name":"Some Org"}"""));
        List<string> notes = [];

        string? token = CursorStateStore.ReadAccessToken(path, notes, out CursorAccountMetadata metadata);

        Assert.Equal("header.payload.signature", token);
        Assert.Equal("enterprise", metadata.MembershipType);
        Assert.Equal(13589081L, metadata.TeamId);
    }

    [Fact]
    public void NeverPlacesTheTokenOrTheTeamNameInNotes()
    {
        using var directory = new TempDirectory();
        string path = WriteDatabase(
            directory,
            ("cursorAuth/accessToken", "super-secret-token-value"),
            ("cursorAuth/cachedEmail", "someone@example.com"),
            ("cursorAuth/cachedTeam", """{"teamId":42,"name":"Confidential Org Name"}"""));
        List<string> notes = [];

        CursorStateStore.ReadAccessToken(path, notes, out _);

        string joined = string.Join("\n", notes);
        Assert.DoesNotContain("super-secret-token-value", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("someone@example.com", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("Confidential Org Name", joined, StringComparison.Ordinal);
        Assert.Contains("token: <present, redacted>", joined, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingFileReturnsNullWithoutThrowing()
    {
        using var directory = new TempDirectory();
        List<string> notes = [];

        string? token = CursorStateStore.ReadAccessToken(directory.File("absent.vscdb"), notes, out CursorAccountMetadata metadata);

        Assert.Null(token);
        Assert.Same(CursorAccountMetadata.Empty, metadata);
        Assert.Contains("token: <absent>", string.Join("\n", notes), StringComparison.Ordinal);
    }

    [Fact]
    public void DatabaseWithoutTheAuthRowsReturnsNull()
    {
        using var directory = new TempDirectory();
        string path = WriteDatabase(directory, ("something/else", "value"));
        List<string> notes = [];

        Assert.Null(CursorStateStore.ReadAccessToken(path, notes, out _));
    }

    [Fact]
    public void AFileThatIsNotADatabaseIsReportedByTypeNameOnly()
    {
        using var directory = new TempDirectory();
        string path = directory.File("state.vscdb");
        File.WriteAllText(path, "this is not a sqlite database");
        List<string> notes = [];

        Assert.Null(CursorStateStore.ReadAccessToken(path, notes, out _));
        Assert.DoesNotContain("this is not a sqlite database", string.Join("\n", notes), StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedCachedTeamLeavesTheTeamIdUnknownRatherThanThrowing()
    {
        using var directory = new TempDirectory();
        string path = WriteDatabase(
            directory,
            ("cursorAuth/accessToken", "a.b.c"),
            ("cursorAuth/cachedTeam", "not json at all"));
        List<string> notes = [];

        CursorStateStore.ReadAccessToken(path, notes, out CursorAccountMetadata metadata);

        Assert.Null(metadata.TeamId);
    }

    [Fact]
    public void ReadsTheExpiryFromTheTokensOwnPayload()
    {
        // exp = 2026-09-01T00:00:00Z, expressed the way a JWT does: base64url, unpadded.
        string payload = Base64Url("""{"exp":1788220800}""");
        using var directory = new TempDirectory();
        string path = WriteDatabase(directory, ("cursorAuth/accessToken", $"header.{payload}.signature"));
        List<string> notes = [];

        CursorStateStore.ReadAccessToken(path, notes, out CursorAccountMetadata metadata);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1788220800), metadata.AccessTokenExpiresAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("only.two")]
    [InlineData("a.!!!not-base64!!!.c")]
    public void AnUnreadableExpiryIsUnknownRatherThanExpired(string? jwt)
    {
        // UNKNOWN must never mean EXPIRED: the probe skips a request only when it is confident
        // the request would fail, so an unreadable payload has to fall through and be sent.
        Assert.Null(CursorJwt.TryReadExpiry(jwt));
    }

    [Fact]
    public void AnImplausibleExpiryIsUnknown()
    {
        Assert.Null(CursorJwt.TryReadExpiry($"a.{Base64Url("""{"exp":1}""")}.c"));
    }

    private static string Base64Url(string json) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
