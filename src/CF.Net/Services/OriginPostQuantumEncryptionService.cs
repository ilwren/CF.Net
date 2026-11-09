using CFNet.Http;
using CFNet.Models.Ssl;

namespace CFNet.Services;

/// <summary>
/// A zone's origin post-quantum encryption setting - how Cloudflare offers post-quantum key agreements to the
/// zone's origin.
/// </summary>
/// <remarks>
/// Two things about this endpoint are worth knowing before calling it.
/// <list type="bullet">
/// <item>
/// The path is not where the subject lives. The setting governs the TLS handshake with the origin, and this
/// library keeps it with the other SSL resources, but Cloudflare publishes it under the zone's
/// <c>cache</c> segment - <c>/zones/{zone_id}/cache/origin_post_quantum_encryption</c> - so the path here says
/// <c>cache</c> even though nothing about caching is involved.
/// </item>
/// <item>
/// Writing to it no longer changes the zone. Cloudflare replaced this control with automatic key exchange and
/// documents that requests to this API are no-ops: a <see cref="UpdateAsync"/> that answers success has not, by
/// itself, changed the zone's post-quantum key agreement. Read the setting again afterwards if the value that
/// took effect is what matters.
/// </item>
/// </list>
/// </remarks>
public interface IOriginPostQuantumEncryptionService
{
    /// <summary>
    /// Reads the zone's origin post-quantum encryption setting.
    /// </summary>
    /// <param name="zoneId">Zone id, or <see langword="null"/> for the configured one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The setting.</returns>
    Task<OriginPostQuantumEncryption> GetAsync(string? zoneId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the zone's origin post-quantum encryption value. See the remarks on
    /// <see cref="IOriginPostQuantumEncryptionService"/>: Cloudflare documents this call as a no-op, so the
    /// answer says what the endpoint returned rather than what the zone now does.
    /// </summary>
    /// <param name="request">The value to set.</param>
    /// <param name="zoneId">Zone id, or <see langword="null"/> for the configured one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The setting Cloudflare answered with.</returns>
    /// <exception cref="CFNet.Exceptions.CloudflareConfigurationException">Thrown when the value is missing or
    /// carries a value this library has not met.</exception>
    Task<OriginPostQuantumEncryption> UpdateAsync(
        OriginPostQuantumEncryptionRequest request,
        string? zoneId = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IOriginPostQuantumEncryptionService" />
public sealed class OriginPostQuantumEncryptionService : CloudflareService, IOriginPostQuantumEncryptionService
{
    private const string Suffix = "cache/origin_post_quantum_encryption";

    /// <summary>Builds the service over a transport.</summary>
    /// <param name="transport">The transport that sends requests and reads Cloudflare's envelope.</param>
    public OriginPostQuantumEncryptionService(ICloudflareTransport transport)
        : base(transport)
    {
    }

    /// <inheritdoc />
    public async Task<OriginPostQuantumEncryption> GetAsync(
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get(SettingsPath(zoneId));
        request.OperationName = "Get origin post-quantum encryption";

        var response = await Transport.SendAsync<OriginPostQuantumEncryption>(request, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<OriginPostQuantumEncryption> UpdateAsync(
        OriginPostQuantumEncryptionRequest request,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();

        var put = CloudflareRequest.Put(SettingsPath(zoneId), request);
        put.OperationName = "Update origin post-quantum encryption";

        var response = await Transport.SendAsync<OriginPostQuantumEncryption>(put, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }

    private string SettingsPath(string? zoneId) => $"{ZonePath(ResolveZoneId(zoneId))}/{Suffix}";
}
