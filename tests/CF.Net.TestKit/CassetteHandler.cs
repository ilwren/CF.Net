using System.Net;
using System.Net.Http.Headers;

namespace CFNet.TestKit;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that can record the Cloudflare traffic passing through it into
/// a <see cref="Cassette"/>, or answer from one without touching the network at all.
/// </summary>
/// <remarks>
/// <para>
/// This is the bridge between the two test suites. The integration project runs against the real API
/// with real credentials and records what came back; the unit project replays those recordings, so
/// its assertions are driven by genuine Cloudflare payloads while still being offline, fast and
/// deterministic.
/// </para>
/// <para>
/// Credentials never reach a cassette: <see cref="CassetteRedaction"/> replaces the value of
/// <c>Authorization</c>, <c>X-Auth-Key</c>, <c>X-Auth-Email</c> and cookie headers with
/// <c>&lt;redacted&gt;</c>. Anything else that lands in a cassette is a normal API payload, so review
/// the diff before committing.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Record (credentials required, one authenticated run)
/// var cassette = new Cassette("zones-list", CloudflareDefaults.BaseAddress);
/// services.AddCloudflareHttpClient(options => options.ApiToken = token,
///     configureClient: builder => builder.AddHttpMessageHandler(() => CassetteHandler.Recording(cassette)));
/// ...
/// cassette.Save(RepoPaths.CassettePath("zones-list"));
///
/// // Replay (no credentials, no network)
/// var recorded = Cassette.Load(RepoPaths.CassettePath("zones-list"));
/// var http = new HttpClient(CassetteHandler.Replaying(recorded));
/// var client = new CloudflareClient(new CloudflareTransport(http, new CloudflareClientOptions { ApiToken = "unused" }));
/// </code>
/// </example>
public sealed class CassetteHandler : DelegatingHandler
{
    private readonly Cassette _cassette;
    private readonly CassetteMode _mode;
    private readonly bool _ownsInnerHandler;
    private readonly List<RecordedExchange> _recorded = new();
    private readonly HashSet<RecordedExchange> _replayed = new(ReferenceEqualityComparer.Instance);
    private readonly object _gate = new();

    /// <summary>
    /// Creates a handler for <paramref name="mode"/>, picking an inner handler that matches: a real
    /// <see cref="HttpClientHandler"/> when the network is allowed, and one that throws when it is not.
    /// </summary>
    /// <param name="cassette">The cassette to read from or write into.</param>
    /// <param name="mode">Record, replay or pass through.</param>
    public CassetteHandler(Cassette cassette, CassetteMode mode)
        : this(cassette, mode, mode == CassetteMode.Replay ? new NoNetworkHandler() : new HttpClientHandler())
    {
        _ownsInnerHandler = true;
    }

    /// <summary>
    /// Creates a handler around an existing inner handler, for use inside an
    /// <c>IHttpClientFactory</c> pipeline where the primary handler is supplied by the factory.
    /// </summary>
    /// <param name="cassette">The cassette to read from or write into.</param>
    /// <param name="mode">Record, replay or pass through.</param>
    /// <param name="innerHandler">The handler that reaches the network.</param>
    public CassetteHandler(Cassette cassette, CassetteMode mode, HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
        _cassette = cassette ?? throw new ArgumentNullException(nameof(cassette));
        _mode = mode;
    }

    /// <summary>Creates a recording handler with a real network handler underneath.</summary>
    /// <param name="cassette">The cassette to append to.</param>
    /// <returns>A handler ready for <c>AddHttpMessageHandler</c>.</returns>
    public static CassetteHandler Recording(Cassette cassette) => new(cassette, CassetteMode.Record);

    /// <summary>Creates a replaying handler that can never reach the network.</summary>
    /// <param name="cassette">The cassette to answer from.</param>
    /// <returns>A handler ready for <c>AddHttpMessageHandler</c>.</returns>
    public static CassetteHandler Replaying(Cassette cassette) => new(cassette, CassetteMode.Replay);

    /// <summary>The cassette being read or written.</summary>
    public Cassette Cassette => _cassette;

    /// <summary>How this handler treats the network.</summary>
    public CassetteMode Mode => _mode;

    /// <summary>
    /// Optional label written into <see cref="RecordedExchange.Name"/>. Set it to the name of the test
    /// doing the call so a cassette reads like a transcript.
    /// </summary>
    public string? Label { get; set; }

    /// <summary>Number of exchanges this handler appended to the cassette.</summary>
    public int RecordedCount
    {
        get
        {
            lock (_gate)
            {
                return _recorded.Count;
            }
        }
    }

    /// <summary>Number of exchanges this handler answered from the cassette.</summary>
    public int ReplayedCount
    {
        get
        {
            lock (_gate)
            {
                return _replayed.Count;
            }
        }
    }

