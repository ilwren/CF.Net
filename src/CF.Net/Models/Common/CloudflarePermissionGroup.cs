using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Models.Common;

/// <summary>
/// One entry of <c>GET /user/tokens/permission_groups</c>: a permission group that can be granted to an API
/// token.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Id"/> is the group's public id - a 32 character hex string such as
/// <c>1a71c399035b4950a1bd1466bbe4f420</c> for Workers Scripts Read - and it is what a token policy refers
/// to. It is not the short key the dashboard's token-creation URL takes in <c>permissionGroupKeys</c>, so
/// this list tells you what may be granted and what it is called, not how to pre-fill a link.
/// </para>
/// <para>
/// This endpoint is user scoped: an account-owned token (<c>cfat_…</c>) is refused with <c>9109</c>, the same
/// refusal it gets from <c>GET /user</c>. The account-scoped sibling
/// <c>GET /accounts/{account_id}/tokens/permission_groups</c> exists but answers an account-owned token the
/// same way, so neither is reachable from the service principal CI normally uses.
/// </para>
/// </remarks>
public sealed class CloudflarePermissionGroup
{
    /// <summary>The group's public id, which is what a token policy grants.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>The group's name as the dashboard shows it, e.g. <c>Workers Scripts Read</c>.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// One sentence on what the group grants, e.g. <c>Grants access to reading AI Audit</c>.
    /// </summary>
    /// <remarks>
    /// Returned by <c>GET /accounts/{account_id}/tokens/permission_groups</c> but not by the published
    /// permission-group catalogue, so it arrived as an uncaptured field on a live run before it was
    /// declared here. Absent from some rows; never invent a value from <see cref="Name"/>.
    /// </remarks>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Product category. One of the <see cref="CloudflarePermissionCategories"/> values.</summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary>
    /// What the group can be scoped to: an account, a zone, the signed-in user, or a single resource
    /// such as one R2 bucket or one Worker script. One or more of the
    /// <see cref="CloudflarePermissionScopes"/> values.
    /// </summary>
    [JsonPropertyName("scopes")]
    public List<string>? Scopes { get; set; }

    /// <summary>Whether the caller may select this group when creating a token.</summary>
    [JsonPropertyName("is_selectable")]
    public bool? IsSelectable { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>True when the group can be granted on an account.</summary>
    [JsonIgnore]
    public bool IsAccountScoped => Contains(CloudflarePermissionScopes.Account);

    /// <summary>True when the group can be granted on a zone.</summary>
    [JsonIgnore]
    public bool IsZoneScoped => Contains(CloudflarePermissionScopes.Zone);

    /// <summary>True when the group belongs to the signed-in user rather than to an account or zone.</summary>
    [JsonIgnore]
    public bool IsUserScoped => Contains(CloudflarePermissionScopes.User);

    /// <inheritdoc />
    public override string ToString() =>
        Category is { Length: > 0 } category ? $"{Name} ({category})" : Name ?? string.Empty;

    private bool Contains(string scope) =>
        Scopes is not null &&
        Scopes.Any(candidate => string.Equals(candidate, scope, StringComparison.Ordinal));
}

/// <summary>The product categories <see cref="CloudflarePermissionGroup.Category"/> can take.</summary>
public static class CloudflarePermissionCategories
{
    /// <summary>Workers, Pages, R2, KV, D1, Queues and the rest of the developer platform.</summary>
    public const string DeveloperPlatform = "developer_platform";

    /// <summary>Workers AI and machine learning products.</summary>
    public const string AiAndMachineLearning = "ai_and_machine_learning";

    /// <summary>Zones, DNS and secondary DNS.</summary>
    public const string DnsAndZones = "dns_and_zones";

    /// <summary>WAF, rate limiting and the rest of application security.</summary>
    public const string AppSecurity = "app_security";

    /// <summary>Rulesets, lists, redirect rules and configuration.</summary>
    public const string RulesAndConfiguration = "rules_and_configuration";

    /// <summary>Zero Trust and Cloudflare One.</summary>
    public const string CloudflareOneAndZeroTrust = "cloudflare_one_and_zero_trust";

    /// <summary>Analytics, logs and Logpush.</summary>
    public const string AnalyticsAndLogs = "analytics_and_logs";

    /// <summary>Load balancing, Spectrum and the rest of network services.</summary>
    public const string NetworkServices = "network_services";

    /// <summary>Stream and Images.</summary>
    public const string Media = "media";

    /// <summary>Email Routing and messaging.</summary>
    public const string EmailAndMessaging = "email_and_messaging";

    /// <summary>Cache and performance products.</summary>
    public const string CacheAndPerformance = "cache_and_performance";

    /// <summary>Account settings, billing and API tokens.</summary>
    public const string AccountAndBilling = "account_and_billing";

    /// <summary>Everything the reference does not place in one of the other categories.</summary>
    public const string Other = "other";
}

/// <summary>The six resource scopes a permission group can apply to.</summary>
public static class CloudflarePermissionScopes
{
    /// <summary>Granted on an account.</summary>
    public const string Account = "com.cloudflare.api.account";

    /// <summary>Granted on a zone within an account.</summary>
    public const string Zone = "com.cloudflare.api.account.zone";

    /// <summary>Granted to the signed-in user.</summary>
    public const string User = "com.cloudflare.api.user";

    /// <summary>Granted on a single R2 bucket.</summary>
    public const string R2Bucket = "com.cloudflare.edge.r2.bucket";

    /// <summary>
    /// Granted on a single Worker script rather than on every script in the account.
    /// </summary>
    /// <remarks>
    /// Carried by the groups that name one script: <c>Individual Workers Editor</c>,
    /// <c>Individual Workers Admin</c>, <c>Individual Workers Content Read-Only</c>,
    /// <c>Individual Workers Metadata Read-Only</c> and <c>Granular Workers Scripts Read</c>.
    /// </remarks>
    public const string WorkerScript = "com.cloudflare.edge.worker.script";

    /// <summary>Granted on a single Flagship (feature flag) application.</summary>
    /// <remarks>
    /// Carried by <c>Flagship App Read</c>, <c>Flagship App Write</c>, <c>Flagship App Bind</c> and
    /// <c>Flagship App Evaluate</c>.
    /// </remarks>
    public const string FlagshipApp = "com.cloudflare.api.account.flagship.app";
}
