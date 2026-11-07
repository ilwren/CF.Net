using System.Net;
using CFNet.Http;
using CFNet.Models.MtlsCertificates;

namespace CFNet.Services;

/// <summary>
/// The mTLS certificates uploaded to an account: a CA Cloudflare should check client certificates against, or
/// a leaf certificate a Cloudflare service should present.
/// </summary>
/// <remarks>
/// <para>
/// These are the certificates an account brings, which is why the resource is called Bring Your Own CA in the
/// dashboard. Certificates <em>issued</em> by Cloudflare's managed CA are a different resource on a different
/// path and are not covered here.
/// </para>
/// <para>
/// <b>The permission group is not the one the name suggests.</b> Cloudflare has two groups called SSL and
/// Certificates: a zone-scoped one, which covers a zone's certificate packs, Universal SSL, custom hostnames
/// and the Origin CA certificates, and an <b>account-scoped</b> one whose own description is "SSL MTLS
/// certificates or Certificate Store". These endpoints want the account-scoped one -
/// <c>Account: SSL and Certificates</c> Read (<c>a7a233f9604845c787d4c8c39ac09c21</c>) or Write
/// (<c>db37e5f1cb1a4e1aabaef8deaea43575</c>) - and a token holding only the zone-scoped group is refused here
/// with a 403 that reads as though the group were missing when it is present under a different id.
/// </para>
/// <para>
/// The list is a bare array with no paging, so there is no walk-every-page method to offer: an account's
/// uploaded certificates are a handful at most, and Cloudflare does not publish page parameters for this path.
/// </para>
/// </remarks>
public interface IMtlsCertificateService
{
    /// <summary>Lists the mTLS certificates uploaded to an account.</summary>
    /// <param name="parameters">A filter by certificate type; all of them when null.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Every certificate the filter matched.</returns>
    /// <remarks><c>GET /accounts/{account_id}/mtls_certificates</c>, a bare array with no paging.</remarks>
    Task<IReadOnlyList<MtlsCertificate>> ListAsync(
        MtlsCertificateListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one uploaded certificate.</summary>
    /// <param name="certificateId">The certificate's id.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The certificate, or null when the account has none of that id.</returns>
    /// <remarks><c>GET /accounts/{account_id}/mtls_certificates/{mtls_certificate_id}</c>.</remarks>
    Task<MtlsCertificate?> GetAsync(
        string certificateId,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Uploads a certificate.</summary>
    /// <param name="certificate">What to upload; <see cref="MtlsCertificateRequest.EnsureValid" /> is applied first.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The certificate as Cloudflare stored it, with the id it assigned and the dates it read out of it.</returns>
    /// <remarks>
    /// <c>POST /accounts/{account_id}/mtls_certificates</c>. This answer, and only this one, carries
    /// <c>updated_at</c>.
    /// </remarks>
    Task<MtlsCertificate> CreateAsync(
        MtlsCertificateRequest certificate,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Removes an uploaded certificate.</summary>
    /// <param name="certificateId">The certificate's id.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The certificate that was removed, or null when there was none of that id to remove.</returns>
    /// <remarks>
    /// <c>DELETE /accounts/{account_id}/mtls_certificates/{mtls_certificate_id}</c>. Cloudflare answers the
    /// certificate it removed rather than only its id, which is what makes the answer worth returning.
    /// </remarks>
    Task<MtlsCertificate?> DeleteAsync(
        string certificateId,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the services a certificate is in use by.</summary>
    /// <param name="certificateId">The certificate's id.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One entry per service using the certificate, empty when nothing is.</returns>
    /// <remarks>
    /// <c>GET /accounts/{account_id}/mtls_certificates/{mtls_certificate_id}/associations</c>. This is the call
    /// to make before removing a certificate, because it is what says whether removing it would break a
    /// hostname or an Access application - and Cloudflare publishes neither the service names nor the statuses
    /// it answers with, so both are kept as they arrive.
    /// </remarks>
    Task<IReadOnlyList<MtlsCertificateAssociation>> ListAssociationsAsync(
        string certificateId,
        string? accountId = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class MtlsCertificatesService : CloudflareService, IMtlsCertificateService
{
    /// <summary>Creates the service over a transport.</summary>
    /// <param name="transport">The transport that carries the call.</param>
    public MtlsCertificatesService(ICloudflareTransport transport)
        : base(transport)
    {
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MtlsCertificate>> ListAsync(
        MtlsCertificateListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get(CertificatesPath(accountId));
        parameters?.Apply(request.Query);
        request.OperationName = "List mTLS Certificates";

        var response = await Transport
            .SendAsync<List<MtlsCertificate>>(request, cancellationToken)
            .ConfigureAwait(false);

        return response.Result ?? new List<MtlsCertificate>();
    }

    /// <inheritdoc />
    public async Task<MtlsCertificate?> GetAsync(
        string certificateId,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest
            .Get(CertificatePath(accountId, certificateId))
            .Tolerate(HttpStatusCode.NotFound);
        request.OperationName = "Get mTLS Certificate";

        var response = await Transport
            .SendAsync<MtlsCertificate>(request, cancellationToken)
            .ConfigureAwait(false);

        return response.Result;
    }

    /// <inheritdoc />
    public async Task<MtlsCertificate> CreateAsync(
        MtlsCertificateRequest certificate,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        certificate.EnsureValid();

        var request = CloudflareRequest.Post(CertificatesPath(accountId), certificate);
        request.OperationName = "Upload mTLS Certificate";

        var response = await Transport
            .SendAsync<MtlsCertificate>(request, cancellationToken)
            .ConfigureAwait(false);

        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<MtlsCertificate?> DeleteAsync(
        string certificateId,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest
            .Delete(CertificatePath(accountId, certificateId))
            .Tolerate(HttpStatusCode.NotFound);
        request.OperationName = "Delete mTLS Certificate";

        var response = await Transport
            .SendAsync<MtlsCertificate>(request, cancellationToken)
            .ConfigureAwait(false);

        return response.Result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MtlsCertificateAssociation>> ListAssociationsAsync(
        string certificateId,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get($"{CertificatePath(accountId, certificateId)}/associations");
        request.OperationName = "List mTLS Certificate Associations";

        var response = await Transport
            .SendAsync<List<MtlsCertificateAssociation>>(request, cancellationToken)
            .ConfigureAwait(false);

        return response.Result ?? new List<MtlsCertificateAssociation>();
    }

    private string CertificatesPath(string? accountId) =>
        $"{AccountPath(ResolveAccountId(accountId))}/mtls_certificates";

    private string CertificatePath(string? accountId, string certificateId) =>
        $"{CertificatesPath(accountId)}/{CloudflareUrl.EncodeSegment(Require(certificateId, nameof(certificateId)))}";
}
