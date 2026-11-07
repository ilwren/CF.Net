using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Http;
using CFNet.Serialization;

namespace CFNet.Models.MtlsCertificates;

/// <summary>
/// A certificate uploaded to an account for use with Cloudflare's mTLS-enabled services - Bring Your Own CA
/// being the ordinary case, where Cloudflare checks a client certificate against a CA the account uploaded
/// rather than against one it issued.
/// </summary>
/// <remarks>
/// <para>
/// Certificates <em>issued by</em> Cloudflare's managed CA are a different resource on a different path: those
/// are client certificates, and this type never describes one.
/// </para>
/// <para>
/// The create answer carries one field the read and list answers do not - <see cref="UpdatedAt" /> - so all of
/// them are declared here and stay null where Cloudflare does not send them. <see cref="Certificates" /> comes
/// back as it went in, which means an account's own certificate material is readable through this API by
/// anybody holding a token with the group; that is Cloudflare's behaviour and not something a client can
/// narrow.
/// </para>
/// </remarks>
public sealed class MtlsCertificate
{
    /// <summary>Certificate identifier tag, up to 36 characters.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Whether this is a CA certificate or a leaf one.</summary>
    [JsonPropertyName("ca")]
    public bool? Ca { get; set; }

    /// <summary>The uploaded certificate material, as PEM.</summary>
    [JsonPropertyName("certificates")]
    public string? Certificates { get; set; }

    /// <summary>When the certificate expires, as Cloudflare wrote it.</summary>
    [JsonPropertyName("expires_on")]
    public string? ExpiresOn { get; set; }

    /// <summary>The certificate authority that issued it.</summary>
    [JsonPropertyName("issuer")]
    public string? Issuer { get; set; }

    /// <summary>The name given at upload, which exists for readability and for nothing else.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>The certificate's serial number, as Cloudflare prints it.</summary>
    [JsonPropertyName("serial_number")]
    public string? SerialNumber { get; set; }

    /// <summary>The hash used to sign the certificate - <c>SHA256</c>, <c>SHA1</c> and the like.</summary>
    [JsonPropertyName("signature")]
    public string? Signature { get; set; }

    /// <summary>Who manages the certificate: uploaded by the account, or managed by a Cloudflare service.</summary>
    [JsonPropertyName("type")]
    public MtlsCertificateType? Type { get; set; }

    /// <summary>When it was uploaded, as Cloudflare wrote it.</summary>
    [JsonPropertyName("uploaded_on")]
    public string? UploadedOn { get; set; }

    /// <summary>
    /// Only the create answer carries this one; the read and list answers do not, so it stays null there.
    /// </summary>
    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }

    /// <summary>Anything else Cloudflare sent, readable as it arrived rather than guessed at.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>True when this is a CA certificate, which is what an mTLS check is performed against.</summary>
    [JsonIgnore]
    public bool IsCertificateAuthority => Ca == true;

    /// <summary><see cref="ExpiresOn" /> parsed, or null when it is missing or in a shape this cannot read.</summary>
    [JsonIgnore]
    public DateTimeOffset? ExpiresOnUtc => CloudflareTimestamps.Read(ExpiresOn);

    /// <summary><see cref="UploadedOn" /> parsed, or null when it is missing or unreadable.</summary>
    [JsonIgnore]
    public DateTimeOffset? UploadedOnUtc => CloudflareTimestamps.Read(UploadedOn);

    /// <summary><see cref="UpdatedAt" /> parsed, or null when it is missing or unreadable.</summary>
    [JsonIgnore]
    public DateTimeOffset? UpdatedAtUtc => CloudflareTimestamps.Read(UpdatedAt);

    /// <summary>Names the certificate by what it is and when it stops being usable.</summary>
    /// <returns>A one-line description of the certificate.</returns>
    public override string ToString()
    {
        var name = Name is { Length: > 0 } given ? $" ({given})" : string.Empty;
        var kind = Type is { } type && type != MtlsCertificateType.Unknown
            ? CloudflareEnumConverter<MtlsCertificateType>.ToWire(type)
            : "an unpublished type";
        var role = Ca == true ? "CA" : Ca == false ? "leaf" : "unknown role";
        return $"mTLS certificate {Id ?? "(no id)"}{name}: {role} {kind}, expires {ExpiresOn ?? "(no expiry)"}";
    }
}

