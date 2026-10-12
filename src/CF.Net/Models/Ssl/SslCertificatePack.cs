using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Http;

namespace CFNet.Models.Ssl;

/// <summary>
/// A certificate pack, from <c>GET /zones/{zone_id}/ssl/certificate_packs</c>.
/// </summary>
/// <remarks>
/// Most of Cloudflare's schema for a pack is modelled here, including the validation records and errors
/// that say what is still needed to finish issuing it. Anything not modelled - a restriction label on an
/// individual certificate, a field Cloudflare adds later - is captured in <see cref="AdditionalData"/>
/// instead of being dropped, so reading it out of the raw JSON is always possible.
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
    /// The individual certificates in this pack, each covering some of <see cref="Hosts"/>. Present on the
    /// response to an order, and on a get once issuance has produced something.
    /// </summary>
    [JsonPropertyName("certificates")]
    public List<SslCertificate>? Certificates { get; set; }

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

    /// <summary>When the pack was ordered.</summary>
    [JsonPropertyName("created_on")]
    public DateTimeOffset? CreatedOn { get; set; }

    /// <summary>How long the certificate is valid for, in days.</summary>
    [JsonPropertyName("validity_days")]
    public int? ValidityDays { get; set; }

    /// <summary>The validation method selected for the order: <c>txt</c>, <c>http</c> or <c>email</c>.</summary>
    [JsonPropertyName("validation_method")]
    public string? ValidationMethod { get; set; }

    /// <summary>Identifier of the primary certificate in the pack.</summary>
    /// <remarks>
    /// The published schema types this as a string. Older API responses returned an index instead, so a
    /// number here is a Cloudflare-side change rather than something this library can paper over.
    /// </remarks>
    [JsonPropertyName("primary_certificate")]
    public string? PrimaryCertificate { get; set; }

    /// <summary>
    /// What has to be published for the authority to validate the hosts - a TXT record, a CNAME for DCV
    /// delegation, or the addresses an e-mail challenge would go to. Empty until the order exists.
    /// </summary>
    [JsonPropertyName("validation_records")]
    public List<SslValidationRecord>? ValidationRecords { get; set; }

    /// <summary>Domain validation errors the certificate authority has reported.</summary>
    [JsonPropertyName("validation_errors")]
    public List<SslValidationError>? ValidationErrors { get; set; }

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

    /// <summary>Validated and waiting for the authority to issue the certificate.</summary>
    public const string PendingIssuance = "pending_issuance";

    /// <summary>Issued and waiting to be deployed to the edge.</summary>
    public const string PendingDeployment = "pending_deployment";

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

    /// <summary>Every value Cloudflare accepts for an order.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Google, LetsEncrypt, SslCom };
}

/// <summary>The deployment environments the <c>deploy</c> query parameter accepts.</summary>
public static class SslCertificatePackDeployments
{
    /// <summary>Certificates that are staged but not serving traffic.</summary>
    public const string Staging = "staging";

    /// <summary>Certificates that are live.</summary>
    public const string Production = "production";
}

/// <summary>One certificate inside a pack.</summary>
public sealed class SslCertificate
{
    /// <summary>Certificate identifier.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Hostnames covered by this certificate.</summary>
    [JsonPropertyName("hosts")]
    public List<string>? Hosts { get; set; }

    /// <summary>Certificate status, as its own lifecycle rather than the pack's.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Certificate bundle method.</summary>
    [JsonPropertyName("bundle_method")]
    public string? BundleMethod { get; set; }

    /// <summary>When the certificate from the authority expires.</summary>
    [JsonPropertyName("expires_on")]
    public DateTimeOffset? ExpiresOn { get; set; }

    /// <summary>
    /// Captures the rest - <c>geo_restrictions</c>, issuer, signature and so on - rather than guessing at
    /// field names this release has not seen documented.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => Id ?? string.Empty;
}

