using System.Text;
using System.Text.Json;
using CFNet.Exceptions;
using CFNet.Http;
using CFNet.Models.Workers;
using CFNet.UnitTests.Testing;
using Xunit;

namespace CFNet.UnitTests.Services;

/// <summary>
/// The static asset upload flow. Three things here are easy to get wrong and are pinned closely: the manifest's
/// hash is the key that has to agree in three places at once, the upload calls are authorized by a JWT rather
/// than by the API credentials the transport is configured with, and the multipart call sends base64 text in
/// parts named by that hash.
/// </summary>
public class WorkersAssetsServiceTests
{
    /// <summary>A hash shaped like wrangler's: 32 hex characters.</summary>
    private const string IndexHash = "0123456789abcdef0123456789abcdef";

    /// <summary>A second hash, for the calls that carry more than one asset.</summary>
    private const string StyleHash = "fedcba9876543210fedcba9876543210";

    /// <summary>A session token that carries both upload hints and a far-future expiry.</summary>
    private static readonly string SessionJwt = Token(
        """{"wrangler_single_asset_uploads":true,"edge_kv_upload_concurrency":7,"exp":4102444800}""");

    /// <summary>A completion token with no hints in it at all.</summary>
    private static readonly string CompletionJwt = Token("""{"sub":"asset-upload"}""");

    /// <summary>The bytes of a tiny asset.</summary>
    private static readonly byte[] IndexBytes = Encoding.UTF8.GetBytes("<h1>cf.net</h1>");

