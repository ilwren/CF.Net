using CFNet.Http;
using CFNet.Models.Ssl;

namespace CFNet.Services;

/// <summary>
/// Mutual TLS towards the origin: <c>/zones/{zone_id}/origin_tls_client_auth</c>, the client certificate
/// Cloudflare presents when it connects to a zone's origin, either for the whole zone or per hostname.
/// </summary>
/// <remarks>
/// <para>
/// Reading needs <c>SSL and Certificates Read</c>, everything else <c>SSL and Certificates Write</c> - and
/// the certificate list accepts either, which is unusual for a read that returns key material's neighbours.
/// </para>
/// <para>
/// <b>Three switches have to agree before a certificate is used.</b> The zone-level setting
/// (<see cref="SetSettingsAsync"/>) turns authenticated origin pulls on for the zone; a hostname association
/// (<see cref="SetHostnamesAsync"/>) points one hostname at one uploaded certificate and enables it; and the
/// certificate itself has to have reached <c>active</c>. A hostname that is <c>disabled</c> does not get the
/// certificate even when the zone-level switch is on.
/// </para>
/// <para>
/// <b>Deleting a certificate is a two-step operation.</b> Cloudflare requires every hostname associated with
/// it to be voided first - a <c>PUT</c> with <c>enabled</c> set to <c>null</c>, which
/// <see cref="VoidHostnameAsync"/> sends - and only then will <see cref="DeleteCertificateAsync"/> remove it.
/// </para>
/// <para>
/// The four top-level endpoints (<see cref="ListZoneCertificatesAsync"/> and friends) are <b>deprecated</b>
/// by Cloudflare and manage one certificate for the whole zone; they are wrapped so an existing deployment
/// can still be read and dismantled, and they return the private key they were given. Everything new should
/// use the per-hostname resources.
/// </para>
/// </remarks>
public interface IOriginTlsClientAuthService
{
    /// <summary>
    /// Reads the zone-level switch via <c>GET /zones/{zone_id}/origin_tls_client_auth/settings</c>.
    /// </summary>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The setting, whose only field is <c>enabled</c>.</returns>
    Task<OriginTlsClientAuthSetting> GetSettingsAsync(
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Turns zone-level authenticated origin pulls on or off via
    /// <c>PUT /zones/{zone_id}/origin_tls_client_auth/settings</c>.
    /// </summary>
    /// <param name="enabled">Whether the zone should present a client certificate to its origin.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The setting as it now stands.</returns>
    /// <remarks>
    /// Cloudflare notes the switch may be set either before or after the certificate is uploaded for it to
    /// be in use, so the order of those two calls does not matter - but both are needed.
    /// </remarks>
    Task<OriginTlsClientAuthSetting> SetSettingsAsync(
        bool enabled,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one hostname's association via
    /// <c>GET /zones/{zone_id}/origin_tls_client_auth/hostnames/{hostname}</c>.
    /// </summary>
    /// <param name="hostname">The hostname on the origin.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The association with both of its statuses, or <c>null</c> when the hostname has none.
    /// </returns>
    /// <remarks>
    /// The path this reads sits beside <c>…/hostnames/certificates</c>, so a hostname literally called
    /// <c>certificates</c> would be indistinguishable from the certificate collection.
    /// </remarks>
    Task<OriginTlsHostnameAssociation?> GetHostnameAsync(
        string hostname,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a batch of association changes via
    /// <c>PUT /zones/{zone_id}/origin_tls_client_auth/hostnames</c>.
    /// </summary>
    /// <param name="request">The <c>config</c> array of changes to apply.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The associations as they now stand - the response is an array, not a page.</returns>
    /// <remarks>
    /// Validates the request and throws <see cref="Exceptions.CloudflareConfigurationException"/> before
    /// anything is sent. The hostname travels in the body, not the path, and at most 100 hostnames may share
    /// one certificate.
    /// </remarks>
    Task<IReadOnlyList<OriginTlsHostnameAssociation>> SetHostnamesAsync(
        UpdateOriginTlsHostnameAssociationsRequest request,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Associates one hostname with a certificate and enables it.</summary>
    /// <param name="hostname">The hostname on the origin.</param>
    /// <param name="certificateId">Identifier of an uploaded hostname certificate.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The associations as they now stand.</returns>
    Task<IReadOnlyList<OriginTlsHostnameAssociation>> EnableHostnameAsync(
        string hostname,
        string certificateId,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Keeps a hostname's association but stops the certificate being sent for it, which holds even when the
    /// zone-level switch is on.
    /// </summary>
    /// <param name="hostname">The hostname on the origin.</param>
    /// <param name="certificateId">Identifier of the certificate it points at.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The associations as they now stand.</returns>
    Task<IReadOnlyList<OriginTlsHostnameAssociation>> DisableHostnameAsync(
        string hostname,
        string certificateId,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Voids a hostname's association by sending <c>enabled: null</c>, which is what Cloudflare requires
    /// before the certificate can be deleted.
    /// </summary>
    /// <param name="hostname">The hostname on the origin.</param>
    /// <param name="certificateId">Identifier of the certificate to disassociate.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The associations as they now stand.</returns>
    /// <remarks>
    /// Void is not the same as disable: a disabled association still exists and still counts towards the 100
    /// per certificate, while a void one is gone.
    /// </remarks>
    Task<IReadOnlyList<OriginTlsHostnameAssociation>> VoidHostnameAsync(
        string hostname,
        string certificateId,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the zone's hostname client certificates via
    /// <c>GET /zones/{zone_id}/origin_tls_client_auth/hostnames/certificates</c>.
    /// </summary>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Every certificate - the endpoint takes no filters and does not page.</returns>
    /// <remarks>Ten hostname certificates per zone are allowed.</remarks>
    Task<IReadOnlyList<OriginTlsClientCertificate>> ListCertificatesAsync(
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one hostname client certificate via
    /// <c>GET /zones/{zone_id}/origin_tls_client_auth/hostnames/certificates/{certificate_id}</c>.
    /// </summary>
    /// <param name="certificateId">Certificate identifier.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The certificate, or <c>null</c> when the zone has no such certificate.</returns>
    Task<OriginTlsClientCertificate?> GetCertificateAsync(
        string certificateId,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a client certificate for per-hostname use via
    /// <c>POST /zones/{zone_id}/origin_tls_client_auth/hostnames/certificates</c>.
    /// </summary>
    /// <param name="request">The certificate and its private key.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored certificate, with the id to associate hostnames with.</returns>
    /// <remarks>
    /// Validates the request and throws <see cref="Exceptions.CloudflareConfigurationException"/> before
    /// anything is sent. Uploading does not put the certificate to use: a hostname still has to be
    /// associated with it and enabled.
    /// </remarks>
    Task<OriginTlsClientCertificate> UploadCertificateAsync(
        UploadOriginTlsCertificateRequest request,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Uploads a certificate and its key for per-hostname use.</summary>
    /// <param name="certificate">PEM certificate.</param>
    /// <param name="privateKey">PEM private key matching the certificate.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored certificate, with the id to associate hostnames with.</returns>
    Task<OriginTlsClientCertificate> UploadCertificateAsync(
        string certificate,
        string privateKey,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a hostname client certificate via
    /// <c>DELETE /zones/{zone_id}/origin_tls_client_auth/hostnames/certificates/{certificate_id}</c>.
    /// </summary>
    /// <param name="certificateId">Certificate identifier.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The certificate Cloudflare removed, or <c>null</c> when it sends no result.</returns>
    /// <remarks>
    /// Every hostname associated with it must be voided first - see <see cref="VoidHostnameAsync"/> - or
    /// Cloudflare refuses the deletion.
    /// </remarks>
    Task<OriginTlsClientCertificate?> DeleteCertificateAsync(
        string certificateId,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <b>Deprecated.</b> Lists the zone-level certificates via
    /// <c>GET /zones/{zone_id}/origin_tls_client_auth</c>.
    /// </summary>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Every zone-level certificate, each carrying its private key.</returns>
    /// <remarks>
    /// Cloudflare deprecates this in favour of per-hostname certificates; it is here so an existing
    /// deployment can be inspected and taken apart. Treat the result as secret-bearing.
    /// </remarks>
    Task<IReadOnlyList<ZoneAuthenticatedOriginPull>> ListZoneCertificatesAsync(
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <b>Deprecated.</b> Reads one zone-level certificate via
    /// <c>GET /zones/{zone_id}/origin_tls_client_auth/{certificate_id}</c>.
    /// </summary>
    /// <param name="certificateId">Certificate identifier.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The certificate, or <c>null</c> when the zone has no such certificate.</returns>
    Task<ZoneAuthenticatedOriginPull?> GetZoneCertificateAsync(
        string certificateId,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <b>Deprecated.</b> Uploads a certificate for the whole zone via
    /// <c>POST /zones/{zone_id}/origin_tls_client_auth</c>.
    /// </summary>
    /// <param name="request">The certificate and its private key.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored certificate, echoing the private key back.</returns>
    /// <remarks>
    /// Validates the request and throws <see cref="Exceptions.CloudflareConfigurationException"/> before
    /// anything is sent. Cloudflare warns to keep only one zone-level certificate active, and it overrides
    /// the shared certificate Cloudflare would otherwise use - so an origin that does not trust it stops
    /// accepting connections from the edge. Prefer the per-hostname resources.
    /// </remarks>
    Task<ZoneAuthenticatedOriginPull> UploadZoneCertificateAsync(
        UploadOriginTlsCertificateRequest request,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <b>Deprecated.</b> Removes a zone-level certificate via
    /// <c>DELETE /zones/{zone_id}/origin_tls_client_auth/{certificate_id}</c>.
    /// </summary>
    /// <param name="certificateId">Certificate identifier.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The certificate Cloudflare removed, or <c>null</c> when it sends no result.</returns>
    Task<ZoneAuthenticatedOriginPull?> DeleteZoneCertificateAsync(
        string certificateId,
        string? zoneId = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class OriginTlsClientAuthService : CloudflareService, IOriginTlsClientAuthService
{
    /// <summary>Creates the service.</summary>
    /// <param name="transport">The transport to execute requests through.</param>
    public OriginTlsClientAuthService(ICloudflareTransport transport)
        : base(transport)
    {
    }

    /// <inheritdoc />
    public async Task<OriginTlsClientAuthSetting> GetSettingsAsync(
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get($"{RootPath(zoneId)}/settings");
        request.OperationName = "Get Origin TLS Client Auth Settings";

        var response = await Transport.SendAsync<OriginTlsClientAuthSetting>(request, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<OriginTlsClientAuthSetting> SetSettingsAsync(
        bool enabled,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var body = enabled ? UpdateOriginTlsClientAuthSettingRequest.Enable() : UpdateOriginTlsClientAuthSettingRequest.Disable();

        var request = CloudflareRequest.Put($"{RootPath(zoneId)}/settings", body);
        request.OperationName = enabled
            ? "Enable Zone Origin TLS Client Auth"
            : "Disable Zone Origin TLS Client Auth";

        var response = await Transport.SendAsync<OriginTlsClientAuthSetting>(request, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<OriginTlsHostnameAssociation?> GetHostnameAsync(
        string hostname,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var name = Require(hostname, nameof(hostname));

        var request = CloudflareRequest
            .Get($"{RootPath(zoneId)}/hostnames/{CloudflareUrl.EncodeSegment(name)}")
            .Tolerate(System.Net.HttpStatusCode.NotFound);
        request.OperationName = "Get Origin TLS Hostname Association";

        var response = await Transport.SendAsync<OriginTlsHostnameAssociation>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OriginTlsHostnameAssociation>> SetHostnamesAsync(
        UpdateOriginTlsHostnameAssociationsRequest request,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();

        var put = CloudflareRequest.Put($"{RootPath(zoneId)}/hostnames", request);
        put.OperationName = "Set Origin TLS Hostname Associations";

        var response = await Transport.SendAsync<List<OriginTlsHostnameAssociation>>(put, cancellationToken).ConfigureAwait(false);
        return response.Result ?? new List<OriginTlsHostnameAssociation>();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OriginTlsHostnameAssociation>> EnableHostnameAsync(
        string hostname,
        string certificateId,
        string? zoneId = null,
        CancellationToken cancellationToken = default) =>
        SetHostnamesAsync(
            UpdateOriginTlsHostnameAssociationsRequest.For(
                OriginTlsHostnameAssociationChange.Enable(
                    Require(hostname, nameof(hostname)),
                    Require(certificateId, nameof(certificateId)))),
            zoneId,
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<OriginTlsHostnameAssociation>> DisableHostnameAsync(
        string hostname,
        string certificateId,
        string? zoneId = null,
        CancellationToken cancellationToken = default) =>
        SetHostnamesAsync(
            UpdateOriginTlsHostnameAssociationsRequest.For(
                OriginTlsHostnameAssociationChange.Disable(
                    Require(hostname, nameof(hostname)),
                    Require(certificateId, nameof(certificateId)))),
            zoneId,
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<OriginTlsHostnameAssociation>> VoidHostnameAsync(
        string hostname,
        string certificateId,
        string? zoneId = null,
        CancellationToken cancellationToken = default) =>
        SetHostnamesAsync(
            UpdateOriginTlsHostnameAssociationsRequest.For(
                OriginTlsHostnameAssociationChange.Void(
                    Require(hostname, nameof(hostname)),
                    Require(certificateId, nameof(certificateId)))),
            zoneId,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<OriginTlsClientCertificate>> ListCertificatesAsync(
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get($"{RootPath(zoneId)}/hostnames/certificates");
        request.OperationName = "List Origin TLS Client Certificates";

        var response = await Transport.SendAsync<List<OriginTlsClientCertificate>>(request, cancellationToken).ConfigureAwait(false);
        return response.Result ?? new List<OriginTlsClientCertificate>();
    }

    /// <inheritdoc />
    public async Task<OriginTlsClientCertificate?> GetCertificateAsync(
        string certificateId,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest
            .Get($"{RootPath(zoneId)}/hostnames/certificates/{CloudflareUrl.EncodeSegment(Require(certificateId, nameof(certificateId)))}")
            .Tolerate(System.Net.HttpStatusCode.NotFound);
        request.OperationName = "Get Origin TLS Client Certificate";

        var response = await Transport.SendAsync<OriginTlsClientCertificate>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<OriginTlsClientCertificate> UploadCertificateAsync(
        UploadOriginTlsCertificateRequest request,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();

        var post = CloudflareRequest.Post($"{RootPath(zoneId)}/hostnames/certificates", request);
        post.OperationName = "Upload Origin TLS Client Certificate";

        var response = await Transport.SendAsync<OriginTlsClientCertificate>(post, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public Task<OriginTlsClientCertificate> UploadCertificateAsync(
        string certificate,
        string privateKey,
        string? zoneId = null,
        CancellationToken cancellationToken = default) =>
        UploadCertificateAsync(
            UploadOriginTlsCertificateRequest.Of(
                Require(certificate, nameof(certificate)),
                Require(privateKey, nameof(privateKey))),
            zoneId,
            cancellationToken);

    /// <inheritdoc />
    public async Task<OriginTlsClientCertificate?> DeleteCertificateAsync(
        string certificateId,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var id = Require(certificateId, nameof(certificateId));

        var request = CloudflareRequest.Delete(
            $"{RootPath(zoneId)}/hostnames/certificates/{CloudflareUrl.EncodeSegment(id)}");
        request.OperationName = "Delete Origin TLS Client Certificate";

        var response = await Transport.SendAsync<OriginTlsClientCertificate>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ZoneAuthenticatedOriginPull>> ListZoneCertificatesAsync(
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get(RootPath(zoneId));
        request.OperationName = "List Zone Origin TLS Certificates";

        var response = await Transport.SendAsync<List<ZoneAuthenticatedOriginPull>>(request, cancellationToken).ConfigureAwait(false);
        return response.Result ?? new List<ZoneAuthenticatedOriginPull>();
    }

    /// <inheritdoc />
    public async Task<ZoneAuthenticatedOriginPull?> GetZoneCertificateAsync(
        string certificateId,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest
            .Get($"{RootPath(zoneId)}/{CloudflareUrl.EncodeSegment(Require(certificateId, nameof(certificateId)))}")
            .Tolerate(System.Net.HttpStatusCode.NotFound);
        request.OperationName = "Get Zone Origin TLS Certificate";

        var response = await Transport.SendAsync<ZoneAuthenticatedOriginPull>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<ZoneAuthenticatedOriginPull> UploadZoneCertificateAsync(
        UploadOriginTlsCertificateRequest request,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();

        var post = CloudflareRequest.Post(RootPath(zoneId), request);
        post.OperationName = "Upload Zone Origin TLS Certificate";

        var response = await Transport.SendAsync<ZoneAuthenticatedOriginPull>(post, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<ZoneAuthenticatedOriginPull?> DeleteZoneCertificateAsync(
        string certificateId,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var id = Require(certificateId, nameof(certificateId));

        var request = CloudflareRequest.Delete($"{RootPath(zoneId)}/{CloudflareUrl.EncodeSegment(id)}");
        request.OperationName = "Delete Zone Origin TLS Certificate";

        var response = await Transport.SendAsync<ZoneAuthenticatedOriginPull>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <summary>Root of the resource, which is a sibling of <c>ssl</c> rather than part of it.</summary>
    private string RootPath(string? zoneId) =>
        $"{ZonePath(ResolveZoneId(zoneId))}/origin_tls_client_auth";
}
