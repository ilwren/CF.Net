using CFNet.Configuration;
using CFNet.Http;

namespace CFNet.UnitTests.Testing;

/// <summary>
/// Wires a <see cref="CloudflareClient"/> to a <see cref="StubHttpMessageHandler"/> so tests can queue
/// canned Cloudflare responses and assert on exactly what the library put on the wire.
/// </summary>
/// <example>
/// <code>
/// using var harness = CloudflareHarness.Create();
/// harness.RespondWithResult("{\"id\":\"zone-1\",\"name\":\"example.com\"}");
///
/// var zone = await harness.Client.Zones.GetAsync("zone-1");
///
/// Assert.Equal("example.com", zone!.Name);
/// Assert.Equal("/client/v4/zones/zone-1", harness.LastRequest.Path);
/// </code>
/// </example>
public sealed class CloudflareHarness : IDisposable
{
    private readonly HttpClient _httpClient;

    private CloudflareHarness(StubHttpMessageHandler handler, CloudflareClientOptions options)
    {
        Handler = handler;
        Options = options;
        _httpClient = new HttpClient(handler);
        Transport = new CloudflareTransport(_httpClient, options)
        {
            DelayOverride = (delay, _) =>
            {
                Delays.Add(delay);
                return Task.CompletedTask;
            },
        };

        Client = new CloudflareClient(Transport);
    }

    /// <summary>The stubbed handler; queue responses on it.</summary>
    public StubHttpMessageHandler Handler { get; }

    /// <summary>The options the client was built with.</summary>
    public CloudflareClientOptions Options { get; }

    /// <summary>The transport under test.</summary>
    public CloudflareTransport Transport { get; }

    /// <summary>The client under test.</summary>
    public CloudflareClient Client { get; }

    /// <summary>Every request the client has sent.</summary>
    public IReadOnlyList<RecordedRequest> Requests => Handler.Requests;

    /// <summary>The most recent request.</summary>
    public RecordedRequest LastRequest => Handler.LastRequest;

    /// <summary>Number of requests sent so far.</summary>
    public int RequestCount => Handler.RequestCount;

    /// <summary>
    /// Back-off delays the transport asked for between retries. Recorded instead of awaited so retry
    /// tests finish instantly.
    /// </summary>
    public List<TimeSpan> Delays { get; } = new();

    /// <summary>Account id used by the default harness options.</summary>
    public const string TestAccountId = "acct-1234";

    /// <summary>Zone id used by the default harness options.</summary>
    public const string TestZoneId = "zone-5678";

    /// <summary>API token used by the default harness options.</summary>
    public const string TestApiToken = "v4-test-token-abc123";

    /// <summary>
    /// Creates a harness with API-token credentials, retries disabled and retry delays short-circuited.
    /// </summary>
    /// <param name="configure">Optional tweaks to the default options.</param>
    public static CloudflareHarness Create(Action<CloudflareClientOptions>? configure = null)
    {
        var options = new CloudflareClientOptions
        {
            ApiToken = TestApiToken,
            AccountId = TestAccountId,
            ZoneId = TestZoneId,
            MaxRetries = 0,
            RetryBaseDelay = TimeSpan.Zero,
            UseRetryJitter = false,
        };

        configure?.Invoke(options);

        // Not validated here on purpose: the transport validates at send time, and one test needs to
        // build a credential-less harness to prove that.
        return new CloudflareHarness(new StubHttpMessageHandler(), options);
    }

    /// <summary>Creates a harness whose responses are computed per request (needed for retry tests).</summary>
    /// <param name="responder">Receives the request and its zero-based sequence number.</param>
    /// <param name="configure">Optional tweaks to the default options.</param>
    public static CloudflareHarness CreateWithResponder(
        Func<HttpRequestMessage, int, HttpResponseMessage> responder,
        Action<CloudflareClientOptions>? configure = null)
    {
        var options = new CloudflareClientOptions
        {
            ApiToken = TestApiToken,
            AccountId = TestAccountId,
            ZoneId = TestZoneId,
            MaxRetries = 2,
            RetryBaseDelay = TimeSpan.Zero,
            UseRetryJitter = false,
        };

        configure?.Invoke(options);

        return new CloudflareHarness(new StubHttpMessageHandler(responder), options);
    }

    /// <summary>Creates a harness authenticated with a Global API key instead of a token.</summary>
    /// <param name="configure">Optional tweaks.</param>
    public static CloudflareHarness CreateWithGlobalKey(Action<CloudflareClientOptions>? configure = null) =>
        Create(options =>
        {
            options.ApiToken = null;
            options.GlobalApiKey = "global-key-1234";
            options.ApiEmail = "owner@example.com";
            configure?.Invoke(options);
        });

    /// <summary>Queues a JSON response.</summary>
    public CloudflareHarness Respond(string json, System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK, IDictionary<string, string>? headers = null)
    {
        Handler.Respond(json, status, headers);
        return this;
    }

    /// <summary>Queues a successful envelope around a raw <c>result</c> payload.</summary>
    public CloudflareHarness RespondWithResult(string resultJson)
    {
        Handler.RespondWithResult(resultJson);
        return this;
    }

    /// <summary>Queues a raw, non-envelope response.</summary>
    public CloudflareHarness RespondRaw(byte[] body, string contentType, System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK, IDictionary<string, string>? headers = null)
    {
        Handler.RespondRaw(body, contentType, status, headers);
        return this;
    }

    /// <summary>Queues the standard Cloudflare error envelope.</summary>
    /// <param name="status">HTTP status.</param>
    /// <param name="code">Cloudflare error code.</param>
    /// <param name="message">Error message.</param>
    public CloudflareHarness RespondWithError(
        System.Net.HttpStatusCode status,
        int code,
        string message)
    {
        Handler.Respond(
            $"{{\"success\":false,\"errors\":[{{\"code\":{code},\"message\":\"{message}\"}}],\"messages\":[],\"result\":null}}",
            status);
        return this;
    }

    /// <summary>
    /// Queues a paged envelope whose <c>result_info</c> reports one more page than is being returned,
    /// which is what drives <c>ListAllAsync</c> to continue.
    /// </summary>
    /// <param name="resultJson">Array JSON for <c>result</c>.</param>
    /// <param name="page">Page number this response represents.</param>
    /// <param name="totalPages">Total pages reported.</param>
    /// <param name="count">Items in this page.</param>
    /// <param name="totalCount">Total items across all pages.</param>
    public CloudflareHarness RespondWithPage(string resultJson, int page, int totalPages, int count, int totalCount)
    {
        Handler.Respond(string.Concat(
            "{\"success\":true,\"errors\":[],\"messages\":[],\"result\":", resultJson,
            ",\"result_info\":{\"page\":", page.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ",\"per_page\":", count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ",\"total_pages\":", totalPages.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ",\"count\":", count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ",\"total_count\":", totalCount.ToString(System.Globalization.CultureInfo.InvariantCulture), "}}"));
        return this;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Transport.Dispose();
        _httpClient.Dispose();
        Handler.Dispose();
    }
}
