using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Models.Common;

namespace CFNet.Models.Zones;

/// <summary>
/// A Cloudflare zone - i.e. a domain (or subdomain) that has been added to an account.
/// Returned by <c>GET /zones</c> and <c>GET /zones/{zone_id}</c>.
/// </summary>
public sealed class Zone
{
    /// <summary>Zone identifier. Most zone scoped endpoints need this.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>The domain name, e.g. <c>example.com</c>.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Activation state: <c>pending</c>, <c>active</c>, <c>initializing</c>, <c>moved</c>,
    /// <c>deleted</c>, <c>deactivated</c>, <c>read only</c>, <c>transfer error</c>.
    /// Kept as a string because Cloudflare has extended this set over time.
    /// See <see cref="ZoneStatusValues"/> for the known literals.
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>When <c>true</c> the zone is proxied through Cloudflare; traffic goes direct when <c>false</c>.</summary>
    [JsonPropertyName("paused")]
    public bool? Paused { get; set; }

    /// <summary><c>full</c> for a normal zone, <c>partial</c> for CNAME (partner) setup.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Number of hours of Development Mode remaining, <c>0</c> when disabled.</summary>
    [JsonPropertyName("development_mode")]
    public int? DevelopmentMode { get; set; }

    /// <summary>The nameservers Cloudflare assigned.</summary>
    [JsonPropertyName("name_servers")]
    public List<string>? NameServers { get; set; }

    /// <summary>The nameservers the domain had before being added.</summary>
    [JsonPropertyName("original_name_servers")]
    public List<string>? OriginalNameServers { get; set; }

    /// <summary>The registrar the domain used before being added.</summary>
    [JsonPropertyName("original_registrar")]
    public string? OriginalRegistrar { get; set; }

    /// <summary>DNS host at the time of switching to Cloudflare.</summary>
    [JsonPropertyName("original_dnshost")]
    public string? OriginalDnsHost { get; set; }

    /// <summary>Custom (vanity) nameservers, when the account uses them.</summary>
    [JsonPropertyName("vanity_name_servers")]
    public List<string>? VanityNameServers { get; set; }

    /// <summary>Creation timestamp.</summary>
    [JsonPropertyName("created_on")]
    public DateTimeOffset? CreatedOn { get; set; }

    /// <summary>Last modification timestamp.</summary>
    [JsonPropertyName("modified_on")]
    public DateTimeOffset? ModifiedOn { get; set; }

    /// <summary>Timestamp when the zone became active.</summary>
    [JsonPropertyName("activated_on")]
    public DateTimeOffset? ActivatedOn { get; set; }

    /// <summary>The account that owns the zone.</summary>
    [JsonPropertyName("account")]
    public CloudflareIdentifier? Account { get; set; }

    /// <summary>The plan the zone is currently on.</summary>
    [JsonPropertyName("plan")]
    public ZonePlan? Plan { get; set; }

    /// <summary>The plan a pending change would move the zone to.</summary>
    [JsonPropertyName("plan_pending")]
    public ZonePlan? PlanPending { get; set; }

    /// <summary>The user or account that owns the zone.</summary>
    [JsonPropertyName("owner")]
    public CloudflareIdentifier? Owner { get; set; }

    /// <summary>Capabilities of the current credentials on this zone.</summary>
    [JsonPropertyName("permissions")]
    public List<string>? Permissions { get; set; }

    /// <summary>Feature flags and quotas exposed by the API.</summary>
    [JsonPropertyName("meta")]
    public ZoneMeta? Meta { get; set; }

    /// <summary>
    /// The tenant this zone belongs to, for enterprise and Cloudflare-for-Platforms accounts: an object
    /// carrying <c>id</c> and <c>name</c>.
    /// </summary>
    /// <remarks>
    /// Published schema and live response agree that this is an object, and modelling it as the id string
    /// made every zone unreadable: one field of one zone threw the whole envelope away, which surfaced as
    /// "no such zone" rather than as an error. Read <c>Tenant?.Id</c> for the identifier.
    /// </remarks>
    [JsonPropertyName("tenant")]
    public CloudflareIdentifier? Tenant { get; set; }

    /// <summary>
    /// The immediate parent organisational unit this zone belongs to, such as a tenant or a
    /// sub-organisation: an object carrying <c>id</c>.
    /// </summary>
    [JsonPropertyName("tenant_unit")]
    public CloudflareIdentifier? TenantUnit { get; set; }

    /// <summary>CNAME suffix used by partial (CNAME) setups, for example <c>cdn.cloudflare.com</c>.</summary>
    [JsonPropertyName("cname_suffix")]
    public string? CnameSuffix { get; set; }

    /// <summary>Status of the universal SSL certificate.</summary>
    [JsonPropertyName("certificate_status")]
    public string? CertificateStatus { get; set; }

    /// <summary>Status of custom certificates.</summary>
    [JsonPropertyName("custom_certificate_status")]
    public string? CustomCertificateStatus { get; set; }

