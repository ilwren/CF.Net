using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace CFNet.UnitTests.Testing;

/// <summary>
/// Everything captured from one outgoing request. The body is read inside the handler because
/// <see cref="HttpContent"/> is disposed as soon as the send completes.
/// </summary>
public sealed class RecordedRequest
{
    internal RecordedRequest(
        HttpMethod method,
        Uri? uri,
        string? body,
        string? contentType,
        IReadOnlyDictionary<string, string> headers,
        IReadOnlyDictionary<string, string> contentHeaders)
    {
        Method = method;
        Uri = uri;
        Body = body;
        ContentType = contentType;
        Headers = headers;
        ContentHeaders = contentHeaders;
    }

    /// <summary>HTTP verb.</summary>
    public HttpMethod Method { get; }

    /// <summary>The absolute URI as the transport built it.</summary>
    public Uri? Uri { get; }

    /// <summary>Request body as text, or <c>null</c> when there was none.</summary>
    public string? Body { get; }

    /// <summary>Media type of the body, or <c>null</c> when there was none.</summary>
    public string? ContentType { get; }

    /// <summary>Request headers, joined comma-separated when repeated.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>Content headers.</summary>
    public IReadOnlyDictionary<string, string> ContentHeaders { get; }

    /// <summary>The path portion of <see cref="Uri"/>, still percent-encoded.</summary>
    public string Path => Uri?.AbsolutePath ?? string.Empty;

    /// <summary>The query string including the leading <c>?</c>, still percent-encoded.</summary>
    public string Query => Uri?.Query ?? string.Empty;

    /// <summary>Returns a header value by name, case-insensitively.</summary>
    public string? Header(string name) =>
        Headers.TryGetValue(name, out var value) ? value
        : ContentHeaders.TryGetValue(name, out var contentValue) ? contentValue
        : null;
}

/// <summary>
/// An <see cref="HttpMessageHandler"/> that never touches the network. Responses are queued up front
/// and consumed in order; every request is recorded for assertions.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();
    private readonly Func<HttpRequestMessage, int, HttpResponseMessage>? _responder;
    private readonly List<RecordedRequest> _requests = new();
    private int _count;

    /// <summary>Creates a handler that answers from a queue of pre-built responses.</summary>
    public StubHttpMessageHandler()
    {
    }

    /// <summary>
    /// Creates a handler that computes each response. Use this for retries, where every attempt must
    /// get a fresh <see cref="HttpResponseMessage"/>.
    /// </summary>
    /// <param name="responder">Receives the request and its zero-based sequence number.</param>
    public StubHttpMessageHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    /// <summary>Every request seen so far, in order.</summary>
    public IReadOnlyList<RecordedRequest> Requests => _requests;

    /// <summary>The most recent request.</summary>
    public RecordedRequest LastRequest => Requests.Count == 0
        ? throw new InvalidOperationException("No request has been sent yet.")
        : Requests[^1];

    /// <summary>How many requests have been sent.</summary>
    public int RequestCount => _count;

    /// <summary>Queues a JSON response.</summary>
    /// <param name="json">Body text.</param>
    /// <param name="status">Status code.</param>
    /// <param name="headers">Optional response headers.</param>
    public StubHttpMessageHandler Respond(string json, HttpStatusCode status = HttpStatusCode.OK, IDictionary<string, string>? headers = null)
    {
        var message = new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        if (headers is not null)
        {
            foreach (var pair in headers)
            {
                message.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
            }
        }

        _responses.Enqueue(message);
        return this;
    }

    /// <summary>Queues a successful Cloudflare envelope around a JSON <c>result</c> payload.</summary>
    /// <param name="resultJson">Raw JSON for the <c>result</c> member.</param>
    /// <param name="success">Value of the envelope <c>success</c> member.</param>
    public StubHttpMessageHandler RespondWithResult(string resultJson, bool success = true) =>
        Respond(string.Concat("{\"success\":", success ? "true" : "false", ",\"errors\":[],\"messages\":[],\"result\":", resultJson, "}"));

    /// <summary>Queues a raw (non-JSON) response, as KV values and Worker content use.</summary>
    /// <param name="body">Body bytes.</param>
    /// <param name="contentType">Media type.</param>
    /// <param name="status">Status code.</param>
    /// <param name="headers">Optional response headers.</param>
    public StubHttpMessageHandler RespondRaw(byte[] body, string contentType, HttpStatusCode status = HttpStatusCode.OK, IDictionary<string, string>? headers = null)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);

        var message = new HttpResponseMessage(status) { Content = content };
        if (headers is not null)
        {
            foreach (var pair in headers)
            {
                message.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
                message.Content.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
            }
        }

        _responses.Enqueue(message);
        return this;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var index = _count++;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
        {
            headers[header.Key] = string.Join(", ", header.Value);
        }

        var contentHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (request.Content is not null)
        {
            foreach (var header in request.Content.Headers)
            {
                contentHeaders[header.Key] = string.Join(", ", header.Value);
            }
        }

        _requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri,
            body,
            request.Content?.Headers.ContentType?.ToString(),
            headers,
            contentHeaders));

        if (_responder is not null)
        {
            return _responder(request, index);
        }

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException(
                $"No queued response left for request #{index + 1} ({request.Method} {request.RequestUri}). Queue one with Respond/RespondWithResult/RespondRaw.");
        }

        return _responses.Dequeue();
    }
}
