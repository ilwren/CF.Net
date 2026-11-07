using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Http;
using CFNet.Serialization;

namespace CFNet.Models.OriginCa;

/// <summary>
/// An Origin CA certificate: a certificate Cloudflare's own certificate authority signs for an origin server,
/// so that traffic between the edge and the origin is encrypted without buying a certificate from a public CA.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="Id"/> is the certificate's x509 serial number</b>, not a UUID — Cloudflare documents it as
/// such, and it is what the read and the revoke both take.
/// </para>
/// <para>
/// <b><see cref="Certificate"/> and <see cref="Csr"/> are newline-encoded PEM</b>: a PEM block whose lines are
/// separated by <c>\n</c>, which JSON escaping handles on the way out. The certificate may carry the issuing CA
/// behind it, so it can be more than one block.
/// </para>
/// <para>
/// <b>There is nothing to update.</b> A certificate is signed or it is not, so this resource has four methods —
/// create, list, read and revoke — and a revoked certificate drops out of the list rather than carrying a
/// status, which is what makes the list the honest place to check that a revoke happened.
/// </para>
/// </remarks>
public sealed class OriginCaCertificate
{
    /// <summary>The certificate's x509 serial number, which is what identifies it to every other method.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>The signed certificate, newline-encoded PEM, and the issuing CA behind it when there is one.</summary>
    [JsonPropertyName("certificate")]
    public string? Certificate { get; set; }

    /// <summary>The Certificate Signing Request this certificate was made from, newline-encoded PEM.</summary>
    [JsonPropertyName("csr")]
    public string? Csr { get; set; }

    /// <summary>When the certificate expires, exactly as Cloudflare wrote it.</summary>
    [JsonPropertyName("expires_on")]
    public string? ExpiresOn { get; set; }

    /// <summary>The hostnames the certificate covers.</summary>
    [JsonPropertyName("hostnames")]
    public List<string>? Hostnames { get; set; }

    /// <summary>The kind of key the certificate was signed for.</summary>
    [JsonPropertyName("request_type")]
    public OriginCaCertificateRequestType? RequestType { get; set; }

    /// <summary>How many days the certificate was asked to be valid for.</summary>
    [JsonPropertyName("requested_validity")]
    public int? RequestedValidity { get; set; }

    /// <summary>Fields Cloudflare answers that this version of the library does not model.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary><see cref="ExpiresOn"/> parsed, or null when Cloudflare did not answer one it could parse.</summary>
    [JsonIgnore]
    public DateTimeOffset? ExpiresOnUtc => CloudflareTimestamps.Read(ExpiresOn);

    /// <summary>Names the certificate, its hostnames and how long it was asked for.</summary>
    /// <returns>A one-line description of the certificate.</returns>
    public override string ToString()
    {
        var hosts = Hostnames is { Count: > 0 } hostList ? string.Join(", ", hostList) : "no hostnames";
        var validity = RequestedValidity is { } days ? $"{days} day(s)" : "an unpublished validity";
        var expires = ExpiresOn is { Length: > 0 } stamp ? $", expires {stamp}" : string.Empty;
        return $"origin CA certificate {Id ?? "(no id yet)"} for {hosts}, {validity}{expires}";
    }
}

/// <summary>The kind of key an Origin CA certificate is signed for.</summary>
[JsonConverter(typeof(CloudflareEnumConverter<OriginCaCertificateRequestType>))]
public enum OriginCaCertificateRequestType
{
    /// <summary>Value Cloudflare returned that this version of the library does not know.</summary>
    Unknown = 0,

    /// <summary>An RSA key on the origin.</summary>
    [CloudflareValue("origin-rsa")]
    OriginRsa = 1,

    /// <summary>An ECDSA key on the origin.</summary>
    [CloudflareValue("origin-ecc")]
    OriginEcc = 2,

    /// <summary>A Keyless SSL server, which holds the key somewhere else entirely.</summary>
    [CloudflareValue("keyless-certificate")]
    KeylessCertificate = 3,
}

/// <summary>
/// The day counts Cloudflare publishes for <c>requested_validity</c>.
/// </summary>
/// <remarks>
/// These are constants rather than an enum on purpose: the published set has changed over the life of this
/// endpoint — older documentation offered 2190, 3650 and 7300 days, and none of the short ones — so a library
/// that refused a number outside today's list would refuse a request Cloudflare would accept. Send one of these
/// and you are sending something published; send another and Cloudflare decides.
/// </remarks>
public static class OriginCaCertificateValidities
{
    /// <summary>A week.</summary>
    public const int SevenDays = 7;

