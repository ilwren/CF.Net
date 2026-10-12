using System.Runtime.CompilerServices;
using CFNet.Http;
using CFNet.Models.Ssl;

namespace CFNet.Services;

/// <summary>
/// SSL certificate packs of a zone: <c>/zones/{zone_id}/ssl/certificate_packs</c>.
/// </summary>
/// <remarks>
/// Listing and reading need the <c>SSL and Certificates Read</c> permission (or <c>Write</c>); deleting an
/// Advanced Certificate Manager pack needs <c>SSL and Certificates Write</c>. Not wrapped by this release:
/// ordering a pack (<c>POST …/certificate_packs/order</c>), restarting validation or changing branding
/// (<c>PATCH …/certificate_packs/{certificate_pack_id}</c>), the quota (<c>GET …/certificate_packs/quota</c>),
/// universal SSL settings (<c>/zones/{zone_id}/ssl/universal/settings</c>) and DCV delegation
/// (<c>/zones/{zone_id}/ssl/verification</c>).
/// </remarks>
public interface ISslCertificatesService
{
    /// <summary>Lists the certificate packs of a zone, one page at a time.</summary>
    /// <param name="parameters">Optional <c>deploy</c>, <c>status</c> and paging.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// Without <c>status=all</c> Cloudflare lists only the active packs. <c>per_page</c> is between 5 and
    /// 50 for this endpoint.
    /// </remarks>
    Task<CloudflarePage<SslCertificatePack>> ListAsync(
        SslCertificatePackListParameters? parameters = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every certificate pack of a zone, walking the pages for you.</summary>
    /// <param name="parameters">Optional <c>deploy</c>, <c>status</c> and page size; <c>page</c> is overwritten as the walk advances.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<SslCertificatePack> ListAllAsync(
        SslCertificatePackListParameters? parameters = null,
        string? zoneId = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default);

