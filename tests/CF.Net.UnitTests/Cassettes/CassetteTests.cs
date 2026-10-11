using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CFNet;
using CFNet.Models.Common;
using CFNet.Models.D1;
using CFNet.Models.Dns;
using CFNet.Models.Kv;
using CFNet.Models.Pages;
using CFNet.Models.R2;
using CFNet.Models.Workers;
using CFNet.Models.Zones;
using CFNet.Serialization;
using CFNet.TestKit;
using CFNet.UnitTests.Testing;
using Xunit;

namespace CFNet.UnitTests.Cassettes;

/// <summary>
/// Covers the record/replay bridge between the two suites: the integration tests capture real
/// Cloudflare traffic into <c>tests/cassettes/*.json</c>, and these tests prove a cassette can be
/// played back with no credentials and no network, byte for byte.
/// </summary>
public class CassetteTests
{
    private const string Token = "v4-super-secret-token";
    private const string ZonesUrl = "https://api.cloudflare.com/client/v4/zones?page=1&per_page=20";
    private const string ZonesPathAndQuery = "/client/v4/zones?page=1&per_page=20";
    private const string ZonesEnvelope =
        """{"success":true,"errors":[],"messages":[],"result_info":{"page":1,"per_page":20,"total_pages":1,"count":1,"total_count":1},"result":[{"id":"z-1","name":"a.test","status":"active","type":"full"}]}""";