    /// <summary>A month.</summary>
    public const int ThirtyDays = 30;

    /// <summary>Three months.</summary>
    public const int NinetyDays = 90;

    /// <summary>A year.</summary>
    public const int OneYear = 365;

    /// <summary>Two years.</summary>
    public const int TwoYears = 730;

    /// <summary>Three years.</summary>
    public const int ThreeYears = 1095;

    /// <summary>Fifteen years, which is the longest Cloudflare publishes today.</summary>
    public const int FifteenYears = 5475;
}

/// <summary>The body of <c>POST /accounts/{account_id}/origin_ca_certificates</c>.</summary>
/// <remarks>
/// The CSR is the caller's: this library does not hold a private key and does not generate one, because the
/// whole point of an Origin CA certificate is that the key never leaves the origin. <c>CertificateRequest</c> in
/// <c>System.Security.Cryptography</c> writes one — <c>CreateSigningRequest()</c>, then
/// <c>PemEncoding.Write("CERTIFICATE REQUEST", …)</c> — and the integration test does exactly that.
/// </remarks>
public sealed class OriginCaCertificateRequest
{
    /// <summary>The Certificate Signing Request, newline-encoded PEM.</summary>
    [JsonPropertyName("csr")]
    public string? Csr { get; set; }

    /// <summary>
    /// The hostnames to bind the certificate to. They have to be fully qualified names belonging to zones on
    /// the account, and a wildcard is only allowed as a single <c>*.</c> prefix over a multi-label domain.
    /// </summary>
    [JsonPropertyName("hostnames")]
    public List<string>? Hostnames { get; set; }

    /// <summary>The kind of key the certificate is for.</summary>
    [JsonPropertyName("request_type")]
    public OriginCaCertificateRequestType? RequestType { get; set; }

    /// <summary>
    /// How many days the certificate should be valid for; one of <see cref="OriginCaCertificateValidities"/>
    /// when you want a published number. Cloudflare picks its own default when this is left out.
    /// </summary>
    [JsonPropertyName("requested_validity")]
    public int? RequestedValidity { get; set; }

    /// <summary>
    /// The zone whose Keyless servers the certificate is for. Required when
    /// <see cref="RequestType"/> is <see cref="OriginCaCertificateRequestType.KeylessCertificate"/> and ignored
    /// otherwise — and when it is required, the answer carries only the id, the certificate and the expiry.
    /// </summary>
    [JsonPropertyName("zone_id")]
    public string? ZoneId { get; set; }

    /// <summary>Builds a request for an origin holding an RSA key.</summary>
    /// <param name="csr">The Certificate Signing Request, in whatever line endings it was written with.</param>
    /// <param name="hostnames">The hostnames to bind the certificate to.</param>
    /// <param name="requestedValidity">Days of validity; Cloudflare's own default when null.</param>
    /// <returns>A request ready to send.</returns>
    public static OriginCaCertificateRequest ForRsa(
        string csr,
        IEnumerable<string> hostnames,
        int? requestedValidity = null) =>
        Build(csr, hostnames, OriginCaCertificateRequestType.OriginRsa, requestedValidity);

    /// <summary>Builds a request for an origin holding an ECDSA key.</summary>
    /// <param name="csr">The Certificate Signing Request, in whatever line endings it was written with.</param>
    /// <param name="hostnames">The hostnames to bind the certificate to.</param>
    /// <param name="requestedValidity">Days of validity; Cloudflare's own default when null.</param>
    /// <returns>A request ready to send.</returns>
    public static OriginCaCertificateRequest ForEcc(
        string csr,
        IEnumerable<string> hostnames,
        int? requestedValidity = null) =>
        Build(csr, hostnames, OriginCaCertificateRequestType.OriginEcc, requestedValidity);