    [Fact]
    public async Task CreateUploadSessionAsync_posts_the_manifest_and_reads_back_the_buckets_and_the_token()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            $$"""{"buckets":[["{{IndexHash}}"]],"jwt":"{{SessionJwt}}"}""");

        var manifest = new WorkersAssetManifest().Add("/index.html", IndexHash, IndexBytes.LongLength);
        var session = await harness.Client.WorkersAssets.CreateUploadSessionAsync("my-worker", manifest);

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/workers/scripts/my-worker/assets-upload-session",
            harness.LastRequest.Path,
            StringComparison.Ordinal);

        // The session call is an ordinary API call: it carries the configured token, not the JWT it is about
        // to be given.
        Assert.Equal("Bearer " + CloudflareHarness.TestApiToken, harness.LastRequest.Header("Authorization"));
        Assert.Equal(
            $$"""{"manifest":{"/index.html":{"hash":"{{IndexHash}}","size":{{IndexBytes.LongLength}}}}}""",
            harness.LastRequest.Body);

        Assert.Equal(SessionJwt, session.Jwt);
        Assert.NotNull(session.Buckets);
        Assert.Single(session.Buckets!);
        Assert.Equal(new[] { IndexHash }, session.RequestedHashes);
        Assert.Equal(1, session.RequestedCount);
        Assert.False(session.NothingToUpload);
        Assert.True(session.AdditionalData is not { Count: > 0 });
        Assert.Equal("1 hash(es) to upload in 1 bucket(s)", session.ToString());
    }

    [Fact]
    public async Task CreateUploadSessionAsync_sends_every_entry_with_no_field_the_schema_does_not_publish()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"buckets":[],"jwt":"session"}""");

        var manifest = new WorkersAssetManifest("/index.html", IndexHash, 13)
            .Add("/assets/site.css", StyleHash, 0)
            .Add("/blog/hello-world.html", "third-hash", 42);
        var session = await harness.Client.WorkersAssets.CreateUploadSessionAsync("my-worker", manifest);

        Assert.Equal(
            $$"""{"manifest":{"/index.html":{"hash":"{{IndexHash}}","size":13},"/assets/site.css":{"hash":"{{StyleHash}}","size":0},"/blog/hello-world.html":{"hash":"third-hash","size":42}}}""",
            harness.LastRequest.Body);

        // Cloudflare already holds all three, so there is nothing to send - and the session's own token is the
        // completion token the deployment redeems.
        Assert.True(session.NothingToUpload);
        Assert.Empty(session.RequestedHashes);
        Assert.Equal("session", session.Jwt);
        Assert.Equal(3, manifest.Count);
        Assert.True(manifest.Contains("/assets/site.css"));
        Assert.Equal("3 asset(s)", manifest.ToString());
    }

    [Fact]
    public async Task CreateUploadSessionAsync_encodes_a_script_name_and_honours_an_explicit_account()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"buckets":[],"jwt":"session"}""");

        await harness.Client.WorkersAssets.CreateUploadSessionAsync(
            "my-worker/v1",
            new WorkersAssetManifest("/index.html", IndexHash, 1),
            "acct-other");

        Assert.Contains("/accounts/acct-other/workers/scripts/", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Contains("my-worker%2Fv1/assets-upload-session", harness.LastRequest.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateUploadSessionAsync_reports_a_null_result_as_an_api_exception_naming_the_call()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("""{"success":true,"errors":[],"messages":[],"result":null}""");

        var exception = await Assert.ThrowsAsync<CloudflareApiException>(() =>
            harness.Client.WorkersAssets.CreateUploadSessionAsync(
                "my-worker",
                new WorkersAssetManifest("/index.html", IndexHash, 1)));

        // Cloudflare has answered this endpoint with a null result in the past, and wrangler turns that into a
        // clear failure rather than dereferencing it. This does the same, and keeps the ray for a support ticket.
        Assert.Contains("without a result payload", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateUploadSessionAsync_allows_an_empty_manifest()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"buckets":[],"jwt":"session"}""");

        var session = await harness.Client.WorkersAssets.CreateUploadSessionAsync(
            "my-worker",
            new WorkersAssetManifest());

        Assert.Equal("""{"manifest":{}}""", harness.LastRequest.Body);
        Assert.True(session.NothingToUpload);
        Assert.Equal("0 hash(es) to upload in 0 bucket(s)", session.ToString());
    }

    [Fact]
    public async Task CreateUploadSessionAsync_refuses_entries_it_cannot_send()
    {
        using var harness = CloudflareHarness.Create();

        var manifest = new WorkersAssetManifest()
            .Add("/no-hash.html", "   ", 5)
            .Add("/negative.html", "hash-2", -1)
            .Add("/no-size.html", "hash-3", 0);
        manifest.Entries["/no-size.html"].Size = null;

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(() =>
            harness.Client.WorkersAssets.CreateUploadSessionAsync("my-worker", manifest));

        Assert.Contains("'/no-hash.html' has no hash", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'/negative.html' has a negative size (-1)", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'/no-size.html' has no size", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public void Manifest_replaces_a_repeated_path_and_hands_out_a_copy_of_its_entries()
    {
        var manifest = new WorkersAssetManifest()
            .Add("/index.html", "first", 1)
            .Add("/index.html", "second", 2);

        Assert.Equal(1, manifest.Count);
        Assert.Equal("second", manifest.Entries["/index.html"].Hash);
        Assert.Equal(2, manifest.Entries["/index.html"].Size);

        var copy = manifest.AsDictionary();
        copy.Remove("/index.html");
        Assert.True(manifest.Contains("/index.html"));
        Assert.True(manifest.Remove("/index.html"));
        Assert.False(manifest.Contains("/index.html"));
        Assert.False(manifest.Remove("/index.html"));
        Assert.False(manifest.Contains(string.Empty));
    }

    [Theory]
    [InlineData("index.html", "/index.html")]
    [InlineData("blog/hello-world.html", "/blog/hello-world.html")]
    [InlineData("blog\\hello-world.html", "/blog/hello-world.html")]
    [InlineData("/already-rooted.html", "/already-rooted.html")]
    [InlineData("./relative.html", "/./relative.html")]
    public void NormalizePath_builds_the_root_relative_key_Cloudflare_matches_requests_against(
        string relative,
        string expected)
    {
        Assert.Equal(expected, WorkersAssetManifest.NormalizePath(relative));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("\\")]
    [InlineData("///")]
    public void NormalizePath_refuses_a_path_that_names_no_file(string relative)
    {
        var exception = Assert.Throws<ArgumentException>(() => WorkersAssetManifest.NormalizePath(relative));

        Assert.Contains("must name a file", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizePath_refuses_a_path_that_is_not_there()
    {
        // ThrowIfNullOrEmpty separates the two: nothing at all is an ArgumentNullException, an empty string is
        // the ArgumentException it derives from.
        Assert.Throws<ArgumentException>(() => WorkersAssetManifest.NormalizePath(string.Empty));
        Assert.Throws<ArgumentNullException>(() => WorkersAssetManifest.NormalizePath(null!));
    }

    [Fact]
    public void ManifestEntry_reports_the_values_it_carries()
    {
        var entry = new WorkersAssetManifestEntry(IndexHash, 13);

        Assert.Equal(IndexHash, entry.Hash);
        Assert.Equal(13, entry.Size);
        Assert.True(entry.AdditionalData is not { Count: > 0 });
        Assert.Equal($"{IndexHash} (13 bytes)", entry.ToString());
        Assert.Equal("? (? bytes)", new WorkersAssetManifestEntry().ToString());
    }

    [Fact]
    public void Session_flattens_buckets_in_order_and_skips_hashes_that_are_not_there()
    {
        var session = JsonSerializer.Deserialize<WorkersAssetsUploadSession>(
            $$"""
              {"buckets":[["{{IndexHash}}","",null],[],["{{StyleHash}}"]],"jwt":"{{SessionJwt}}","surprise":1}
              """)!;

        Assert.Equal(new[] { IndexHash, StyleHash }, session.RequestedHashes);
        Assert.Equal(2, session.RequestedCount);
        Assert.False(session.NothingToUpload);
        Assert.Equal(3, session.Buckets!.Count);
        Assert.NotNull(session.AdditionalData);
        Assert.Equal(new[] { "surprise" }, session.AdditionalData!.Keys);
    }

    [Fact]
    public void Session_reads_the_upload_hints_out_of_its_token()
    {
        var session = new WorkersAssetsUploadSession { Jwt = SessionJwt };

        var token = session.ReadToken();

        Assert.NotNull(token);
        Assert.True(token!.SingleAssetUploads);
        Assert.Equal(7, token.UploadConcurrency);
        Assert.Equal(7, token.EffectiveUploadConcurrency);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(4102444800), token.ExpiresAt);
        Assert.False(token.IsExpired);
        Assert.Equal(3, token.Claims.Count);
        Assert.Equal(
            "3 claim(s), expires " + DateTimeOffset.FromUnixTimeSeconds(4102444800).ToString("O"),
            token.ToString());
    }

    [Fact]
    public void Token_falls_back_to_the_default_concurrency_and_reads_a_false_hint()
    {
        var token = WorkersAssetsToken.TryRead(
            Token("""{"wrangler_single_asset_uploads":false,"edge_kv_upload_concurrency":0,"exp":1}"""))!;

        Assert.NotNull(token);
        Assert.False(token.SingleAssetUploads);
        Assert.Equal(0, token.UploadConcurrency);

        // wrangler only honours a positive number, so a zero means "use the default".
        Assert.Equal(WorkersAssetsToken.DefaultUploadConcurrency, token.EffectiveUploadConcurrency);
        Assert.Equal(50, WorkersAssetsToken.DefaultUploadConcurrency);
        Assert.True(token.IsExpired);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(1), token.ExpiresAt);
    }

    [Fact]
    public void Token_keeps_claims_it_does_not_understand_and_reports_what_it_reads()
    {
        var token = WorkersAssetsToken.TryRead(
            Token("""{"exp":"not-a-number","edge_kv_upload_concurrency":"seven","iss":"cloudflare"}"""))!;

        Assert.NotNull(token);
        Assert.Null(token.ExpiresAt);
        Assert.Null(token.UploadConcurrency);
        Assert.Null(token.SingleAssetUploads);
        Assert.Equal(WorkersAssetsToken.DefaultUploadConcurrency, token.EffectiveUploadConcurrency);
        Assert.False(token.IsExpired);
        Assert.Equal("3 claim(s)", token.ToString());
        Assert.Equal(JsonValueKind.String, token.Claims["iss"].ValueKind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-jwt")]
    [InlineData("only.two")]
    [InlineData("a.!!!not-base64!!!.c")]
    [InlineData("a.YQ.c")]
    public void Token_TryRead_returns_null_for_anything_it_cannot_read(string? jwt)
    {
        Assert.Null(WorkersAssetsToken.TryRead(jwt));
    }

    [Fact]
    public void Token_TryRead_returns_null_when_the_payload_is_not_an_object()
    {
        Assert.Null(WorkersAssetsToken.TryRead(Token("[1,2,3]")));
        Assert.Null(WorkersAssetsToken.TryRead(Token("42")));
        Assert.Null(new WorkersAssetUpload().ReadToken());
    }

    [Fact]
    public async Task UploadAssetAsync_sends_the_bytes_raw_with_the_jwt_and_no_api_credentials()
    {
        using var harness = CloudflareHarness.CreateWithGlobalKey();
        harness.RespondWithResult($$"""{"jwt":"{{CompletionJwt}}"}""");

        var upload = await harness.Client.WorkersAssets.UploadAssetAsync(
            IndexHash,
            IndexBytes,
            SessionJwt,
            WorkersAssetContentTypes.Html);

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.EndsWith(
            $"/accounts/acct-1234/workers/assets/upload/{IndexHash}",
            harness.LastRequest.Path,
            StringComparison.Ordinal);

        // The session JWT is the only credential on the call: neither the API token nor the global key pair
        // goes with it, which is what makes these endpoints usable without API credentials at all.
        Assert.Equal("Bearer " + SessionJwt, harness.LastRequest.Header("Authorization"));
        Assert.Null(harness.LastRequest.Header("X-Auth-Key"));
        Assert.Null(harness.LastRequest.Header("X-Auth-Email"));

        Assert.Equal("<h1>cf.net</h1>", harness.LastRequest.Body);
        Assert.Equal("text/html; charset=utf-8", harness.LastRequest.ContentType);

        Assert.Equal(CompletionJwt, upload.Jwt);
        Assert.Equal("completion token", upload.ToString());
        Assert.Equal("no completion token", new WorkersAssetUpload().ToString());
        Assert.NotNull(upload.ReadToken());
        Assert.Equal("asset-upload", upload.ReadToken()!.Claims["sub"].GetString());
    }

    [Fact]
    public async Task UploadAssetAsync_sends_the_no_content_type_sentinel_when_it_is_given_none()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"jwt":"completion"}""");

        await harness.Client.WorkersAssets.UploadAssetAsync(IndexHash, IndexBytes, SessionJwt);

        Assert.Equal(WorkersAssetContentTypes.Unknown, harness.LastRequest.ContentType);
        Assert.Equal("application/null", WorkersAssetContentTypes.Unknown);
        Assert.Equal(string.Empty, harness.LastRequest.Query);
    }

    [Fact]
    public async Task UploadAssetAsync_works_for_a_client_that_configured_no_credentials()
    {
        using var harness = CloudflareHarness.Create(options => options.ApiToken = null);
        harness.RespondWithResult("""{"jwt":"completion"}""");

        var upload = await harness.Client.WorkersAssets.UploadAssetAsync(IndexHash, IndexBytes, SessionJwt);

        Assert.Equal("completion", upload.Jwt);
        Assert.Equal("Bearer " + SessionJwt, harness.LastRequest.Header("Authorization"));
    }

    [Fact]
    public async Task UploadAssetAsync_encodes_a_hash_that_is_not_a_url_segment()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"jwt":"completion"}""");

        await harness.Client.WorkersAssets.UploadAssetAsync("hash/with-slash", IndexBytes, SessionJwt);

        Assert.EndsWith(
            "/workers/assets/upload/hash%2Fwith-slash",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UploadAssetAsync_refuses_a_hash_or_a_token_it_cannot_send(string? value)
    {
        using var harness = CloudflareHarness.Create();

        var hashException = await Assert.ThrowsAsync<CloudflareConfigurationException>(() =>
            harness.Client.WorkersAssets.UploadAssetAsync(value!, IndexBytes, SessionJwt));
        Assert.Contains("'hash' must not be null or empty", hashException.Message, StringComparison.Ordinal);

        var jwtException = await Assert.ThrowsAsync<CloudflareConfigurationException>(() =>
            harness.Client.WorkersAssets.UploadAssetAsync(IndexHash, IndexBytes, value!));
        Assert.Contains("'jwt' must not be null or empty", jwtException.Message, StringComparison.Ordinal);

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task UploadAssetsAsync_sends_one_base64_part_per_asset_named_by_its_hash()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult($$"""{"jwt":"{{CompletionJwt}}"}""");

        var upload = await harness.Client.WorkersAssets.UploadAssetsAsync(
            new[]
            {
                new WorkersAssetUploadItem(IndexHash, IndexBytes, WorkersAssetContentTypes.Html),
                WorkersAssetUploadItem.ForText(StyleHash, "body{}"),
            },
            SessionJwt);

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.EndsWith("/accounts/acct-1234/workers/assets/upload", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal("?base64=true", harness.LastRequest.Query);
        Assert.Equal("Bearer " + SessionJwt, harness.LastRequest.Header("Authorization"));
        Assert.StartsWith("multipart/form-data", harness.LastRequest.ContentType, StringComparison.Ordinal);

        var body = harness.LastRequest.Body!;

        // Each part is named - and file-named - by its hash, quoted, because the API matches a part to a
        // manifest entry by that name.
        Assert.Contains($"name=\"{IndexHash}\"; filename=\"{IndexHash}\"", body, StringComparison.Ordinal);
        Assert.Contains($"name=\"{StyleHash}\"; filename=\"{StyleHash}\"", body, StringComparison.Ordinal);
        Assert.Contains(Convert.ToBase64String(IndexBytes), body, StringComparison.Ordinal);
        Assert.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes("body{}")), body, StringComparison.Ordinal);
        Assert.Contains("text/html; charset=utf-8", body, StringComparison.Ordinal);

        // The parts go out in the order they were given.
        Assert.True(body.IndexOf(IndexHash, StringComparison.Ordinal) < body.IndexOf(StyleHash, StringComparison.Ordinal));
        Assert.Equal(CompletionJwt, upload.Jwt);
    }

    [Fact]
    public async Task UploadAssetsAsync_uses_the_sentinel_for_an_asset_with_no_type_and_trims_the_one_it_is_given()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"jwt":"completion"}""");

        await harness.Client.WorkersAssets.UploadAssetsAsync(
            new[]
            {
                new WorkersAssetUploadItem(IndexHash, IndexBytes),
                new WorkersAssetUploadItem(StyleHash, IndexBytes, "  application/json  "),
            },
            SessionJwt);

        var body = harness.LastRequest.Body!;
        Assert.Contains("Content-Type: application/null", body, StringComparison.Ordinal);
        Assert.Contains("Content-Type: application/json", body, StringComparison.Ordinal);
        Assert.DoesNotContain("  application/json  ", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadAssetsAsync_refuses_a_repeated_hash()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(() =>
            harness.Client.WorkersAssets.UploadAssetsAsync(
                new[]
                {
                    new WorkersAssetUploadItem(IndexHash, IndexBytes),
                    new WorkersAssetUploadItem(IndexHash, IndexBytes),
                },
                SessionJwt));

        Assert.Contains(IndexHash, exception.Message, StringComparison.Ordinal);
        Assert.Contains("appears more than once", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);

        // A blank key gets past the item's own constructor, which only refuses null and empty, and is caught
        // here instead: it would otherwise go out as a part named by whitespace.
        var blank = await Assert.ThrowsAsync<CloudflareConfigurationException>(() =>
            harness.Client.WorkersAssets.UploadAssetsAsync(
                new[] { new WorkersAssetUploadItem("   ", IndexBytes) },
                SessionJwt));
        Assert.Contains("'Hash' must not be null or empty", blank.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadAssetsAsync_refuses_an_empty_upload_and_a_null_asset()
    {
        using var harness = CloudflareHarness.Create();

        var empty = await Assert.ThrowsAsync<CloudflareConfigurationException>(() =>
            harness.Client.WorkersAssets.UploadAssetsAsync(Array.Empty<WorkersAssetUploadItem>(), SessionJwt));
        Assert.Contains("At least one asset", empty.Message, StringComparison.Ordinal);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            harness.Client.WorkersAssets.UploadAssetsAsync(
                new WorkersAssetUploadItem[] { null! },
                SessionJwt));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            harness.Client.WorkersAssets.UploadAssetsAsync(null!, SessionJwt));

        await Assert.ThrowsAsync<CloudflareConfigurationException>(() =>
            harness.Client.WorkersAssets.UploadAssetAsync(IndexHash, null!, SessionJwt));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            harness.Client.WorkersAssets.CreateUploadSessionAsync("my-worker", null!));

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public void UploadItem_reports_its_bytes_its_size_and_the_type_it_will_send()
    {
        var item = WorkersAssetUploadItem.ForText(IndexHash, "hello");
        var typed = new WorkersAssetUploadItem(StyleHash, IndexBytes, " text/css ");
        var unknown = new WorkersAssetUploadItem(StyleHash, Array.Empty<byte>());

        Assert.Equal("hello", Encoding.UTF8.GetString(item.Content));
        Assert.Equal(WorkersAssetContentTypes.Text, item.ContentType);
        Assert.Equal(WorkersAssetContentTypes.Text, item.EffectiveContentType);
        Assert.Equal(5, item.Size);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("hello")), item.ContentAsBase64());
        Assert.Equal($"{IndexHash} (5 bytes, text/plain; charset=utf-8)", item.ToString());

        Assert.Equal("text/css", typed.EffectiveContentType);
        Assert.Equal(WorkersAssetContentTypes.Unknown, unknown.EffectiveContentType);
        Assert.Equal(0, unknown.Size);

        Assert.Throws<ArgumentException>(() => new WorkersAssetUploadItem(string.Empty, IndexBytes));
        Assert.Throws<ArgumentNullException>(() => new WorkersAssetUploadItem(IndexHash, null!));
        Assert.Equal("text/plain; charset=utf-8", WorkersAssetContentTypes.Text);
        Assert.Equal("application/javascript; charset=utf-8", WorkersAssetContentTypes.JavaScript);
        Assert.Equal("application/json", WorkersAssetContentTypes.Json);
    }

    [Fact]
    public void Request_WithBearerToken_marks_the_request_so_the_transport_leaves_the_credentials_off()
    {
        var request = CloudflareRequest
            .Post("accounts/acct-1234/workers/assets/upload/hash")
            .WithBearerToken("  session-jwt  ");

        Assert.True(request.UsesCustomAuthentication);
        Assert.Equal("Bearer session-jwt", request.Headers["Authorization"]);

        // An Authorization header set the ordinary way is not enough: the transport adds its own credentials
        // first, and a repeated header is dropped rather than replacing the one already there.
        var headerOnly = CloudflareRequest.Get("ips").WithHeader("Authorization", "Bearer session-jwt");
        Assert.False(headerOnly.UsesCustomAuthentication);

        Assert.Throws<ArgumentException>(() => CloudflareRequest.Get("ips").WithBearerToken(string.Empty));
        Assert.Throws<ArgumentNullException>(() => CloudflareRequest.Get("ips").WithBearerToken(null!));
    }

    /// <summary>Builds a JWT whose payload is the given JSON, with no padding on either segment.</summary>
    /// <param name="payloadJson">The payload to encode.</param>
    /// <returns>A token shaped like the ones Cloudflare's asset endpoints hand out.</returns>
    private static string Token(string payloadJson) =>
        Base64Url(Encoding.UTF8.GetBytes("""{"alg":"none","typ":"JWT"}"""))
        + "."
        + Base64Url(Encoding.UTF8.GetBytes(payloadJson))
        + ".cf-net-signature";

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