    [Fact]
    public async Task Record_then_replay_round_trips_without_a_socket()
    {
        using var stub = new StubHttpMessageHandler();
        stub.Respond(ZonesEnvelope);

        var recorded = new Cassette("zones-list", CloudflareDefaults.BaseAddress);
        using (var recordingClient = new HttpClient(new CassetteHandler(recorded, CassetteMode.Record, stub)))
        {
            var response = await recordingClient.GetAsync(new Uri(ZonesUrl), CancellationToken.None);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(ZonesEnvelope, await response.Content.ReadAsStringAsync(CancellationToken.None));
        }

        var exchange = Assert.Single(recorded.Exchanges);
        Assert.Equal("GET", exchange.Method);
        Assert.Equal(ZonesPathAndQuery, exchange.PathAndQuery);
        Assert.Equal(ZonesEnvelope, exchange.ResponseBody);
        Assert.Equal("GET /client/v4/zones?page=1&per_page=20", exchange.MatchKey);

        // Reload from disk so the test exercises serialisation, not just the in-memory list.
        var path = TempCassettePath("round-trip");
        try
        {
            recorded.Save(path);
            var reloaded = Cassette.Load(path);

            using var replayClient = new HttpClient(CassetteHandler.Replaying(reloaded));
            var replayed = await replayClient.GetAsync(new Uri(ZonesUrl), CancellationToken.None);

            Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
            Assert.Equal(ZonesEnvelope, await replayed.Content.ReadAsStringAsync(CancellationToken.None));
            Assert.Equal("application/json; charset=utf-8", replayed.Content.Headers.ContentType?.ToString());
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task Recording_redacts_credential_headers_before_they_reach_disk()
    {
        using var stub = new StubHttpMessageHandler();
        stub.Respond(ZonesEnvelope);

        var cassette = new Cassette("redaction", CloudflareDefaults.BaseAddress);
        var path = TempCassettePath("redaction");

        try
        {
            using (var client = new HttpClient(new CassetteHandler(cassette, CassetteMode.Record, stub)))
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, ZonesUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
                request.Headers.Add("X-Auth-Key", Token);
                request.Headers.Add("X-Auth-Email", "someone@example.test");

                await client.SendAsync(request, CancellationToken.None);
            }

            cassette.Save(path);

            var exchange = Assert.Single(cassette.Exchanges);
            Assert.Equal(CassetteRedaction.Redacted, exchange.RequestHeaders!["Authorization"]);
            Assert.Equal(CassetteRedaction.Redacted, exchange.RequestHeaders!["X-Auth-Key"]);
            Assert.Equal(CassetteRedaction.Redacted, exchange.RequestHeaders!["X-Auth-Email"]);

            // The token must not survive anywhere in the file.
            var onDisk = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain(Token, onDisk, StringComparison.Ordinal);
            Assert.DoesNotContain("someone@example.test", onDisk, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task Replay_refuses_to_reach_the_network_when_an_entry_is_missing()
    {
        var cassette = new Cassette("sparse", CloudflareDefaults.BaseAddress);

        // Driven through HttpMessageInvoker rather than HttpClient on purpose: HttpClient wraps
        // anything a handler throws in an HttpRequestException, which would hide the miss. The
        // handler's own inner handler throws, so a miss cannot silently become a real API call.
        using var invoker = new HttpMessageInvoker(CassetteHandler.Replaying(cassette));
        using var request = new HttpRequestMessage(HttpMethod.Get, ZonesUrl);

        var exception = await Assert.ThrowsAsync<CassetteMissException>(
            () => invoker.SendAsync(request, CancellationToken.None));

        Assert.Equal("GET " + ZonesPathAndQuery, exception.MatchKey);
        Assert.Equal("sparse", exception.CassetteName);
    }

    [Fact]
    public async Task Binary_payloads_round_trip_through_base64()
    {
        var payload = new byte[256];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)i;   // includes 0x00, so this is not valid UTF-8 text
        }

        using var stub = new StubHttpMessageHandler();
        stub.RespondRaw(payload, "application/octet-stream");

        var cassette = new Cassette("binary", CloudflareDefaults.BaseAddress);
        using (var client = new HttpClient(new CassetteHandler(cassette, CassetteMode.Record, stub)))
        {
            await client.GetAsync(new Uri(ZonesUrl), CancellationToken.None);
        }

        var exchange = Assert.Single(cassette.Exchanges);
        Assert.Null(exchange.ResponseBody);
        Assert.NotNull(exchange.ResponseBodyBase64);
        Assert.Equal(payload, exchange.ResponseBytes);

        using var replayClient = new HttpClient(CassetteHandler.Replaying(cassette));
        var replayed = await replayClient.GetAsync(new Uri(ZonesUrl), CancellationToken.None);

        Assert.Equal(payload, await replayed.Content.ReadAsByteArrayAsync(CancellationToken.None));
        Assert.Equal("application/octet-stream", replayed.Content.Headers.ContentType?.ToString());
    }

    [Fact]
    public void Saving_the_same_cassette_twice_produces_identical_bytes()
    {
        // A cassette is committed to git, so a re-recording that captured the same traffic must not
        // show up as a diff.
        var cassette = new Cassette("stable", CloudflareDefaults.BaseAddress);
        cassette.Exchanges.Add(new RecordedExchange
        {
            Method = "GET",
            PathAndQuery = "/client/v4/zones",
            StatusCode = 200,
            ResponseContentType = "application/json",
            ResponseBody = ZonesEnvelope,
            RecordedAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero),
        });

        var first = TempCassettePath("stable-a");
        var second = TempCassettePath("stable-b");

        try
        {
            cassette.Save(first);
            cassette.Save(second);

            var left = File.ReadAllText(first);
            Assert.Equal(left, File.ReadAllText(second));
            Assert.DoesNotContain("\r", left, StringComparison.Ordinal);
            Assert.Contains("\"pathAndQuery\": \"/client/v4/zones\"", left, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(first);
            TryDelete(second);
        }
    }

    [Fact]
    public void ParseMode_understands_the_switches_the_workflow_sets()
    {
        Assert.Equal(CassetteMode.Passthrough, CassetteHandler.ParseMode(null));
        Assert.Equal(CassetteMode.Passthrough, CassetteHandler.ParseMode("  "));
        Assert.Equal(CassetteMode.Passthrough, CassetteHandler.ParseMode("no"));
        Assert.Equal(CassetteMode.Record, CassetteHandler.ParseMode("true"));
        Assert.Equal(CassetteMode.Record, CassetteHandler.ParseMode("RECORD"));
        Assert.Equal(CassetteMode.Record, CassetteHandler.ParseMode("1"));
        Assert.Equal(CassetteMode.Replay, CassetteHandler.ParseMode("replay"));
    }

    [Fact]
    public void SplitBody_chooses_text_for_text_and_base64_for_anything_else()
    {
        CassetteRedaction.SplitBody(Array.Empty<byte>(), out var emptyText, out var emptyBase64);
        Assert.Null(emptyText);
        Assert.Null(emptyBase64);

        CassetteRedaction.SplitBody(new byte[] { 0xEF, 0xBB, 0xBF, 0x00 }, out var nulText, out var nulBase64);
        Assert.Null(nulText);
        Assert.NotNull(nulBase64);

        CassetteRedaction.SplitBody(new byte[] { 0xC3, 0x28 }, out var invalidText, out var invalidBase64);
        Assert.Null(invalidText);
        Assert.NotNull(invalidBase64);
    }

    /// <summary>
    /// The contract test that only becomes meaningful once somebody has recorded against the real
    /// API: every committed cassette is bound into the strongly typed models, so a Cloudflare payload
    /// change the library no longer understands fails here, offline, in CI.
    /// </summary>
    [Fact]
    public void Committed_cassettes_bind_to_the_typed_models()
    {
        var paths = RepoPaths.DiscoverCassettes();
        if (paths.Count == 0)
        {
            // Nothing recorded yet. Run the integration suite with
            // CLOUDFLARE_IT_CASSETTE=<name> CLOUDFLARE_IT_RECORD=true to create one.
            return;
        }

        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);
            var cassette = Cassette.Load(path);

            Assert.NotEmpty(cassette.Exchanges);

            foreach (var exchange in cassette.Exchanges)
            {
                AssertNoCredentials(exchange, name);
                AssertBinds(exchange, name);
            }
        }
    }

