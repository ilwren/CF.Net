using CFNet.Http;
using CFNet.Models.IpRanges;

namespace CFNet.Services;

/// <summary>
/// The IP ranges Cloudflare's edge answers from - one endpoint, and the one most often reached for by a
/// firewall rule on somebody else's origin server.
/// </summary>
/// <remarks>
/// <para>
/// <b>No permission is needed.</b> Cloudflare publishes no permission group for <c>GET /ips</c>, and the path
/// carries neither an account nor a zone, so this is readable by any authenticated caller - including a token
/// that can do nothing else at all. That makes it a useful first call for a client to make when it wants to
/// know its credentials work before it spends one on something that could be refused.
/// </para>
/// <para>
/// The answer is <see cref="CloudflareIpRanges" />, which carries an <c>etag</c> digest worth caching against
/// and a <see cref="CloudflareIpRanges.Contains(string?)" /> helper for the question callers actually have:
/// whether a given address arrived through Cloudflare.
/// </para>
/// </remarks>
public interface IIpRangesService
{
    /// <summary>Reads the ranges, optionally adding the JD Cloud ones.</summary>
    /// <param name="parameters">Which networks to include; Cloudflare's own ranges only when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ranges Cloudflare reported, with its digest of them.</returns>
    /// <remarks>
    /// <c>GET /ips</c>. At the API root, with no account and no zone in the path.
    /// </remarks>
    Task<CloudflareIpRanges> ListAsync(
        IpRangesListParameters? parameters = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class IpRangesService : CloudflareService, IIpRangesService
{
    /// <summary>The path, at the API root - no account, no zone.</summary>
    private const string IpsPath = "ips";

    /// <summary>Creates the service over a transport.</summary>
    /// <param name="transport">The transport that carries the call.</param>
    public IpRangesService(ICloudflareTransport transport)
        : base(transport)
    {
    }

    /// <inheritdoc />
    public async Task<CloudflareIpRanges> ListAsync(
        IpRangesListParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get(IpsPath);
        parameters?.Apply(request.Query);
        request.OperationName = "List Cloudflare IP Ranges";

        var response = await Transport
            .SendAsync<CloudflareIpRanges>(request, cancellationToken)
            .ConfigureAwait(false);

        return response.EnsureResult();
    }
}