/// <summary>
/// Body of <c>POST /zones/{zone_id}/ssl/certificate_packs/order</c>, which orders an Advanced Certificate
/// Manager pack.
/// </summary>
/// <remarks>
/// Every field except <see cref="CloudflareBranding"/> is required by Cloudflare, and each has a closed set
/// of accepted values - <see cref="EnsureValid"/> checks all of them locally so a typo is a configuration
/// exception here rather than a 400 from the API. Needs <c>SSL and Certificates Write</c>.
/// </remarks>
public sealed class OrderSslCertificatePackRequest
{
    /// <summary>How many hosts one certificate pack may cover. Cloudflare's cap, checked locally.</summary>
    public const int MaxHosts = 50;

    /// <summary>Creates an order.</summary>
    /// <param name="hosts">
    /// Hostnames to cover. Must contain the zone apex, at most 50 of them, and never empty.
    /// </param>
    /// <param name="validationMethod">One of <see cref="SslValidationMethods"/>.</param>
    /// <param name="validityDays">One of <see cref="SslCertificatePackValidityDays"/>.</param>
    /// <param name="certificateAuthority">One of <see cref="SslCertificateAuthorities"/>.</param>
    /// <param name="cloudflareBranding">
    /// Adds a <c>sni.cloudflaressl.com</c> subdomain as the common name when true.
    /// </param>
    public OrderSslCertificatePackRequest(
        IEnumerable<string> hosts,
        string validationMethod,
        int validityDays,
        string certificateAuthority,
        bool? cloudflareBranding = null)
    {
        ArgumentNullException.ThrowIfNull(hosts);

        Type = SslCertificatePackTypes.Advanced;
        Hosts = hosts.ToList();
        ValidationMethod = validationMethod;
        ValidityDays = validityDays;
        CertificateAuthority = certificateAuthority;
        CloudflareBranding = cloudflareBranding;
    }

    /// <summary>Parameterless constructor for deserialisation.</summary>
    [JsonConstructor]
    public OrderSslCertificatePackRequest()
    {
    }

    /// <summary>
    /// Kind of pack. <c>advanced</c> is the only value this endpoint defines, and the constructor sets it.
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Hostnames to cover; must contain the zone apex.</summary>
    [JsonPropertyName("hosts")]
    public List<string>? Hosts { get; set; }

    /// <summary>How domain control is validated: <c>txt</c>, <c>http</c> or <c>email</c>.</summary>
    [JsonPropertyName("validation_method")]
    public string? ValidationMethod { get; set; }

    /// <summary>How long the certificate is valid for: 14, 30, 90 or 365 days.</summary>
    [JsonPropertyName("validity_days")]
    public int? ValidityDays { get; set; }

    /// <summary>Which authority issues it: <c>google</c>, <c>lets_encrypt</c> or <c>ssl_com</c>.</summary>
    [JsonPropertyName("certificate_authority")]
    public string? CertificateAuthority { get; set; }

    /// <summary>Whether to add Cloudflare branding to the order.</summary>
    [JsonPropertyName("cloudflare_branding")]
    public bool? CloudflareBranding { get; set; }

