namespace CFNet.Exceptions;

/// <summary>
/// Raised when a request never produced a usable HTTP response: DNS failure, TLS handshake failure,
/// connection reset, or a client side timeout. Distinct from <see cref="CloudflareApiException"/>
/// so callers can tell "Cloudflare said no" from "we never reached Cloudflare".
/// </summary>
public sealed class CloudflareTransportException : CloudflareException
{
    /// <summary>Creates a new transport exception.</summary>
    public CloudflareTransportException(string message, Uri? requestUri = null, Exception? innerException = null)
        : base(message, innerException)
    {
        RequestUri = requestUri;
    }

    /// <summary>The URI that could not be reached.</summary>
    public Uri? RequestUri { get; }

    /// <summary>True when the failure was a client side timeout rather than a network error.</summary>
    public bool IsTimeout { get; init; }

    /// <summary>Number of attempts that were made before giving up.</summary>
    public int Attempts { get; init; }
}
