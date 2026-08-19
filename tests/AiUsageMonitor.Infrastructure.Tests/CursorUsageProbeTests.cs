using System.Net;
using System.Net.Http.Headers;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers.Cursor;
using Microsoft.Data.Sqlite;

namespace AiUsageMonitor.Infrastructure.Tests;

public sealed class CursorUsageProbeTests
{
    private const string ExePath = @"C:\tools\Cursor.exe";
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    // exp = 2026-10-01, comfortably in the future relative to Now.
    private static readonly string LiveToken = Jwt(1790899200);

    [Fact]
    public async Task AbsentInstallReturnsNotInstalledWithoutIssuingAnHttpRequest()
    {
        var handler = new RoutingHandler();
        var probe = new CursorUsageProbe(handler, locateExecutable: () => null);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.NotInstalled, snapshot.State);
        Assert.False(snapshot.Installed);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task InstalledWithoutADatabaseIsUnavailableWithoutIssuingAnHttpRequest()
    {
        using var directory = new TempDirectory();
        var handler = new RoutingHandler();
        var probe = CreateProbe(handler, directory.File("absent.vscdb"));

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Unavailable, snapshot.State);
        Assert.True(snapshot.Installed);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task AnAlreadyExpiredTokenSkipsTheRequestEntirely()
    {
        using var directory = new TempDirectory();
        // exp = 2026-01-01, long past Now.
        string path = WriteDatabase(directory, Jwt(1767225600));
        var handler = new RoutingHandler();
        var probe = CreateProbe(handler, path);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Unavailable, snapshot.State);
        Assert.Equal(0, handler.RequestCount);
        Assert.Contains("Cursor", snapshot.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnUnreadableExpirySendsTheRequestAnyway()
    {
        // UNKNOWN must never be treated as EXPIRED: a bug in the expiry check must not be able to
        // disable a working widget.
        using var directory = new TempDirectory();
        string path = WriteDatabase(directory, "not-a-jwt-at-all");
        var handler = IndividualSeat();
        var probe = CreateProbe(handler, path);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Connected, snapshot.State);
        Assert.True(handler.RequestCount > 0);
    }