    /// <summary>
    /// Anything this release does not model yet can be sent through here; extension data is serialised
    /// alongside the properties above.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Checks every field against the values Cloudflare accepts.</summary>
    /// <exception cref="Exceptions.CloudflareConfigurationException">A field is missing or out of range.</exception>
    public void EnsureValid()
    {
        if (!string.Equals(Type, SslCertificatePackTypes.Advanced, StringComparison.OrdinalIgnoreCase))
        {
            throw new Exceptions.CloudflareConfigurationException(
                $"Ordering a certificate pack needs type '{SslCertificatePackTypes.Advanced}', which is the only one this endpoint defines.");
        }

        if (Hosts is not { Count: > 0 })
        {
            throw new Exceptions.CloudflareConfigurationException(
                "A certificate pack needs at least one host, and the list must contain the zone apex.");
        }

        if (Hosts.Count > MaxHosts)
        {
            throw new Exceptions.CloudflareConfigurationException(
                $"A certificate pack may not cover more than {MaxHosts} hosts, and {Hosts.Count} were given.");
        }

        if (Hosts.Any(string.IsNullOrWhiteSpace))
        {
            throw new Exceptions.CloudflareConfigurationException("Every host in a certificate pack needs a name.");
        }

        if (!SslValidationMethods.All.Contains(ValidationMethod, StringComparer.OrdinalIgnoreCase))
        {
            throw new Exceptions.CloudflareConfigurationException(
                $"Validation method '{ValidationMethod}' is not one of {string.Join(", ", SslValidationMethods.All)}.");
        }

        if (ValidityDays is not { } days || !SslCertificatePackValidityDays.All.Contains(days))
        {
            throw new Exceptions.CloudflareConfigurationException(
                $"Validity days '{ValidityDays}' is not one of {string.Join(", ", SslCertificatePackValidityDays.All)}.");
        }

        if (!SslCertificateAuthorities.All.Contains(CertificateAuthority, StringComparer.OrdinalIgnoreCase))
        {
            throw new Exceptions.CloudflareConfigurationException(
                $"Certificate authority '{CertificateAuthority}' is not one of {string.Join(", ", SslCertificateAuthorities.All)}.");
        }
    }
}

/// <summary>The <c>validation_method</c> values an order accepts.</summary>
public static class SslValidationMethods
{
    /// <summary>DNS: prove control with a TXT record.</summary>
    public const string Txt = "txt";

    /// <summary>HTTP: prove control by serving a token over HTTP.</summary>
    public const string Http = "http";

    /// <summary>E-mail to an administrative address at the domain.</summary>
    public const string Email = "email";

    /// <summary>
    /// DCV delegation: prove control with a CNAME record. Accepted by the validation-method PATCH under
    /// <c>ssl/verification</c>, but not when ordering a pack, which is why it is absent from <see cref="All"/>.
    /// </summary>
    public const string Cname = "cname";

    /// <summary>Every value Cloudflare accepts, in the order the schema lists them.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Txt, Http, Email };
}

/// <summary>The <c>validity_days</c> values an order accepts.</summary>
public static class SslCertificatePackValidityDays
{
    /// <summary>Two weeks.</summary>
    public const int Fourteen = 14;

    /// <summary>One month.</summary>
    public const int Thirty = 30;

    /// <summary>Three months.</summary>
    public const int Ninety = 90;

    /// <summary>One year.</summary>
    public const int OneYear = 365;

    /// <summary>Every value Cloudflare accepts, shortest first.</summary>
    public static readonly IReadOnlyList<int> All = new[] { Fourteen, Thirty, Ninety, OneYear };
}

/// <summary>
/// Body of <c>PATCH /zones/{zone_id}/ssl/certificate_packs/{certificate_pack_id}</c>.
/// </summary>
/// <remarks>
/// The endpoint does two unrelated things and the body decides which. Sending
/// <see cref="CloudflareBranding"/> adds or removes Cloudflare branding (a <c>sni.cloudflaressl.com</c>
/// subdomain as the common name); sending nothing at all restarts validation, which Cloudflare only
/// performs for a pack in <see cref="SslCertificatePackStatuses.ValidationTimedOut"/>. That is the whole
/// schema - <c>cloudflare_branding</c> is the only field this PATCH accepts - so anything else has to go
/// through <see cref="AdditionalData"/>.
/// </remarks>
public sealed class UpdateSslCertificatePackRequest
{
    /// <summary>Creates the body that turns Cloudflare branding on or off.</summary>
    /// <param name="enabled">Whether the pack should carry Cloudflare branding.</param>
    public static UpdateSslCertificatePackRequest ForCloudflareBranding(bool enabled) =>
        new() { CloudflareBranding = enabled };

    /// <summary>Creates the empty body that asks Cloudflare to restart validation.</summary>
    public static UpdateSslCertificatePackRequest ToRestartValidation() => new();