/// <summary>Who manages an mTLS certificate.</summary>
[JsonConverter(typeof(CloudflareEnumConverter<MtlsCertificateType>))]
public enum MtlsCertificateType
{
    /// <summary>A value this version of the library has not met. Reading one is tolerated; sending one is not.</summary>
    Unknown = 0,

    /// <summary>Uploaded by the account, which is what <c>POST</c> makes.</summary>
    [CloudflareValue("custom")]
    Custom = 1,

    /// <summary>Managed by Cloudflare Gateway.</summary>
    [CloudflareValue("gateway_managed")]
    GatewayManaged = 2,

    /// <summary>Managed by Cloudflare Access.</summary>
    [CloudflareValue("access_managed")]
    AccessManaged = 3,
}

/// <summary>
/// A certificate to upload. <c>ca</c> and <c>certificates</c> are both required by Cloudflare; a private key is
/// only wanted for the cases that need one, such as a custom certificate on Zero Trust's block page.
/// </summary>
public sealed class MtlsCertificateRequest
{
    /// <summary>True for a CA certificate, false for a leaf one. Required.</summary>
    [JsonPropertyName("ca")]
    public bool? Ca { get; set; }

    /// <summary>The certificate material as PEM - a root CA, or a chain. Required.</summary>
    [JsonPropertyName("certificates")]
    public string? Certificates { get; set; }

    /// <summary>A unique name, for readability only; none when null.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// The private key as PEM, for the few cases that need one. Leave null for an ordinary BYO-CA upload:
    /// Cloudflare validates client certificates against the CA and never needs the CA's key.
    /// </summary>
    [JsonPropertyName("private_key")]
    public string? PrivateKey { get; set; }

    /// <summary>Builds an upload of a CA certificate, which is what mTLS verification is performed against.</summary>
    /// <param name="certificates">The CA certificate as PEM.</param>
    /// <param name="name">A name for it; none when null.</param>
    /// <returns>A request ready to validate and send.</returns>
    public static MtlsCertificateRequest ForCertificateAuthority(string certificates, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(certificates);

        return new MtlsCertificateRequest { Ca = true, Certificates = certificates, Name = name };
    }

    /// <summary>
    /// Builds an upload of a leaf certificate and its key, for the services that present a certificate rather
    /// than check one.
    /// </summary>
    /// <param name="certificates">The certificate, or chain, as PEM.</param>
    /// <param name="privateKey">The matching private key as PEM.</param>
    /// <param name="name">A name for it; none when null.</param>
    /// <returns>A request ready to validate and send.</returns>
    public static MtlsCertificateRequest ForLeafCertificate(string certificates, string privateKey, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(certificates);
        ArgumentNullException.ThrowIfNull(privateKey);

        return new MtlsCertificateRequest
        {
            Ca = false,
            Certificates = certificates,
            PrivateKey = privateKey,
            Name = name,
        };
    }

    /// <summary>
    /// Reports every reason this request would be refused, and settles the line endings in the PEM on the way
    /// through.
    /// </summary>
    /// <exception cref="CFNet.Exceptions.CloudflareConfigurationException">
    /// Thrown with all of the problems at once, so a caller fixes the request in one pass rather than one
    /// refusal per attempt.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>This method changes the request.</b> Windows line endings in <see cref="Certificates" /> and
    /// <see cref="PrivateKey" /> are rewritten to <c>\n</c>, deliberately and for the same reason the Origin CA
    /// request does it: PEM is a text format whose lines Cloudflare parses, a <c>\r</c> inside one is invisible
    /// in a log and tedious to diagnose, and every producer of PEM on Windows writes <c>\r\n</c>. Normalizing
    /// here means the bytes that go out are the bytes Cloudflare expects.
    /// </para>
    /// <para>
    /// What is <em>not</em> checked is whether the certificate parses, whether the key matches it, or whether
    /// it has expired: parsing X.509 twice - once here and once in Cloudflare - would only mean two places to
    /// disagree, and an expired CA is something an account may upload on purpose.
    /// </para>
    /// </remarks>
    public void EnsureValid()
    {
        var problems = new List<string>();

        if (Ca is null)
        {
            problems.Add(
                "an mTLS upload has to say whether it is a CA certificate or a leaf one: Cloudflare requires " +
                "the flag and the two are used for opposite things.");
        }

        if (string.IsNullOrWhiteSpace(Certificates))
        {
            problems.Add("an mTLS upload needs the certificate itself, and this one carries none.");
        }
        else
        {
            Certificates = SettleLineEndings(Certificates);
            if (!Certificates.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal))
            {
                problems.Add(
                    "the certificates field has to be PEM: it should carry a -----BEGIN CERTIFICATE----- line, " +
                    "and this one does not, so it is probably a DER blob or a file name.");
            }
        }