    /// <summary>
    /// Builds a request for a Keyless SSL server, where the private key is not on the origin at all.
    /// </summary>
    /// <param name="csr">The Certificate Signing Request, in whatever line endings it was written with.</param>
    /// <param name="hostnames">The hostnames to bind the certificate to.</param>
    /// <param name="zoneId">The zone whose keyless servers this is for, which this request type cannot do
    /// without.</param>
    /// <param name="requestedValidity">Days of validity; Cloudflare's own default when null.</param>
    /// <returns>A request ready to send.</returns>
    public static OriginCaCertificateRequest ForKeyless(
        string csr,
        IEnumerable<string> hostnames,
        string zoneId,
        int? requestedValidity = null)
    {
        ArgumentNullException.ThrowIfNull(zoneId);

        var request = Build(csr, hostnames, OriginCaCertificateRequestType.KeylessCertificate, requestedValidity);
        request.ZoneId = zoneId;
        return request;
    }

    private static OriginCaCertificateRequest Build(
        string csr,
        IEnumerable<string> hostnames,
        OriginCaCertificateRequestType requestType,
        int? requestedValidity)
    {
        ArgumentNullException.ThrowIfNull(csr);
        ArgumentNullException.ThrowIfNull(hostnames);

        return new OriginCaCertificateRequest
        {
            // Cloudflare asks for newline-encoded PEM, and a CSR written on Windows carries CRLF, so the line
            // endings are settled here rather than left for Cloudflare to refuse.
            Csr = csr.Replace("\r\n", "\n", StringComparison.Ordinal),
            Hostnames = hostnames.ToList(),
            RequestType = requestType,
            RequestedValidity = requestedValidity,
        };
    }

    /// <summary>
    /// Refuses a request Cloudflare would refuse, naming everything wrong with it at once — and settles the
    /// CSR's line endings on the way through, because Cloudflare asks for newline-encoded PEM.
    /// </summary>
    /// <exception cref="CFNet.Exceptions.CloudflareConfigurationException">
    /// Thrown when there is no CSR, no hostname, a hostname whose wildcard grammar Cloudflare documents as
    /// unacceptable, no request type, a keyless request with no zone, or a validity that is not a positive
    /// number; its <c>Problems</c> lists every reason.
    /// </exception>
    public void EnsureValid()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(Csr))
        {
            problems.Add(
                "a certificate request needs a CSR: Cloudflare signs the key inside it and holds no key of its " +
                "own, so there is nothing to sign without one.");
        }
        else
        {
            Csr = Csr.Replace("\r\n", "\n", StringComparison.Ordinal);

            if (!Csr.Contains("BEGIN", StringComparison.Ordinal))
            {
                problems.Add(
                    "a CSR is PEM: it carries a 'BEGIN CERTIFICATE REQUEST' line. What is here does not, so it " +
                    "is either DER bytes or a certificate rather than a request for one.");
            }
        }

        if (Hostnames is not { Count: > 0 })
        {
            problems.Add("a certificate needs at least one hostname to be for.");
        }
        else
        {
            foreach (var hostname in Hostnames)
            {
                var problem = HostnameProblem(hostname);
                if (problem is not null)
                {
                    problems.Add(problem);
                }
            }
        }

        if (RequestType is null or OriginCaCertificateRequestType.Unknown)
        {
            problems.Add(
                "a certificate request needs a request type: origin-rsa, origin-ecc or keyless-certificate, " +
                "which is the kind of key the certificate is being signed for.");
        }

        if (RequestType == OriginCaCertificateRequestType.KeylessCertificate && string.IsNullOrWhiteSpace(ZoneId))
        {
            problems.Add(
                "a keyless-certificate request needs the zone whose keyless servers it is for: Cloudflare " +
                "documents zone_id as required for that request type and ignores it for the others.");
        }

        if (RequestedValidity is { } days && days <= 0)
        {
            problems.Add(
                $"a validity of {days} day(s) is not a length of time: Cloudflare publishes " +
                $"{string.Join(", ", PublishedValidities())} and picks its own default when the field is left " +
                "out, but it will not sign a certificate that has already expired.");
        }

        if (problems.Count > 0)
        {
            throw new CFNet.Exceptions.CloudflareConfigurationException(problems);
        }
    }

    /// <summary>The published day counts, in the order Cloudflare lists them.</summary>
    /// <returns>Every number in <see cref="OriginCaCertificateValidities"/>.</returns>
    public static IReadOnlyList<int> PublishedValidities() => new[]
    {
        OriginCaCertificateValidities.SevenDays,
        OriginCaCertificateValidities.ThirtyDays,
        OriginCaCertificateValidities.NinetyDays,
        OriginCaCertificateValidities.OneYear,
        OriginCaCertificateValidities.TwoYears,
        OriginCaCertificateValidities.ThreeYears,
        OriginCaCertificateValidities.FifteenYears,
    };

    /// <summary>
    /// Says what is wrong with one hostname, by the grammar Cloudflare publishes: fully qualified, belonging to
    /// a zone on the account (which cannot be checked here), and wildcards only as a single <c>*.</c> prefix
    /// over a multi-label domain — so <c>*.example.com</c> is acceptable while <c>*.*.example.com</c>,
    /// <c>foo.*.example.com</c> and <c>*.com</c> are not.
    /// </summary>
    /// <param name="hostname">The hostname as a caller wrote it.</param>
    /// <returns>What is wrong with it, or null when the grammar is one Cloudflare accepts.</returns>
    private static string? HostnameProblem(string? hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname))
        {
            return "a certificate hostname cannot be empty: it is the name the certificate is for.";
        }

        if (hostname.Any(char.IsWhiteSpace))
        {
            return $"the hostname '{hostname}' has whitespace in it, which no DNS name does.";
        }

        if (hostname.StartsWith('.') || hostname.EndsWith('.'))
        {
            return
                $"the hostname '{hostname}' starts or ends with a dot: Cloudflare wants a fully qualified name " +
                "written the way a browser would, without the trailing root label.";
        }

        var wildcards = hostname.Count(character => character == '*');
        if (wildcards > 1)
        {
            return
                $"the hostname '{hostname}' carries {wildcards} wildcards, and Cloudflare documents a single " +
                "'*.' prefix as the only wildcard it accepts.";
        }

        if (wildcards == 1)
        {
            if (!hostname.StartsWith("*.", StringComparison.Ordinal))
            {
                return
                    $"the hostname '{hostname}' has its wildcard somewhere other than the front: Cloudflare " +
                    "refuses an interior wildcard such as 'foo.*.example.com'.";
            }

            var suffix = hostname[2..];
            if (!suffix.Contains('.'))
            {
                return
                    $"the hostname '{hostname}' wildcards a single label ('{suffix}'), and Cloudflare wants the " +
                    "part after '*.' to be a multi-label domain: '*.example.com' is acceptable, '*.com' is not.";
            }
        }
        else if (!hostname.Contains('.'))
        {
            return
                $"the hostname '{hostname}' has no dot in it, so it is not a fully qualified name belonging to " +
                "a zone.";
        }

        return null;
    }
}