    /// <summary>Whether to add Cloudflare branding; <c>null</c> leaves it alone.</summary>
    [JsonPropertyName("cloudflare_branding")]
    public bool? CloudflareBranding { get; set; }

    /// <summary>Anything this release does not model yet, serialised alongside the property above.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>
    /// True when the body carries nothing, which is how a validation restart is spelled.
    /// </summary>
    [JsonIgnore]
    public bool IsEmpty => CloudflareBranding is null && AdditionalData is not { Count: > 0 };

    /// <inheritdoc />
    public override string ToString() =>
        CloudflareBranding is { } branding ? $"cloudflare_branding={branding}" : "restart validation";
}

/// <summary>
/// What a certificate authority wants to see before it validates a hostname, from a pack's
/// <c>validation_records</c>.
/// </summary>
/// <remarks>
/// Which of these are populated depends on the pack's validation method: a <c>txt</c> order carries
/// <see cref="TxtName"/> and <see cref="TxtValue"/>, a <c>cname</c> order (DCV delegation) carries
/// <see cref="Cname"/> and <see cref="CnameTarget"/>, and an <c>email</c> order carries
/// <see cref="Emails"/>. This is exactly what <see cref="Services.IDnsRecordsService.CreateTxtRecordAsync"/>
/// needs to answer a TXT challenge, so the two services are meant to be used together.
/// </remarks>
public sealed class SslValidationRecord
{
    /// <summary>The CNAME record hostname for DCV delegation.</summary>
    [JsonPropertyName("cname")]
    public string? Cname { get; set; }

    /// <summary>The CNAME record target value for DCV delegation.</summary>
    [JsonPropertyName("cname_target")]
    public string? CnameTarget { get; set; }

    /// <summary>The addresses the certificate authority will use to complete domain validation.</summary>
    [JsonPropertyName("emails")]
    public List<string>? Emails { get; set; }

    /// <summary>The hostname the authority checks for a TXT record during domain validation.</summary>
    [JsonPropertyName("txt_name")]
    public string? TxtName { get; set; }

    /// <summary>The TXT record the authority will check during domain validation.</summary>
    [JsonPropertyName("txt_value")]
    public string? TxtValue { get; set; }

    /// <summary>Status of this validation record.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>
    /// Captures the rest - the HTTP challenge details and anything Cloudflare adds - rather than guessing
    /// at field names this release has not seen documented.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => TxtName ?? Cname ?? Status ?? string.Empty;
}

/// <summary>A domain validation error the certificate authority reported.</summary>
public sealed class SslValidationError
{
    /// <summary>A domain validation error.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => Message ?? string.Empty;
}

/// <summary>
/// The <c>result</c> of <c>GET /zones/{zone_id}/ssl/certificate_packs/quota</c>: how many advanced
/// certificate packs this zone may have, and how many it has.
/// </summary>
public sealed class SslCertificatePackQuota
{
    /// <summary>The Advanced Certificate Manager part of the quota.</summary>
    [JsonPropertyName("advanced")]
    public SslAdvancedCertificateQuota? Advanced { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>How many more advanced packs may be ordered, or <c>null</c> when Cloudflare did not say.</summary>
    [JsonIgnore]
    public int? Remaining =>
        Advanced?.Allocated is { } allocated && Advanced?.Used is { } used ? allocated - used : null;

    /// <inheritdoc />
    public override string ToString() =>
        Advanced is { } advanced ? $"advanced {advanced.Used ?? 0}/{advanced.Allocated ?? 0}" : "no quota reported";
}

/// <summary>The allocated and used halves of a certificate quota.</summary>
public sealed class SslAdvancedCertificateQuota
{
    /// <summary>Quantity allocated.</summary>
    [JsonPropertyName("allocated")]
    public int? Allocated { get; set; }

    /// <summary>Quantity used.</summary>
    [JsonPropertyName("used")]
    public int? Used { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"{Used ?? 0}/{Allocated ?? 0}";
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