    /// <summary>Fetches one certificate pack via <c>GET /zones/{zone_id}/ssl/certificate_packs/{certificate_pack_id}</c>.</summary>
    /// <param name="certificatePackId">The pack id, as listed by <see cref="ListAsync"/>.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The pack, or <c>null</c> when the zone has no such pack.</returns>
    /// <remarks>
    /// Only 404 is turned into <c>null</c>. A zone id the token cannot see is answered with 403 - code
    /// 9109 for an unknown zone, 10000 for one outside the token's Zone Resources - and surfaces as a
    /// <see cref="CFNet.Exceptions.CloudflareApiException"/>, because that is a scoping problem rather than a
    /// missing pack.
    /// </remarks>
    Task<SslCertificatePack?> GetAsync(
        string certificatePackId,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Orders an Advanced Certificate Manager pack via <c>POST /zones/{zone_id}/ssl/certificate_packs/order</c>.
    /// </summary>
    /// <param name="request">Hosts, validation method, validity, authority and branding; validated locally first.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The pack as Cloudflare created it, usually still <c>initializing</c> or <c>pending_validation</c>.</returns>
    /// <remarks>
    /// Note the path: ordering posts to <c>…/certificate_packs/order</c>, not to the collection itself. The
    /// hosts must contain the zone apex. Validation is not automatic - with <c>txt</c> or <c>http</c> you
    /// still have to answer the challenge, and with <c>email</c> someone has to click the link - so the pack
    /// stays <c>pending_validation</c> until then. Needs <c>SSL and Certificates Write</c> and, being a
    /// paid Advanced Certificate Manager feature, an account that has it enabled.
    /// </remarks>
    Task<SslCertificatePack> OrderAsync(
        OrderSslCertificatePackRequest request,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes an Advanced Certificate Manager pack via
    /// <c>PATCH /zones/{zone_id}/ssl/certificate_packs/{certificate_pack_id}</c>.
    /// </summary>
    /// <param name="certificatePackId">The pack id.</param>
    /// <param name="request">
    /// What to change. <see cref="UpdateSslCertificatePackRequest.ForCloudflareBranding"/> sets branding;
    /// <see cref="UpdateSslCertificatePackRequest.ToRestartValidation"/> sends an empty body, which is how
    /// validation is restarted.
    /// </param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The pack as Cloudflare reports it after the change.</returns>
    /// <remarks>Needs <c>SSL and Certificates Write</c>.</remarks>
    Task<SslCertificatePack> UpdateAsync(
        string certificatePackId,
        UpdateSslCertificatePackRequest request,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Adds or removes Cloudflare branding on a pack.</summary>
    /// <param name="certificatePackId">The pack id.</param>
    /// <param name="enabled">
    /// When true, Cloudflare adds a <c>sni.cloudflaressl.com</c> subdomain as the certificate's common name.
    /// </param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SslCertificatePack> SetCloudflareBrandingAsync(
        string certificatePackId,
        bool enabled,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Restarts domain control validation on a pack whose validation timed out.</summary>
    /// <param name="certificatePackId">The pack id.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The pack, usually back in <c>pending_validation</c>.</returns>
    /// <remarks>
    /// Sends an empty body, which is what Cloudflare reads as "restart validation". It is only a validation
    /// operation for a pack in <see cref="SslCertificatePackStatuses.ValidationTimedOut"/>; on any other
    /// pack Cloudflare rejects it rather than silently re-issuing. The challenge still has to be answered
    /// afterwards - restarting only starts the clock again.
    /// </remarks>
    Task<SslCertificatePack> RestartValidationAsync(
        string certificatePackId,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes an Advanced Certificate Manager pack via <c>DELETE /zones/{zone_id}/ssl/certificate_packs/{certificate_pack_id}</c>.</summary>
    /// <param name="certificatePackId">The pack id.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The identifier Cloudflare echoes back, or <c>null</c> when it sends no result.</returns>
    /// <remarks>
    /// Destructive and irreversible: the hosts on the pack fall back to whatever certificate still covers
    /// them, and the universal certificate is not affected. Needs <c>SSL and Certificates Write</c>.
    /// </remarks>
    Task<string?> DeleteAsync(
        string certificatePackId,
        string? zoneId = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class SslCertificatesService : CloudflareService, ISslCertificatesService
{
    /// <summary>Creates the service.</summary>
    /// <param name="transport">The transport to execute requests through.</param>
    public SslCertificatesService(ICloudflareTransport transport)
        : base(transport)
    {
    }

    /// <inheritdoc />
    public async Task<CloudflarePage<SslCertificatePack>> ListAsync(
        SslCertificatePackListParameters? parameters = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var zone = ResolveZoneId(zoneId);
        var request = Apply(CloudflareRequest.Get($"{ZonePath(zone)}/ssl/certificate_packs"), parameters);
        request.OperationName = "List SSL Certificate Packs";

        var response = await Transport.SendAsync<List<SslCertificatePack>>(request, cancellationToken).ConfigureAwait(false);
        return response.ToPage();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SslCertificatePack> ListAllAsync(
        SslCertificatePackListParameters? parameters = null,
        string? zoneId = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var zone = ResolveZoneId(zoneId);
        var page = 1;

        while (true)
        {
            var effective = new SslCertificatePackListParameters
            {
                Deploy = parameters?.Deploy,
                Status = parameters?.Status,
                PerPage = parameters?.PerPage,
                Page = page,
            };

            var result = await ListAsync(effective, zone, cancellationToken).ConfigureAwait(false);
            foreach (var pack in result.Items)
            {
                yield return pack;
            }

            if (result.Items.Count == 0 || !result.HasMore)
            {
                yield break;
            }

            page++;
        }
    }

    /// <inheritdoc />
    public async Task<SslCertificatePack?> GetAsync(
        string certificatePackId,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var zone = ResolveZoneId(zoneId);
        var id = Require(certificatePackId, nameof(certificatePackId));

        var request = CloudflareRequest.Get($"{ZonePath(zone)}/ssl/certificate_packs/{CloudflareUrl.EncodeSegment(id)}")
            .Tolerate(System.Net.HttpStatusCode.NotFound);
        request.OperationName = "Get SSL Certificate Pack";

        var response = await Transport.SendAsync<SslCertificatePack>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<SslCertificatePack> OrderAsync(
        OrderSslCertificatePackRequest request,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();

        var zone = ResolveZoneId(zoneId);
        var httpRequest = CloudflareRequest.Post($"{ZonePath(zone)}/ssl/certificate_packs/order", request);
        httpRequest.OperationName = "Order SSL Certificate Pack";

        var response = await Transport.SendAsync<SslCertificatePack>(httpRequest, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<SslCertificatePack> UpdateAsync(
        string certificatePackId,
        UpdateSslCertificatePackRequest request,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var zone = ResolveZoneId(zoneId);
        var id = Require(certificatePackId, nameof(certificatePackId));

        var httpRequest = CloudflareRequest.Patch(
            $"{ZonePath(zone)}/ssl/certificate_packs/{CloudflareUrl.EncodeSegment(id)}",
            request);
        httpRequest.OperationName = request.IsEmpty ? "Restart SSL Certificate Pack Validation" : "Update SSL Certificate Pack";

        var response = await Transport.SendAsync<SslCertificatePack>(httpRequest, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public Task<SslCertificatePack> SetCloudflareBrandingAsync(
        string certificatePackId,
        bool enabled,
        string? zoneId = null,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(certificatePackId, UpdateSslCertificatePackRequest.ForCloudflareBranding(enabled), zoneId, cancellationToken);

    /// <inheritdoc />
    public Task<SslCertificatePack> RestartValidationAsync(
        string certificatePackId,
        string? zoneId = null,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(certificatePackId, UpdateSslCertificatePackRequest.ToRestartValidation(), zoneId, cancellationToken);

    /// <inheritdoc />
    public async Task<string?> DeleteAsync(
        string certificatePackId,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var zone = ResolveZoneId(zoneId);
        var id = Require(certificatePackId, nameof(certificatePackId));

        var request = CloudflareRequest.Delete($"{ZonePath(zone)}/ssl/certificate_packs/{CloudflareUrl.EncodeSegment(id)}");
        request.OperationName = "Delete SSL Certificate Pack";

        var response = await Transport.SendAsync<SslCertificatePackDeletion>(request, cancellationToken).ConfigureAwait(false);
        return response.Result?.Id;
    }
}
