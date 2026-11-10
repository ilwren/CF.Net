using System.Net;
using System.Runtime.CompilerServices;
using CFNet.Http;
using CFNet.Models.OriginCa;

namespace CFNet.Services;

/// <summary>
/// Origin CA certificates: <c>/certificates</c>, at the root of the API — the certificates Cloudflare's own CA
/// signs for an origin server.
/// </summary>
/// <remarks>
/// <para>
/// An Origin CA certificate is free on every plan and is not a public certificate: browsers do not trust it, and
/// it is not meant to be served to them. It sits between Cloudflare's edge and your origin, where
/// <b>Authenticated Origin Pulls</b> or a plain TLS listener can use it, and it is what the dashboard's
/// "Origin Server" tab creates.
/// </para>
/// <para>
/// <b>The path carries neither an account nor a zone</b>, which is unusual here and worth stating plainly:
/// this resource lives at <c>certificates</c> under the API root, and the zone is a <i>filter</i> on the list
/// rather than a segment of the path. The name of the resource suggests
/// <c>accounts/{account_id}/origin_ca_certificates</c>, and a first draft of this wrapper sent exactly that —
/// Cloudflare answered <b>404 <c>[10404] No route for that URI</c></b> to the <c>POST</c> as much as to the
/// <c>GET</c>, while its own generated client and its published documentation both say <c>/certificates</c>. A
/// 404 with <c>10404</c> in it is Cloudflare saying the path does not exist, which is neither a permission nor
/// an entitlement, and no amount of scope on a token fixes it.
/// </para>
/// <para>
/// The private key never comes near this library, which is the point of the design: the caller generates a key
/// and a Certificate Signing Request on the origin, sends the CSR here, and gets a signed certificate back.
/// <c>System.Security.Cryptography.X509Certificates.CertificateRequest</c> writes such a CSR in a few lines, and
/// the integration test does exactly that rather than carrying a fixture around.
/// </para>
/// <para>
/// There is no update, and no status to read: a certificate is signed or it is not, and revoking it takes it out
/// of the list. So the four methods are <see cref="CreateAsync"/>, <see cref="ListAsync"/>,
/// <see cref="GetAsync"/> and <see cref="RevokeAsync"/>.
/// </para>
/// <para>
/// Cloudflare also accepts a separate credential on this path — an <b>Origin CA Key</b>, sent as
/// <c>X-Auth-User-Service-Key</c> — which this library has no notion of, because it sends an API token or the
/// legacy key and email like every other endpoint. Which token permission reaches it is what the integration
/// suite's reach matrix records: it probes this exact path, so a run says whether the configured token can.
/// </para>
/// </remarks>
public interface IOriginCaCertificatesService
{
    /// <summary>Asks Cloudflare's CA to sign a Certificate Signing Request.</summary>
    /// <param name="certificate">The CSR, the hostnames to bind it to, and the kind of key it carries.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The signed certificate, carrying the serial number that identifies it from here on.</returns>
    /// <remarks>
    /// <c>POST /certificates</c>. A <c>keyless-certificate</c> request answers with only the id, the certificate
    /// and the expiry, and needs <see cref="OriginCaCertificateRequest.ZoneId"/>.
    /// </remarks>
    Task<OriginCaCertificate> CreateAsync(
        OriginCaCertificateRequest certificate,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the Origin CA certificates of one zone that have not been revoked.</summary>
    /// <param name="zoneId">The zone to list for, which this endpoint cannot be called without.</param>
    /// <param name="parameters">Paging; Cloudflare's own defaults when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One page of certificates, with the paging Cloudflare reported.</returns>
    /// <remarks>
    /// <c>GET /certificates?zone_id=…</c>. Revoked certificates are not included, which is what makes this the
    /// place to check that a revoke took effect.
    /// </remarks>
    Task<CloudflarePage<OriginCaCertificate>> ListAsync(
        string zoneId,
        OriginCaCertificateListParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every unrevoked Origin CA certificate of one zone, walking the pages.</summary>
    /// <param name="zoneId">The zone to list for, which this endpoint cannot be called without.</param>
    /// <param name="parameters">The page size to walk with; none when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Every certificate, page by page, so a caller can stop early without reading the rest.</returns>
    IAsyncEnumerable<OriginCaCertificate> ListAllAsync(
        string zoneId,
        OriginCaCertificateListParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one certificate by its serial number.</summary>
    /// <param name="certificateId">The certificate's x509 serial number, as <see cref="OriginCaCertificate.Id"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The certificate, or null when Cloudflare has no certificate with that serial number.</returns>
    /// <remarks>
    /// <c>GET /certificates/{certificate_id}</c>. A revoked certificate still answers here — observed live —
    /// while <see cref="ListAsync"/> drops it, so the list is the call that says whether one is still trusted.
    /// </remarks>
    Task<OriginCaCertificate?> GetAsync(
        string certificateId,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes a certificate, which is this resource's delete.</summary>
    /// <param name="certificateId">The certificate's x509 serial number, as <see cref="OriginCaCertificate.Id"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What Cloudflare revoked, and when.</returns>
    /// <remarks>
    /// <c>DELETE /certificates/{certificate_id}</c>. A revoked certificate drops out of the list rather than
    /// changing status, so there is nothing left to read afterwards.
    /// </remarks>
    Task<OriginCaCertificateRevocation> RevokeAsync(
        string certificateId,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IOriginCaCertificatesService" />
public sealed class OriginCaCertificatesService : CloudflareService, IOriginCaCertificatesService
{
    /// <summary>
    /// The path, at the API root. No account id and no zone id in it, which is what makes this resource the one
    /// place in the library where a service takes neither.
    /// </summary>
    private const string CertificatesPath = "certificates";

    /// <summary>Creates the service over a transport.</summary>
    /// <param name="transport">The transport every call goes through.</param>
    public OriginCaCertificatesService(ICloudflareTransport transport)
        : base(transport)
    {
    }

    /// <inheritdoc />
    public async Task<OriginCaCertificate> CreateAsync(
        OriginCaCertificateRequest certificate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        certificate.EnsureValid();

        var request = CloudflareRequest.Post(CertificatesPath, certificate);
        request.OperationName = "Create Origin CA Certificate";

        var response = await Transport
            .SendAsync<OriginCaCertificate>(request, cancellationToken)
            .ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<CloudflarePage<OriginCaCertificate>> ListAsync(
        string zoneId,
        OriginCaCertificateListParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get(CertificatesPath);
        request.Query.Add("zone_id", Require(zoneId, nameof(zoneId)));
        parameters?.Apply(request.Query);
        request.OperationName = "List Origin CA Certificates";

        var response = await Transport
            .SendAsync<List<OriginCaCertificate>>(request, cancellationToken)
            .ConfigureAwait(false);
        return response.ToPage();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<OriginCaCertificate> ListAllAsync(
        string zoneId,
        OriginCaCertificateListParameters? parameters = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var page = 1;

        while (true)
        {
            var effective = new OriginCaCertificateListParameters
            {
                PerPage = parameters?.PerPage,
                Page = page,
            };

            var result = await ListAsync(zoneId, effective, cancellationToken).ConfigureAwait(false);
            foreach (var certificate in result.Items)
            {
                yield return certificate;
            }

            // Stop on an empty page as well as on result_info saying so: not every list reports its paging.
            if (result.Items.Count == 0 || !result.HasMore)
            {
                yield break;
            }

            page++;
        }
    }

    /// <inheritdoc />
    public async Task<OriginCaCertificate?> GetAsync(
        string certificateId,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest
            .Get(CertificatePath(certificateId))
            .Tolerate(HttpStatusCode.NotFound);
        request.OperationName = "Get Origin CA Certificate";

        var response = await Transport
            .SendAsync<OriginCaCertificate>(request, cancellationToken)
            .ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<OriginCaCertificateRevocation> RevokeAsync(
        string certificateId,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Delete(CertificatePath(certificateId));
        request.OperationName = "Revoke Origin CA Certificate";

        var response = await Transport
            .SendAsync<OriginCaCertificateRevocation>(request, cancellationToken)
            .ConfigureAwait(false);
        return response.EnsureResult();
    }

    private static string CertificatePath(string certificateId) =>
        $"{CertificatesPath}/{CloudflareUrl.EncodeSegment(Require(certificateId, nameof(certificateId)))}";
}