    private static void AssertNoCredentials(RecordedExchange exchange, string cassetteName)
    {
        if (exchange.RequestHeaders is null)
        {
            return;
        }

        foreach (var header in exchange.RequestHeaders)
        {
            Assert.False(
                CassetteRedaction.IsSensitive(header.Key) && header.Value != CassetteRedaction.Redacted,
                $"{cassetteName}: header '{header.Key}' was recorded with a real value.");
        }
    }

    private static void AssertBinds(RecordedExchange exchange, string cassetteName)
    {
        if (exchange.StatusCode is < 200 or >= 300 || string.IsNullOrWhiteSpace(exchange.ResponseBody))
        {
            return;
        }

        using var document = JsonDocument.Parse(exchange.ResponseBody!);
        var root = document.RootElement;

        // Every v4 envelope carries these, whatever the endpoint.
        Assert.True(root.TryGetProperty("success", out _), $"{cassetteName}: {exchange.PathAndQuery} has no 'success'.");
        Assert.True(root.TryGetProperty("errors", out _), $"{cassetteName}: {exchange.PathAndQuery} has no 'errors'.");

        if (!root.TryGetProperty("result", out var result) || result.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        var path = exchange.PathAndQuery?.Split('?')[0] ?? string.Empty;
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var isArray = result.ValueKind == JsonValueKind.Array;

        switch (path)
        {
            case "/client/v4/user":
                Bind<CloudflareUser>(result, cassetteName, path);
                return;
            case "/client/v4/accounts":
                Bind<List<CloudflareAccount>>(result, cassetteName, path);
                return;
            case "/client/v4/zones":
                Bind<List<Zone>>(result, cassetteName, path);
                return;
            default:
                break;
        }

        // Sub-resources hang off an account or a zone, so match on the tail of the path.
        if (EndsWith(segments, "dns_records") && isArray)
        {
            Bind<List<DnsRecord>>(result, cassetteName, path);
        }
        else if (EndsWith(segments, "workers", "scripts") && isArray)
        {
            Bind<List<WorkerScript>>(result, cassetteName, path);
        }
        else if (IsSingleScript(segments) && !isArray)
        {
            Bind<WorkerScript>(result, cassetteName, path);
        }
        else if (EndsWith(segments, "pages", "projects") && isArray)
        {
            Bind<List<PagesProject>>(result, cassetteName, path);
        }
        else if (EndsWith(segments, "d1", "database") && isArray)
        {
            Bind<List<D1Database>>(result, cassetteName, path);
        }
        else if (EndsWith(segments, "storage", "kv", "namespaces") && isArray)
        {
            Bind<List<KvNamespace>>(result, cassetteName, path);
        }
        else if (EndsWith(segments, "r2", "buckets") && !isArray)
        {
            // R2 wraps its list: { "buckets": [...] }.
            Bind<R2BucketListResult>(result, cassetteName, path);
        }
    }

    /// <summary>True when the path ends with exactly these segments.</summary>
    private static bool EndsWith(string[] segments, params string[] expected)
    {
        if (segments.Length < expected.Length)
        {
            return false;
        }

        var offset = segments.Length - expected.Length;
        for (var i = 0; i < expected.Length; i++)
        {
            if (!string.Equals(segments[offset + i], expected[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True for <c>.../workers/scripts/{name}</c>, and false for the list endpoint and for the
    /// deeper sub-resources such as <c>.../{name}/content</c> or <c>.../{name}/deployments</c>.
    /// </summary>
    private static bool IsSingleScript(string[] segments) =>
        segments.Length >= 3 &&
        string.Equals(segments[^3], "workers", StringComparison.Ordinal) &&
        string.Equals(segments[^2], "scripts", StringComparison.Ordinal);

    private static void Bind<T>(JsonElement result, string cassetteName, string path)
        where T : class
    {
        var model = result.Deserialize<T>(CloudflareJson.Default);
        Assert.False(model is null, $"{cassetteName}: '{path}' did not bind to {typeof(T).Name}.");
    }

    private static string TempCassettePath(string name)
    {
        var directory = Path.Combine(Path.GetTempPath(), "cfnet-cassettes");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{name}-{Guid.NewGuid():N}.json");
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover temp file is not worth failing a test over.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// A replay has to rebuild the URLs the recording contains, and the account and zone ids are part of
/// them. Neither is a secret, so recovering both from the cassette is what makes an offline replay
/// zero-configuration: no credentials, and no ids to export either.
/// </summary>
public class CassetteContextTests
{
    private const string Account = "023e105f4ecef8ad9ca31a8372d0c353";
    private const string Zone = "8d0c8239f88f98a8cb82ec7bb29b8556";
    private const string Record = "372e67954025e0ba6aaa6d586b9e0b59";

    private static Cassette CassetteWith(params string[] paths)
    {
        var cassette = new Cassette("snapshot", CloudflareDefaults.BaseAddress);
        foreach (var path in paths)
        {
            cassette.Exchanges.Add(new RecordedExchange
            {
                Name = path,
                Method = "GET",
                PathAndQuery = path,
                StatusCode = 200,
                ResponseContentType = "application/json",
                ResponseBody = "{\"success\":true,\"errors\":[],\"messages\":[],\"result\":null}",
            });
        }

        return cassette;
    }

    [Fact]
    public void Account_and_zone_ids_come_out_of_the_recorded_paths()
    {
        var cassette = CassetteWith(
            "/client/v4/user/tokens/verify",
            "/client/v4/zones?page=1&per_page=20",
            $"/client/v4/zones/{Zone}/dns_records/{Record}",
            $"/client/v4/accounts/{Account}/workers/scripts",
            $"/client/v4/accounts/{Account}/storage/kv/namespaces");

        Assert.Equal(Account, CassetteContext.DeriveAccountId(cassette));
        Assert.Equal(Zone, CassetteContext.DeriveZoneId(cassette));
    }

    [Fact]
    public void A_list_endpoint_carries_no_id_and_is_not_mistaken_for_one()
    {
        var cassette = CassetteWith(
            "/client/v4/zones?page=1&per_page=20",
            "/client/v4/accounts?page=1&per_page=20");

        Assert.Null(CassetteContext.DeriveZoneId(cassette));
        Assert.Null(CassetteContext.DeriveAccountId(cassette));
    }

    [Fact]
    public void Derive_returns_both_at_once()
    {
        var derived = CassetteContext.Derive(CassetteWith(
            $"/client/v4/accounts/{Account}/r2/buckets",
            $"/client/v4/zones/{Zone}"));

        Assert.Equal(Account, derived.AccountId);
        Assert.Equal(Zone, derived.ZoneId);
    }

    [Fact]
    public void Nothing_is_invented_when_there_is_nothing_to_read()
    {
        Assert.Null(CassetteContext.DeriveAccountId(null));
        Assert.Null(CassetteContext.DeriveZoneId(null));
        Assert.Null(CassetteContext.DeriveAccountId(new Cassette("empty")));
        Assert.Null(CassetteContext.DeriveZoneId(CassetteWith("/client/v4/user")));
    }
}