    /// <summary>
    /// Interprets an environment variable as a <see cref="CassetteMode"/>.
    /// </summary>
    /// <param name="value">The raw value; <see langword="null"/> or empty means pass through.</param>
    /// <returns>The mode, defaulting to <see cref="CassetteMode.Passthrough"/>.</returns>
    public static CassetteMode ParseMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return CassetteMode.Passthrough;
        }

        if (string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value.Trim(), "record", StringComparison.OrdinalIgnoreCase) ||
            value.Trim() == "1")
        {
            return CassetteMode.Record;
        }

        if (string.Equals(value.Trim(), "replay", StringComparison.OrdinalIgnoreCase))
        {
            return CassetteMode.Replay;
        }

        return CassetteMode.Passthrough;
    }

    /// <summary>
    /// Disposes the inner handler only when this instance created it.
    /// </summary>
    /// <remarks>
    /// Inside an <c>IHttpClientFactory</c> pipeline the factory owns the primary handler and chains
    /// the extra ones onto it, so disposing what it supplied would break every other client sharing
    /// that pipeline. The handler is also meant to be shared across clients so a whole run lands in
    /// one cassette, which makes an aggressive <see cref="Dispose()"/> doubly wrong.
    /// </remarks>
    /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && _ownsInnerHandler)
        {
            base.Dispose(true);
        }
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_mode == CassetteMode.Replay)
        {
            return Replay(request);
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (_mode == CassetteMode.Record)
        {
            await RecordAsync(request, response, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    private async Task RecordAsync(
        HttpRequestMessage request,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var exchange = new RecordedExchange
        {
            Name = Label,
            Method = request.Method.Method.ToUpperInvariant(),
            PathAndQuery = request.RequestUri?.PathAndQuery,
            RequestHeaders = CassetteRedaction.Copy(request.Headers),
            StatusCode = (int)response.StatusCode,
            ResponseHeaders = CassetteRedaction.Copy(response.Headers),
            RecordedAt = DateTimeOffset.UtcNow,
        };

        if (response.Content is not null)
        {
            // Buffer first: the transport still has to read this body, and ReadAsByteArrayAsync on an
            // unbuffered stream would consume it.
            await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
            exchange.ResponseContentType = response.Content.Headers.ContentType?.ToString();
            CassetteRedaction.SplitBody(
                await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false),
                out var text,
                out var base64);
            exchange.ResponseBody = text;
            exchange.ResponseBodyBase64 = base64;
        }

        if (request.Content is not null)
        {
            // Read after the send: StringContent, ByteArrayContent and MultipartFormDataContent can be
            // read again, a one-shot StreamContent cannot, and that is not worth failing a run over.
            try
            {
                CassetteRedaction.SplitBody(
                    await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false),
                    out var text,
                    out var base64);
                exchange.RequestBody = text;
                exchange.RequestBodyBase64 = base64;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or IOException)
            {
                exchange.RequestBody = null;
                exchange.RequestBodyBase64 = null;
            }
        }

        lock (_gate)
        {
            _recorded.Add(exchange);
            _cassette.Exchanges.Add(exchange);
        }
    }

    private HttpResponseMessage Replay(HttpRequestMessage request)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("A replayed request needs an absolute URI.");
        var key = $"{request.Method.Method.ToUpperInvariant()} {uri.PathAndQuery}";

        RecordedExchange? hit;
        lock (_gate)
        {
            hit = FindUnused(key, uri.AbsolutePath);
        }

        if (hit is null)
        {
            throw new CassetteMissException(key, _cassette.Name);
        }

        var response = new HttpResponseMessage((HttpStatusCode)hit.StatusCode)
        {
            RequestMessage = request,
        };

        if (hit.ResponseHeaders is not null)
        {
            foreach (var header in hit.ResponseHeaders)
            {
                // TryAddWithoutValidation ignores anything that belongs on the content instead.
                response.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        var content = new ByteArrayContent(hit.ResponseBytes);
        if (!string.IsNullOrWhiteSpace(hit.ResponseContentType) &&
            MediaTypeHeaderValue.TryParse(hit.ResponseContentType, out var mediaType))
        {
            content.Headers.ContentType = mediaType;
        }

        response.Content = content;
        return response;
    }

    private RecordedExchange? FindUnused(string exactKey, string pathOnly)
    {
        // First an exact match on method + path + query, then the same call with a different query
        // (pagination, a changed default page size). Each entry is consumed once so a cassette can
        // record the same endpoint being called twice with two different answers.
        foreach (var exchange in _cassette.Exchanges)
        {
            if (!_replayed.Contains(exchange) &&
                string.Equals(exchange.MatchKey, exactKey, StringComparison.Ordinal))
            {
                _replayed.Add(exchange);
                return exchange;
            }
        }

        foreach (var exchange in _cassette.Exchanges)
        {
            if (_replayed.Contains(exchange))
            {
                continue;
            }

            var candidatePath = exchange.PathAndQuery?.Split('?')[0];
            if (string.Equals(candidatePath, pathOnly, StringComparison.Ordinal) &&
                string.Equals(exchange.Method, exactKey.Split(' ')[0], StringComparison.OrdinalIgnoreCase))
            {
                _replayed.Add(exchange);
                return exchange;
            }
        }

        return null;
    }

    /// <summary>
    /// The inner handler used when replaying: it exists so disposing the chain is safe, and it fails
    /// loudly if anything tries to reach Cloudflare while a cassette is supposed to be answering.
    /// </summary>
    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"A cassette replay tried to call {request.RequestUri}. The cassette is missing an entry and the handler refuses to fall back to the network.");
    }
}
