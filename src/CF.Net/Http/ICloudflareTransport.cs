using CFNet.Configuration;

namespace CFNet.Http;

/// <summary>
/// Executes <see cref="CloudflareRequest"/> instances against the Cloudflare API.
/// </summary>
/// <remarks>
/// This is the only seam between the typed services and the network. Replace it in tests to assert on
/// requests, or decorate it to add caching, metrics or a circuit breaker.
/// </remarks>
public interface ICloudflareTransport
{
    /// <summary>The options the transport was created with.</summary>
    CloudflareClientOptions Options { get; }

    /// <summary>
    /// Sends the request and deserialises the standard Cloudflare envelope.
    /// </summary>
    /// <typeparam name="T">Type of the <c>result</c> field.</typeparam>
    /// <param name="request">The request description.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="Exceptions.CloudflareApiException">
    /// The API reported a failure and <see cref="CloudflareClientOptions.ThrowOnApiError"/> is <c>true</c>.
    /// </exception>
    /// <exception cref="Exceptions.CloudflareTransportException">The request never reached Cloudflare.</exception>
    Task<CloudflareResponse<T>> SendAsync<T>(CloudflareRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the request and returns the raw, unparsed response. Use it for endpoints whose payload is
    /// not a JSON envelope (KV values, Worker module downloads).
    /// </summary>
    /// <param name="request">The request description.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CloudflareRawResponse> SendRawAsync(CloudflareRequest request, CancellationToken cancellationToken = default);
}
