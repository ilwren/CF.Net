using System.Net;
using System.Text;
using CFNet.Models.Common;

namespace CFNet.Exceptions;

/// <summary>
/// Raised when Cloudflare answered, but reported a failure - either a non 2xx HTTP status or a
/// 200 response with <c>"success": false</c>. Carries the structured <c>errors</c> array so callers
/// can branch on <see cref="ErrorCode"/> instead of string matching.
/// </summary>
public sealed class CloudflareApiException : CloudflareException
{
    /// <summary>Creates a new API exception.</summary>
    public CloudflareApiException(
        string message,
        HttpStatusCode? statusCode,
        Uri? requestUri,
        IReadOnlyList<CloudflareErrorInfo>? errors = null,
        IReadOnlyList<string>? messages = null,
        string? responseBody = null,
        string? rayId = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        RequestUri = requestUri;
        Errors = errors ?? Array.Empty<CloudflareErrorInfo>();
        Messages = messages ?? Array.Empty<string>();
        ResponseBody = responseBody;
        RayId = rayId;
    }

    /// <summary>HTTP status code, when a response was received at all.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>The URI that produced the failure.</summary>
    public Uri? RequestUri { get; }

    /// <summary>Structured Cloudflare errors (<c>code</c> + <c>message</c>).</summary>
    public IReadOnlyList<CloudflareErrorInfo> Errors { get; }

    /// <summary>Free form messages returned alongside the errors.</summary>
    public IReadOnlyList<string> Messages { get; }

    /// <summary>Raw response body, for logging and diagnostics.</summary>
    public string? ResponseBody { get; }

    /// <summary>Value of the <c>CF-RAY</c> response header.</summary>
    public string? RayId { get; }

    /// <summary>The first Cloudflare error code, or <c>null</c> when the API returned none.</summary>
    public int? ErrorCode => Errors.Count > 0 ? Errors[0].Code : null;

    /// <summary>True for HTTP 429 - back off and retry later.</summary>
    public bool IsRateLimited => StatusCode == (HttpStatusCode)429;

    /// <summary>True for HTTP 404 - the resource does not exist.</summary>
    public bool IsNotFound => StatusCode == HttpStatusCode.NotFound;

    /// <summary>True for HTTP 401/403 - credentials missing, expired or insufficiently scoped.</summary>
    public bool IsAuthenticationFailure =>
        StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    /// <summary>True for HTTP 400/422 or any response carrying Cloudflare validation errors.</summary>
    public bool IsValidationFailure =>
        StatusCode is HttpStatusCode.BadRequest || (StatusCode == (HttpStatusCode)422);

    /// <summary>True when the failure looks retryable (429 or 5xx).</summary>
    public bool IsTransient => IsRateLimited || (StatusCode.HasValue && (int)StatusCode.Value >= 500);

    /// <summary>True when <see cref="ErrorCode"/> equals <paramref name="code"/>.</summary>
    public bool HasErrorCode(int code) => Errors.Any(e => e.Code == code);

    /// <inheritdoc />
    public override string ToString()
    {
        var builder = new StringBuilder(base.ToString());
        if (StatusCode.HasValue)
        {
            builder.Append(Environment.NewLine).Append("Status: ").Append((int)StatusCode.Value).Append(' ').Append(StatusCode.Value);
        }

        if (RequestUri is not null)
        {
            builder.Append(Environment.NewLine).Append("Request: ").Append(RequestUri);
        }

        if (!string.IsNullOrEmpty(RayId))
        {
            builder.Append(Environment.NewLine).Append("CF-RAY: ").Append(RayId);
        }

        foreach (var error in Errors)
        {
            builder.Append(Environment.NewLine).Append("Error ").Append(error.Code).Append(": ").Append(error.Message);
        }

        return builder.ToString();
    }
}