/// <summary>What <c>DELETE /accounts/{account_id}/origin_ca_certificates/{certificate_id}</c> answers.</summary>
/// <remarks>
/// A revoke is not a delete in the usual sense: the certificate's serial number stops being trusted, and the
/// answer says when that happened.
/// </remarks>
public sealed class OriginCaCertificateRevocation
{
    /// <summary>The serial number of the certificate that was revoked.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>When the revocation happened, exactly as Cloudflare wrote it.</summary>
    [JsonPropertyName("revoked_at")]
    public string? RevokedAt { get; set; }

    /// <summary>Fields Cloudflare answers that this version of the library does not model.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary><see cref="RevokedAt"/> parsed, or null when Cloudflare did not answer one it could parse.</summary>
    [JsonIgnore]
    public DateTimeOffset? RevokedAtUtc => CloudflareTimestamps.Read(RevokedAt);

    /// <summary>Names the revocation and when it happened.</summary>
    /// <returns>A one-line description of the revocation.</returns>
    public override string ToString()
    {
        var when = RevokedAt is { Length: > 0 } stamp ? $" at {stamp}" : string.Empty;
        return $"revoked origin CA certificate {Id ?? "(no id)"}{when}";
    }
}

/// <summary>
/// Query parameters for <c>GET /accounts/{account_id}/origin_ca_certificates</c>, which pages by page and
/// per_page and cannot be called without a zone.
/// </summary>
/// <remarks>
/// <c>per_page</c> is bounded by this endpoint more tightly than most: Cloudflare documents 5 as its floor and
/// 50 as its cap.
/// </remarks>
public sealed class OriginCaCertificateListParameters : CloudflarePagedParameters
{
}
