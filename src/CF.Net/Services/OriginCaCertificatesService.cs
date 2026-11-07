using System.Net;
using System.Runtime.CompilerServices;
using CFNet.Http;
using CFNet.Models.OriginCa;

namespace CFNet.Services;

/// <summary>
/// Origin CA certificates: <c>/accounts/{account_id}/origin_ca_certificates</c>, the certificates Cloudflare's
/// own CA signs for an origin server.
/// </summary>
/// <remarks>
/// <para>
/// An Origin CA certificate is free on every plan and is not a public certificate: browsers do not trust it, and
/// it is not meant to be served to them. It sits between Cloudflare's edge and your origin, where
/// <b>Authenticated Origin Pulls</b> or a plain TLS listener can use it, and it is what the dashboard's
/// "Origin Server" tab creates.
/// </para>
/// <para>
/// The private key never comes near this library, which is the point of the design: the caller generates a key
/// and a Certificate Signing Request on the origin, sends the CSR here, and gets a signed certificate back.
/// <c>System.Security.Cryptography.CertificateRequest</c> writes such a CSR in a few lines, and the integration
/// test does exactly that rather than carrying a fixture around.
/// </para>
/// <para>
/// There is no update, and no status to read: a certificate is signed or it is not, and revoking it takes it out
/// of the list. So the four methods are <see cref="CreateAsync"/>, <see cref="ListAsync"/>,
/// <see cref="GetAsync"/> and <see cref="RevokeAsync"/>.
/// </para>
/// <para>
/// Cloudflare also accepts a separate credential on this path — an <b>Origin CA Key</b>, sent as
/// <c>X-Auth-User-Service-Key</c> — which this library has no notion of, because it sends an API token or the
/// legacy key and email like every other endpoint. With a token, the SSL and Certificates permissions are what
/// reach certificates, and the integration suite's reach matrix probes this exact path so a run says which one
/// the configured token actually has.
/// </para>
/// </remarks>
public interface IOriginCaCertificatesService
{
    /// <summary>Asks Cloudflare's CA to sign a Certificate Signing Request.</summary>
    /// <param name="certificate">The CSR, the hostnames to bind it to, and the kind of key it carries.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The signed certificate, carrying the serial number that identifies it from here on.</returns>
    /// <remarks>
    /// <c>POST /accounts/{account_id}/origin_ca_certificates</c>. A <c>keyless-certificate</c> request answers
    /// with only the id, the certificate and the expiry, and needs <see cref="OriginCaCertificateRequest.ZoneId"/>.
    /// </remarks>
    Task<OriginCaCertificate> CreateAsync(
        OriginCaCertificateRequest certificate,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the Origin CA certificates of one zone that have not been revoked.</summary>
    /// <param name="zoneId">The zone to list for, which this endpoint cannot be called without.</param>
    /// <param name="parameters">Paging; Cloudflare's own defaults when null.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One page of certificates, with the paging Cloudflare reported.</returns>
    /// <remarks>
    /// <c>GET /accounts/{account_id}/origin_ca_certificates?zone_id=…</c>. Revoked certificates are not
    /// included, which is what makes this the place to check that a revoke took effect.
    /// </remarks>
    Task<CloudflarePage<OriginCaCertificate>> ListAsync(
        string zoneId,
        OriginCaCertificateListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every unrevoked Origin CA certificate of one zone, walking the pages.</summary>
    /// <param name="zoneId">The zone to list for, which this endpoint cannot be called without.</param>
    /// <param name="parameters">The page size to walk with; none when null.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Every certificate, page by page, so a caller can stop early without reading the rest.</returns>
    IAsyncEnumerable<OriginCaCertificate> ListAllAsync(
        string zoneId,
        OriginCaCertificateListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one certificate by its serial number.</summary>
    /// <param name="certificateId">The certificate's x509 serial number, as <see cref="OriginCaCertificate.Id"/>.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The certificate, or null when the account has no unrevoked certificate with that serial number.</returns>
    /// <remarks><c>GET /accounts/{account_id}/origin_ca_certificates/{certificate_id}</c>.</remarks>
    Task<OriginCaCertificate?> GetAsync(
        string certificateId,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes a certificate, which is this resource's delete.</summary>
    /// <param name="certificateId">The certificate's x509 serial number, as <see cref="OriginCaCertificate.Id"/>.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What Cloudflare revoked, and when.</returns>
    /// <remarks>
    /// <c>DELETE /accounts/{account_id}/origin_ca_certificates/{certificate_id}</c>. A revoked certificate drops
    /// out of the list rather than changing status, so there is nothing left to read afterwards.
    /// </remarks>
    Task<OriginCaCertificateRevocation> RevokeAsync(
        string certificateId,
        string? accountId = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IOriginCaCertificatesService" />
public sealed class OriginCaCertificatesService : CloudflareService, IOriginCaCertificatesService
{
    /// <summary>Creates the service over a transport.</summary>
    /// <param name="transport">The transport every call goes through.</param>
    public OriginCaCertificatesService(ICloudflareTransport transport)
        : base(transport)
    {
    }

    /// <inheritdoc />
    public async Task<OriginCaCertificate> CreateAsync(
        OriginCaCertificateRequest certificate,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        certificate.EnsureValid();

        var request = CloudflareRequest.Post(CertificatesPath(ResolveAccountId(accountId)), certificate);
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
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get(CertificatesPath(ResolveAccountId(accountId)));
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
        string? accountId = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var account = ResolveAccountId(accountId);
        var page = 1;

        while (true)
        {
            var effective = new OriginCaCertificateListParameters
            {
                PerPage = parameters?.PerPage,
                Page = page,
            };

            var result = await ListAsync(zoneId, effective, account, cancellationToken).ConfigureAwait(false);
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
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest
            .Get(CertificatePath(ResolveAccountId(accountId), certificateId))
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
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Delete(CertificatePath(ResolveAccountId(accountId), certificateId));
        request.OperationName = "Revoke Origin CA Certificate";

        var response = await Transport
            .SendAsync<OriginCaCertificateRevocation>(request, cancellationToken)
            .ConfigureAwait(false);
        return response.EnsureResult();
    }

    private static string CertificatesPath(string accountId) =>
        $"{AccountPath(accountId)}/origin_ca_certificates";

    private static string CertificatePath(string accountId, string certificateId) =>
        $"{CertificatesPath(accountId)}/{CloudflareUrl.EncodeSegment(Require(certificateId, nameof(certificateId)))}";
}