    /// <summary>Status of the universal certificate.</summary>
    [JsonPropertyName("universal_certificate_status")]
    public string? UniversalCertificateStatus { get; set; }

    /// <summary>Key that proves ownership during partial (CNAME) setup.</summary>
    [JsonPropertyName("verification_key")]
    public string? VerificationKey { get; set; }

    /// <summary>Quota for legacy certificates.</summary>
    [JsonPropertyName("legacy_certificate_quota")]
    public int? LegacyCertificateQuota { get; set; }

    /// <summary>True when <see cref="Status"/> is <c>active</c>.</summary>
    [JsonIgnore]
    public bool IsActive => string.Equals(Status, ZoneStatusValues.Active, StringComparison.OrdinalIgnoreCase);

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>The plan block attached to a zone.</summary>
public sealed class ZonePlan
{
    /// <summary>Plan identifier, needed by <c>PATCH /zones/{zone_id}</c> to change plans.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Marketing name, e.g. <c>Free Website</c>.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Price per billing period.</summary>
    [JsonPropertyName("price")]
    public decimal? Price { get; set; }

    /// <summary>ISO currency code.</summary>
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    /// <summary>Billing period, e.g. <c>monthly</c>.</summary>
    [JsonPropertyName("frequency")]
    public string? Frequency { get; set; }

    /// <summary>Legacy plan id, still present on older accounts.</summary>
    [JsonPropertyName("legacy_id")]
    public string? LegacyId { get; set; }

    /// <summary>Whether the account is subscribed to this plan.</summary>
    [JsonPropertyName("is_subscribed")]
    public bool? IsSubscribed { get; set; }

    /// <summary>Whether the account may subscribe to this plan.</summary>
    [JsonPropertyName("can_subscribe")]
    public bool? CanSubscribe { get; set; }

    /// <summary>Whether legacy discounts apply.</summary>
    [JsonPropertyName("legacy_discount")]
    public bool? LegacyDiscount { get; set; }

    /// <summary>Whether billing is handled outside Cloudflare.</summary>
    [JsonPropertyName("externally_managed")]
    public bool? ExternallyManaged { get; set; }

    /// <summary>External plan identifier.</summary>
    [JsonPropertyName("external_id")]
    public string? ExternalId { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>The <c>meta</c> block attached to a zone.</summary>
public sealed class ZoneMeta
{
    /// <summary>Internal onboarding step counter.</summary>
    [JsonPropertyName("step")]
    public int? Step { get; set; }

    /// <summary>Quota for custom certificates.</summary>
    [JsonPropertyName("custom_certificate_quota")]
    public int? CustomCertificateQuota { get; set; }

    /// <summary>Quota for page rules.</summary>
    [JsonPropertyName("page_rule_quota")]
    public int? PageRuleQuota { get; set; }

    /// <summary>Whether wildcard hostnames may be proxied.</summary>
    [JsonPropertyName("wildcard_proxiable")]
    public bool? WildcardProxiable { get; set; }

    /// <summary>Whether Cloudflare flagged the zone for phishing.</summary>
    [JsonPropertyName("phishing_detected")]
    public bool? PhishingDetected { get; set; }

    /// <summary>Whether multiple load balancers are allowed.</summary>
    [JsonPropertyName("multiple_load_balancers")]
    public bool? MultipleLoadBalancers { get; set; }

    /// <summary>Whether importing a zone file is allowed.</summary>
    [JsonPropertyName("allow_zone_file_import")]
    public bool? AllowZoneFileImport { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>Literal values Cloudflare uses for <see cref="Zone.Status"/>.</summary>
public static class ZoneStatusValues
{
    /// <summary>The zone is live and proxied.</summary>
    public const string Active = "active";

    /// <summary>Nameserver change has not been detected yet.</summary>
    public const string Pending = "pending";

    /// <summary>Cloudflare is still setting the zone up.</summary>
    public const string Initializing = "initializing";

    /// <summary>The domain was moved to another registrar or account.</summary>
    public const string Moved = "moved";

    /// <summary>The zone was deleted.</summary>
    public const string Deleted = "deleted";

    /// <summary>The zone was deactivated.</summary>
    public const string Deactivated = "deactivated";

    /// <summary>The credentials only allow reading.</summary>
    public const string ReadOnly = "read only";

    /// <summary>A registry transfer failed.</summary>
    public const string TransferError = "transfer error";
}

/// <summary>Literal values Cloudflare uses for <see cref="Zone.Type"/>.</summary>
public static class ZoneTypeValues
{
    /// <summary>Full zone setup - the domain uses Cloudflare nameservers.</summary>
    public const string Full = "full";

    /// <summary>Partial (CNAME) setup.</summary>
    public const string Partial = "partial";

    /// <summary>Secondary setup - Cloudflare receives zone transfers from the primary nameservers.</summary>
    public const string Secondary = "secondary";

    /// <summary>Internal zone, used by Cloudflare Zero Trust private DNS.</summary>
    public const string Internal = "internal";
}
