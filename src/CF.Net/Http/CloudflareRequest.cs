using System.Net;

namespace CFNet.Http;

/// <summary>
/// A fully described, transport neutral Cloudflare API call.
/// </summary>
/// <remarks>
/// Services build these; <see cref="ICloudflareTransport"/> executes them. Keeping the description
/// separate from <see cref="HttpRequestMessage"/> has two benefits: the body can be re-materialised
/// for every retry attempt, and unit tests can inspect exactly what a service wanted to send.
/// </remarks>
public sealed class CloudflareRequest
{
    /// <summary>Creates a request.</summary>
    /// <param name="method">HTTP method.</param>
    /// <param name="path">
    /// Path relative to the configured base address, e.g. <c>zones/{id}/dns_records</c>.
    /// Segments the caller interpolated must already be percent-encoded - use
    /// <see cref="CloudflareUrl.EncodeSegment"/>.
    /// </param>
    public CloudflareRequest(HttpMethod method, string path)
    {
        Method = method ?? throw new ArgumentNullException(nameof(method));
        Path = path ?? throw new ArgumentNullException(nameof(path));
    }

    /// <summary>HTTP method.</summary>
    public HttpMethod Method { get; }

    /// <summary>Path relative to <see cref="Configuration.CloudflareClientOptions.BaseAddress"/>.</summary>
    public string Path { get; }

    /// <summary>Query string parameters, built in insertion order.</summary>
    public QueryStringBuilder Query { get; } = new();

    /// <summary>Additional request headers (merged after the authentication headers).</summary>
    public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Request payload, or <c>null</c> for methods without a body.</summary>
    public CloudflareRequestBody? Body { get; set; }

    /// <summary>
    /// Status codes that should be returned to the caller instead of throwing. Used for
    /// <c>GET</c> operations that treat "not found" as a normal outcome.
    /// </summary>
    public ISet<HttpStatusCode> ToleratedStatusCodes { get; } = new HashSet<HttpStatusCode>();

    /// <summary>
    /// Short, human readable label used in log messages and in exceptions, e.g.
    /// <c>GET zones/{zone_id}/dns_records</c>.
    /// </summary>
    public string? OperationName { get; set; }

    /// <summary>Creates a GET request.</summary>
    public static CloudflareRequest Get(string path) => new(HttpMethod.Get, path);

    /// <summary>Creates a POST request with a JSON body.</summary>
    public static CloudflareRequest Post(string path, object? body = null) =>
        new(HttpMethod.Post, path) { Body = body is null ? null : new JsonRequestBody(body) };

    /// <summary>Creates a PUT request with a JSON body.</summary>
    public static CloudflareRequest Put(string path, object? body = null) =>
        new(HttpMethod.Put, path) { Body = body is null ? null : new JsonRequestBody(body) };

    /// <summary>Creates a PATCH request with a JSON body.</summary>
    public static CloudflareRequest Patch(string path, object? body = null) =>
        new(HttpMethod.Patch, path) { Body = body is null ? null : new JsonRequestBody(body) };

    /// <summary>Creates a DELETE request.</summary>
    public static CloudflareRequest Delete(string path) => new(HttpMethod.Delete, path);

    /// <summary>Marks the given status codes as non-fatal.</summary>
    public CloudflareRequest Tolerate(params HttpStatusCode[] statusCodes)
    {
        foreach (var code in statusCodes)
        {
            ToleratedStatusCodes.Add(code);
        }

        return this;
    }

    /// <summary>Adds a header.</summary>
    public CloudflareRequest WithHeader(string name, string value)
    {
        Headers[name] = value;
        return this;
    }

    /// <inheritdoc />
    public override string ToString() => $"{OperationName ?? Method.Method} {Path}{(Query.IsEmpty ? string.Empty : "?" + Query)}";
}
