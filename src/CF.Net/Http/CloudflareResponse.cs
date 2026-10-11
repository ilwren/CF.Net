using System.Net;
using CFNet.Models.Common;

namespace CFNet.Http;

/// <summary>
/// The parsed Cloudflare v4 response envelope: <c>result</c>, <c>success</c>, <c>errors</c>,
/// <c>messages</c> and <c>result_info</c>.
/// </summary>
/// <typeparam name="T">The type <c>result</c> was deserialised into.</typeparam>
public sealed class CloudflareResponse<T>
{
    /// <summary>Value of the top level <c>success</c> field.</summary>
    public bool Success { get; init; }

    /// <summary>The deserialised <c>result</c>. May be <c>null</c> for endpoints that return none.</summary>
    public T? Result { get; init; }

    /// <summary>Structured errors returned by Cloudflare.</summary>
    public IReadOnlyList<CloudflareErrorInfo> Errors { get; init; } = Array.Empty<CloudflareErrorInfo>();

    /// <summary>
    /// Free form messages. Cloudflare is inconsistent here: most endpoints return an array of strings,
    /// a few (D1, for example) return an array of objects with a <c>message</c> field. Both shapes are
    /// normalised into plain strings.
    /// </summary>
    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();

    /// <summary>Pagination metadata, when the endpoint is paginated.</summary>
    public CloudflareResultInfo? ResultInfo { get; init; }

    /// <summary>HTTP status code.</summary>
    public HttpStatusCode StatusCode { get; init; }

    /// <summary>The URI that was called.</summary>
    public Uri? RequestUri { get; init; }

    /// <summary>Raw response body as text, for logging and for the gallery's response pane.</summary>
    public string? RawBody { get; init; }

    /// <summary>Value of the <c>CF-RAY</c> header.</summary>
    public string? RayId { get; init; }

    /// <summary>All response headers, flattened to one value per name.</summary>
    public IReadOnlyDictionary<string, string> ResponseHeaders { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns <see cref="Result"/> or throws when the API reported a failure and the caller asked
    /// the transport not to throw.
    /// </summary>
    public T EnsureResult()
    {
        if (Result is null)
        {
            throw new Exceptions.CloudflareApiException(
                $"Cloudflare returned a successful response without a result payload for '{RequestUri}'.",
                StatusCode,
                RequestUri,
                Errors,
                Messages,
                RawBody,
                RayId);
        }

        return Result;
    }

    /// <summary>Projects the result onto another shape while keeping all metadata.</summary>
    /// <typeparam name="TOut">The projected result type.</typeparam>
    /// <param name="selector">Projection function.</param>
    public CloudflareResponse<TOut> Select<TOut>(Func<T?, TOut?> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        return new CloudflareResponse<TOut>
        {
            Success = Success,
            Result = selector(Result),
            Errors = Errors,
            Messages = Messages,
            ResultInfo = ResultInfo,
            StatusCode = StatusCode,
            RequestUri = RequestUri,
            RawBody = RawBody,
            RayId = RayId,
            ResponseHeaders = ResponseHeaders,
        };
    }
}

/// <summary>
/// An unparsed response. Used for endpoints whose body is not a JSON envelope - the KV value
/// getters are the notable example, they return the stored bytes verbatim.
/// </summary>
public sealed class CloudflareRawResponse
{
    /// <summary>HTTP status code.</summary>
    public HttpStatusCode StatusCode { get; init; }

    /// <summary>The URI that was called.</summary>
    public Uri? RequestUri { get; init; }

    /// <summary>Response body as bytes; empty when there was none.</summary>
    public byte[] Content { get; init; } = Array.Empty<byte>();

    /// <summary>Value of the <c>Content-Type</c> response header.</summary>
    public string? ContentType { get; init; }

    /// <summary>Value of the <c>CF-RAY</c> header.</summary>
    public string? RayId { get; init; }

    /// <summary>All response headers, flattened to one value per name.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>True for 2xx status codes.</summary>
    public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and <= 299;

    /// <summary>The body decoded as UTF-8 text.</summary>
    public string Body => Content.Length == 0 ? string.Empty : System.Text.Encoding.UTF8.GetString(Content);

    /// <summary>Returns the first value of a response header, or <c>null</c>.</summary>
    /// <param name="name">Header name; matched case-insensitively.</param>
    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
}
