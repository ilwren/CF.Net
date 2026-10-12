using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Http;

namespace CFNet.Models.Ssl;

/// <summary>
/// A certificate pack, from <c>GET /zones/{zone_id}/ssl/certificate_packs</c>.
/// </summary>
/// <remarks>
/// Cloudflare's schema for a pack carries more than the fields modelled here - issuance details, the
/// validation state of each host, the primary certificate. Anything unmapped is captured in
/// <see cref="AdditionalData"/> instead of being dropped, so reading one of those fields out of the raw
/// JSON is always possible without waiting for the model to grow.
/// </remarks>
public sealed class SslCertificatePack
{
    /// <summary>The unique identifier of the certificate pack.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Zone the pack belongs to.</summary>
    [JsonPropertyName("zone_id")]
    public string? ZoneId { get; set; }

    /// <summary>
    /// Hostnames the pack covers. Cloudflare requires the zone apex to be among them, allows at most 50
    /// and rejects an empty list.
    /// </summary>
    [JsonPropertyName("hosts")]
    public List<string>? Hosts { get; set; }

    /// <summary>
    /// Status of the pack, e.g. <see cref="SslCertificatePackStatuses.PendingValidation"/>. Cloudflare
    /// defines more statuses than this library names, so the value is kept as the string it arrived in.
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Kind of pack, one of <see cref="SslCertificatePackTypes"/>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// Certificate authority that issued the pack: one of <see cref="SslCertificateAuthorities"/>, or
    /// <c>null</c> for packs that have not been issued yet.
    /// </summary>
    [JsonPropertyName("certificate_authority")]
    public string? CertificateAuthority { get; set; }

    /// <summary>The order in which this certificate is used when several match a hostname.</summary>
    [JsonPropertyName("priority")]
    public double? Priority { get; set; }

    /// <summary>The type of hash used for the certificate.</summary>
    [JsonPropertyName("signature")]
    public string? Signature { get; set; }

    /// <summary>When the certificate was uploaded to Cloudflare.</summary>
    [JsonPropertyName("uploaded_on")]
    public DateTimeOffset? UploadedOn { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => Id ?? string.Empty;
}

/// <summary>
/// The <c>status</c> values the published schema names for a certificate pack.
/// </summary>
/// <remarks>
/// Cloudflare defines more statuses than these; <see cref="SslCertificatePack.Status"/> is a plain string
/// precisely so that an unlisted one is never a deserialisation failure. Compare with
/// <see cref="string.Equals(string?, string?, StringComparison)"/> and
/// <see cref="StringComparison.OrdinalIgnoreCase"/>.
/// </remarks>
public static class SslCertificatePackStatuses
{
    /// <summary>The pack has been created and issuance has not started.</summary>
    public const string Initializing = "initializing";

    /// <summary>Waiting for the domain control validation to be answered.</summary>
    public const string PendingValidation = "pending_validation";

    /// <summary>Validation was never answered in time; restarting it is what the PATCH endpoint is for.</summary>
    public const string ValidationTimedOut = "validation_timed_out";

    /// <summary>Issued but not currently deployed.</summary>
    public const string Inactive = "inactive";

    /// <summary>A replacement certificate has been issued and is being held.</summary>
    public const string BackupIssued = "backup_issued";

    /// <summary>Deployment is on hold.</summary>
    public const string HoldingDeployment = "holding_deployment";

    /// <summary>The pack has been deleted.</summary>
    public const string Deleted = "deleted";
}

/// <summary>The complete set of <c>type</c> values the schema defines for a certificate pack.</summary>
public static class SslCertificatePackTypes
{
    /// <summary>The free universal certificate Cloudflare issues for every zone.</summary>
    public const string Universal = "universal";

    /// <summary>An Advanced Certificate Manager pack.</summary>
    public const string Advanced = "advanced";

    /// <summary>A managed hostname (SaaS) certificate.</summary>
    public const string ManagedHostname = "managed_hostname";

    /// <summary>A custom hostname certificate.</summary>
    public const string MhCustom = "mh_custom";

    /// <summary>A legacy SNI custom certificate.</summary>
    public const string SniCustom = "sni_custom";

    /// <summary>Total TLS, which covers every hostname under the zone.</summary>
    public const string TotalTls = "total_tls";

    /// <summary>A keyless certificate: the private key never leaves your infrastructure.</summary>
    public const string Keyless = "keyless";

    /// <summary>A legacy custom certificate.</summary>
    public const string LegacyCustom = "legacy_custom";
}

/// <summary>The certificate authorities the schema names for a pack.</summary>
public static class SslCertificateAuthorities
{
    /// <summary>Google Trust Services.</summary>
    public const string Google = "google";

    /// <summary>Let's Encrypt.</summary>
    public const string LetsEncrypt = "lets_encrypt";

    /// <summary>SSL.com.</summary>
    public const string SslCom = "ssl_com";
}

/// <summary>The deployment environments the <c>deploy</c> query parameter accepts.</summary>
public static class SslCertificatePackDeployments
{
    /// <summary>Certificates that are staged but not serving traffic.</summary>
    public const string Staging = "staging";

    /// <summary>Certificates that are live.</summary>
    public const string Production = "production";
}

/// <summary>Query parameters of <c>GET /zones/{zone_id}/ssl/certificate_packs</c>.</summary>
/// <remarks>
/// <see cref="CloudflarePagedParameters.PerPage"/> is capped at 50 by this endpoint and must be at least 5
/// when it is sent; <see cref="CloudflarePagedParameters.Page"/> starts at 1.
/// </remarks>
public sealed class SslCertificatePackListParameters : CloudflarePagedParameters
{
    /// <summary>
    /// Deployment environment to list, <see cref="SslCertificatePackDeployments.Staging"/> or
    /// <see cref="SslCertificatePackDeployments.Production"/>. Omit it to let Cloudflare choose.
    /// </summary>
    public string? Deploy { get; set; }

    /// <summary>
    /// Send <c>all</c> to include packs of every status. Without it the endpoint lists only the active
    /// ones, which is the single value the schema defines for this parameter.
    /// </summary>
    public string? Status { get; set; }

    /// <inheritdoc />
    public override void Apply(QueryStringBuilder query)
    {
        base.Apply(query);
        query.AddIfNotEmpty("deploy", Deploy);
        query.AddIfNotEmpty("status", Status);
    }
}

/// <summary>
/// The <c>result</c> of <c>DELETE /zones/{zone_id}/ssl/certificate_packs/{certificate_pack_id}</c>, which
/// is just the identifier of the pack that was removed.
/// </summary>
public sealed class SslCertificatePackDeletion
{
    /// <summary>The unique identifier of the deleted certificate pack.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => Id ?? string.Empty;
}