        if (PrivateKey is { Length: > 0 } key)
        {
            PrivateKey = SettleLineEndings(key);
            if (!PrivateKey.Contains("PRIVATE KEY-----", StringComparison.Ordinal))
            {
                problems.Add(
                    "the private key has to be PEM as well - a -----BEGIN … PRIVATE KEY----- block - and this " +
                    "one is not, so it would be refused beside a certificate Cloudflare was happy with.");
            }
        }

        if (problems.Count > 0)
        {
            throw new CFNet.Exceptions.CloudflareConfigurationException(problems);
        }
    }

    /// <summary>Says what this would upload, without printing any of the material.</summary>
    /// <returns>A one-line description of the request.</returns>
    public override string ToString()
    {
        var name = Name is { Length: > 0 } given ? $" ({given})" : string.Empty;
        var role = Ca == true ? "a CA certificate" : Ca == false ? "a leaf certificate" : "a certificate of unstated role";
        var key = PrivateKey is { Length: > 0 } ? ", with its private key" : string.Empty;
        return $"mTLS upload of {role}{name}{key}";
    }

    private static string SettleLineEndings(string pem) => pem.Replace("\r\n", "\n", StringComparison.Ordinal);
}

/// <summary>Which kinds of mTLS certificate a list should answer with.</summary>
public sealed class MtlsCertificateListParameters : ICloudflareQueryParameters
{
    /// <summary>The types to ask for; all of them when null or empty.</summary>
    public List<MtlsCertificateType>? Types { get; set; }

    /// <summary>Builds a filter for one or more types.</summary>
    /// <param name="types">The types to ask for.</param>
    /// <returns>Parameters ready to apply.</returns>
    public static MtlsCertificateListParameters OfTypes(params MtlsCertificateType[] types)
    {
        ArgumentNullException.ThrowIfNull(types);

        return new MtlsCertificateListParameters { Types = types.ToList() };
    }

    /// <summary>Asks for the certificates an account uploaded itself, leaving out the service-managed ones.</summary>
    /// <returns>Parameters filtering to <c>custom</c>.</returns>
    public static MtlsCertificateListParameters UploadedOnly() => OfTypes(MtlsCertificateType.Custom);

    /// <inheritdoc />
    /// <remarks>
    /// The filter is one <c>type</c> parameter with the values comma-separated, which is what Cloudflare's own
    /// reference says to send rather than a repeated parameter per type.
    /// </remarks>
    public void Apply(QueryStringBuilder query)
    {
        var types = Types?.Where(type => type != MtlsCertificateType.Unknown).ToList();
        if (types is { Count: > 0 })
        {
            query.Add("type", string.Join(",", types.Select(CloudflareEnumConverter<MtlsCertificateType>.ToWire)));
        }
    }
}

/// <summary>
/// One service an mTLS certificate is in use by. Cloudflare answers a bare pair of strings and publishes
/// neither vocabulary, so both stay as they arrive.
/// </summary>
public sealed class MtlsCertificateAssociation
{
    /// <summary>The service using the certificate - a hostname, an Access application, a Gateway policy.</summary>
    [JsonPropertyName("service")]
    public string? Service { get; set; }

    /// <summary>How that service is using it.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Anything else Cloudflare sent.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Names the association by the service and its status.</summary>
    /// <returns>A one-line description of the association.</returns>
    public override string ToString() =>
        $"{Service ?? "(an unnamed service)"}: {Status ?? "(no status)"}";
}