    [Fact]
    public async Task AnIndividualSeatIsReadInTwoRequests()
    {
        using var directory = new TempDirectory();
        string path = WriteDatabase(directory, LiveToken, membershipType: "pro", teamId: null);
        var handler = IndividualSeat();
        var probe = CreateProbe(handler, path);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Connected, snapshot.State);
        Assert.Equal(MechanismTier.Unofficial, snapshot.Tier);
        QuotaWindow window = Assert.Single(snapshot.Windows);
        Assert.Equal("cursor:plan_spend", window.Id);
        Assert.Equal(25.0, window.UsedPercent!.Value, 3);
        Assert.Equal(2, handler.RequestCount);
        Assert.DoesNotContain("GetFilteredUsageEvents", handler.Methods);
    }

    [Fact]
    public async Task AnEnterpriseSeatFallsThroughToTheEventTotal()
    {
        using var directory = new TempDirectory();
        string path = WriteDatabase(directory, LiveToken, membershipType: "enterprise", teamId: 13589081);
        var handler = EnterpriseSeat(totalEvents: 2, pages: [Events(700.61, 470.0)]);
        var probe = CreateProbe(handler, path);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Connected, snapshot.State);
        QuotaWindow window = Assert.Single(snapshot.Windows);
        Assert.Equal("cursor:cycle_spend", window.Id);
        Assert.Equal(11.7061, window.UsedPercent!.Value, 3);
        Assert.Equal("11.71", window.Extra["cursor.spentUsd"]);
    }

    [Fact]
    public async Task ItNeverAsksForTheTeamRoster()
    {
        using var directory = new TempDirectory();
        string path = WriteDatabase(directory, LiveToken, membershipType: "enterprise", teamId: 13589081);
        var handler = EnterpriseSeat(totalEvents: 2, pages: [Events(700.61, 470.0)]);
        var probe = CreateProbe(handler, path);

        await probe.ProbeAsync(CancellationToken.None);

        Assert.DoesNotContain("GetTeamMembers", handler.Methods);
        Assert.DoesNotContain("GetTeamSpend", handler.Methods);
        Assert.DoesNotContain("oauth/token", string.Join(" ", handler.Paths));
    }

    [Fact]
    public async Task AnUnchangedEventCountReusesTheTotalInsteadOfRefetchingThePages()
    {
        using var directory = new TempDirectory();
        string path = WriteDatabase(directory, LiveToken, membershipType: "enterprise", teamId: 13589081);
        var handler = EnterpriseSeat(totalEvents: 2, pages: [Events(700.61, 470.0)]);
        var probe = CreateProbe(handler, path);

        await probe.ProbeAsync(CancellationToken.None);
        int afterFirst = handler.FullPageRequests;
        ProviderSnapshot second = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(afterFirst, handler.FullPageRequests);
        Assert.Equal(11.7061, Assert.Single(second.Windows).UsedPercent!.Value, 3);
    }

    [Fact]
    public async Task AChangedEventCountRefetches()
    {
        using var directory = new TempDirectory();
        string path = WriteDatabase(directory, LiveToken, membershipType: "enterprise", teamId: 13589081);
        var handler = EnterpriseSeat(totalEvents: 2, pages: [Events(700.61, 470.0)]);
        var probe = CreateProbe(handler, path);

        await probe.ProbeAsync(CancellationToken.None);
        int afterFirst = handler.FullPageRequests;
        handler.SetEvents(totalEvents: 3, pages: [Events(700.61, 470.0, 100.0)]);
        await probe.ProbeAsync(CancellationToken.None);

        Assert.True(handler.FullPageRequests > afterFirst);
    }

    [Fact]
    public async Task AChangedSignInWithTheSameCycleAndCountDoesNotReuseAnotherUsersTotal()
    {
        using var directory = new TempDirectory();
        string firstPath = WriteDatabase(directory, LiveToken, membershipType: "enterprise", teamId: 13589081);
        string secondPath = WriteDatabase(directory, Jwt(1790985600), membershipType: "enterprise", teamId: 13589081);
        string currentPath = firstPath;
        var handler = EnterpriseSeat(totalEvents: 2, pages: [Events(700.61, 470.0)]);
        var probe = new CursorUsageProbe(
            handler,
            locateExecutable: () => ExePath,
            readVersion: _ => "3.16.29",
            databasePath: () => currentPath,
            clock: () => Now);

        await probe.ProbeAsync(CancellationToken.None);
        int afterFirst = handler.FullPageRequests;
        currentPath = secondPath;
        await probe.ProbeAsync(CancellationToken.None);

        Assert.True(handler.FullPageRequests > afterFirst);
    }

    [Fact]
    public async Task EventsBelongingToMoreThanOneAccountRefuseToProduceAFigure()
    {
        using var directory = new TempDirectory();
        string path = WriteDatabase(directory, LiveToken, membershipType: "enterprise", teamId: 13589081);
        var handler = EnterpriseSeat(
            totalEvents: 2,
            pages: ["""{"chargedCents":5,"owningUser":"1"},{"chargedCents":5,"owningUser":"2"}"""]);
        var probe = CreateProbe(handler, path);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Unavailable, snapshot.State);
        Assert.Empty(snapshot.Windows);
        Assert.Contains("more than one", snapshot.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnEnterpriseSeatWithNoTeamIdIsUnsupportedRatherThanZero()
    {
        using var directory = new TempDirectory();
        string path = WriteDatabase(directory, LiveToken, membershipType: "enterprise", teamId: null);
        var handler = EnterpriseSeat(totalEvents: 0, pages: [""]);
        var probe = CreateProbe(handler, path);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Unsupported, snapshot.State);
        Assert.Empty(snapshot.Windows);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ARejectedTokenIsAnErrorThatTellsTheUserWhatToDo(HttpStatusCode status)
    {
        using var directory = new TempDirectory();
        string path = WriteDatabase(directory, LiveToken);
        var handler = new RoutingHandler { Fallback = _ => new HttpResponseMessage(status) };
        var probe = CreateProbe(handler, path);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Error, snapshot.State);
        Assert.Contains("Cursor", snapshot.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AThrottleCarriesTheProvidersOwnRetryInstant()
    {
        using var directory = new TempDirectory();
        string path = WriteDatabase(directory, LiveToken);
        var handler = new RoutingHandler
        {
            Fallback = _ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(5));
                return response;
            },
        };
        var probe = CreateProbe(handler, path);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Error, snapshot.State);
        Assert.NotNull(snapshot.Throttle);
        Assert.Equal(Now.AddMinutes(5), snapshot.Throttle!.NotBefore);
    }

    [Fact]
    public async Task AnUnexpectedRequestFailureReturnsASanitizedErrorSnapshot()
    {
        using var directory = new TempDirectory();
        string path = WriteDatabase(directory, LiveToken);
        var handler = new RoutingHandler
        {
            Fallback = _ => throw new InvalidOperationException($"unsafe response content: {LiveToken}"),
        };
        var probe = CreateProbe(handler, path);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Error, snapshot.State);
        Assert.Contains("InvalidOperationException", snapshot.Error!, StringComparison.Ordinal);
        Assert.DoesNotContain(LiveToken, snapshot.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnexpectedLocalReadFailureReturnsASanitizedErrorSnapshot()
    {
        var probe = new CursorUsageProbe(
            new RoutingHandler(),
            locateExecutable: () => ExePath,
            readVersion: _ => "3.16.29",
            databasePath: () => throw new IOException($"unsafe local state: {LiveToken}"),
            clock: () => Now);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Error, snapshot.State);
        Assert.Contains("Communication", snapshot.Error!, StringComparison.Ordinal);
        Assert.DoesNotContain(LiveToken, snapshot.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoSnapshotFieldEverCarriesTheToken()
    {
        using var directory = new TempDirectory();
        string path = WriteDatabase(directory, LiveToken);
        var handler = IndividualSeat();
        var probe = CreateProbe(handler, path);

        ProviderSnapshot snapshot = await probe.ProbeAsync(CancellationToken.None);

        string everything = string.Join(
            "\n",
            [
                snapshot.Error ?? string.Empty,
                snapshot.Mechanism,
                string.Join("\n", snapshot.Notes),
                string.Join("\n", snapshot.Windows.SelectMany(w => w.Extra).Select(p => $"{p.Key}={p.Value}")),
            ]);

        Assert.DoesNotContain(LiveToken, everything, StringComparison.Ordinal);
        Assert.Contains("token: <present, redacted>", everything, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheBearerTokenIsSentAndTheHostIsAlwaysCursors()
    {
        using var directory = new TempDirectory();
        string path = WriteDatabase(directory, LiveToken);
        var handler = IndividualSeat();
        var probe = CreateProbe(handler, path);

        await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal("Bearer", handler.Authorization!.Scheme);
        Assert.Equal(LiveToken, handler.Authorization.Parameter);
        Assert.All(handler.Hosts, host => Assert.Equal("api2.cursor.sh", host));
    }

    // ---- helpers -------------------------------------------------------------------------

    private static CursorUsageProbe CreateProbe(RoutingHandler handler, string databasePath) =>
        new(handler,
            locateExecutable: () => ExePath,
            readVersion: _ => "3.16.29",
            databasePath: () => databasePath,
            clock: () => Now);

    private static string Jwt(long expUnixSeconds)
    {
        string payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($$"""{"exp":{{expUnixSeconds}}}"""))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"header.{payload}.signature";
    }

    private static string Events(params double[] cents) =>
        string.Join(",", cents.Select(c =>
            $$"""{"chargedCents":{{c.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"owningUser":"1"}"""));

    private static string WriteDatabase(
        TempDirectory directory, string token, string? membershipType = "pro", long? teamId = null)
    {
        string path = directory.File($"state-{Guid.NewGuid():N}.vscdb");
        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            using (SqliteCommand create = connection.CreateCommand())
            {
                create.CommandText = "CREATE TABLE ItemTable (key TEXT UNIQUE ON CONFLICT REPLACE, value BLOB)";
                create.ExecuteNonQuery();
            }

            void Insert(string key, string value)
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "INSERT INTO ItemTable (key, value) VALUES ($k, $v)";
                command.Parameters.AddWithValue("$k", key);
                command.Parameters.AddWithValue("$v", value);
                command.ExecuteNonQuery();
            }

            Insert("cursorAuth/accessToken", token);
            if (membershipType is not null)
            {
                Insert("cursorAuth/stripeMembershipType", membershipType);
            }

            if (teamId is long id)
            {
                Insert("cursorAuth/cachedTeam", $$"""{"teamId":{{id}},"name":"Some Org"}""");
            }
        }

        SqliteConnection.ClearAllPools();
        return path;
    }

    /// <summary>An individual seat: planUsage present, so the team path is never reached.</summary>
    private static RoutingHandler IndividualSeat() => new()
    {
        Responses =
        {
            ["GetCurrentPeriodUsage"] = """{"planUsage":{"totalSpend":2500,"limit":10000}}""",
            ["GetPlanInfo"] = """{"planInfo":{"planName":"Pro","billingCycleEnd":"1788220800000"}}""",
        },
    };

    /// <summary>
    /// An enterprise seat shaped exactly like the measured one: GetCurrentPeriodUsage carries
    /// neither planUsage nor spendLimitUsage.
    /// </summary>
    private static RoutingHandler EnterpriseSeat(int totalEvents, string[] pages)
    {
        var handler = new RoutingHandler
        {
            Responses =
            {
                ["GetCurrentPeriodUsage"] =
                    """{"billingCycleStart":"1787153574780","billingCycleEnd":"1787153574780","displayThreshold":100}""",
                ["GetPlanInfo"] = """{"planInfo":{"planName":"Business","price":"Custom","billingCycleEnd":"1788220800000"}}""",
                ["GetHardLimit"] = """{"hardLimit":2147483647,"perUserMonthlyLimitDollars":100}""",
            },
        };
        handler.SetEvents(totalEvents, pages);
        return handler;
    }

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private int _totalEvents;
        private string[] _pages = [];

        public Dictionary<string, string> Responses { get; } = new(StringComparer.Ordinal);
        public Func<HttpRequestMessage, HttpResponseMessage>? Fallback { get; set; }
        public int RequestCount { get; private set; }
        public int FullPageRequests { get; private set; }
        public List<string> Methods { get; } = [];
        public List<string> Paths { get; } = [];
        public List<string> Hosts { get; } = [];
        public AuthenticationHeaderValue? Authorization { get; private set; }

        public void SetEvents(int totalEvents, string[] pages)
        {
            _totalEvents = totalEvents;
            _pages = pages;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            Authorization = request.Headers.Authorization;

            Uri uri = request.RequestUri!;
            Hosts.Add(uri.Host);
            Paths.Add(uri.AbsolutePath);
            string method = uri.Segments[^1];
            Methods.Add(method);

            string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);

            if (method == "GetFilteredUsageEvents")
            {
                bool isCountProbe = body.Contains("\"pageSize\":1,", StringComparison.Ordinal)
                    || body.EndsWith("\"pageSize\":1}", StringComparison.Ordinal);
                if (isCountProbe)
                {
                    return Json($$"""{"totalUsageEventsCount":{{_totalEvents}},"usageEventsDisplay":[]}""");
                }

                FullPageRequests++;
                int page = ExtractPage(body);
                string events = page >= 1 && page <= _pages.Length ? _pages[page - 1] : string.Empty;
                return Json($$"""{"totalUsageEventsCount":{{_totalEvents}},"usageEventsDisplay":[{{events}}]}""");
            }

            if (Responses.TryGetValue(method, out string? response))
            {
                return Json(response);
            }

            return Fallback?.Invoke(request) ?? Json("{}");
        }

        private static int ExtractPage(string body)
        {
            int index = body.IndexOf("\"page\":", StringComparison.Ordinal);
            if (index < 0)
            {
                return 1;
            }

            string tail = body[(index + 7)..];
            int end = tail.IndexOfAny([',', '}']);
            return int.TryParse(end < 0 ? tail : tail[..end], out int page) ? page : 1;
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
    }
}
