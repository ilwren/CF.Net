using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Models.Common;

/// <summary>
/// The small <c>{ id, name }</c> reference object Cloudflare embeds for related resources
/// (accounts, plans, tenants, ...).
/// </summary>
public sealed class CloudflareIdentifier
{
    /// <summary>Identifier of the referenced object.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Display name of the referenced object.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => Name ?? Id ?? string.Empty;
}

/// <summary>
/// Body Cloudflare returns for most <c>DELETE</c> calls: the id of the removed object, or
/// simply <c>true</c>.
/// </summary>
public sealed class CloudflareDeleteResult
{
    /// <summary>Identifier of the deleted resource, when the endpoint reports one.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>An account as returned by <c>GET /accounts</c>.</summary>
public sealed class CloudflareAccount
{
    /// <summary>Account identifier (the value most endpoints need).</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Account display name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Account type, e.g. <c>enterprise</c>, <c>pro</c>, <c>free</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Settings block (use_defaults, default_nameservers, ...).</summary>
    [JsonPropertyName("settings")]
    public JsonElement? Settings { get; set; }

    /// <summary>Creation timestamp.</summary>
    [JsonPropertyName("created_on")]
    public DateTimeOffset? CreatedOn { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>
/// The authenticated user, returned by <c>GET /user</c>. Only the fields that are stable across
/// Cloudflare account types are strongly typed; everything else lands in <see cref="AdditionalData"/>.
/// </summary>
public sealed class CloudflareUser
{
    /// <summary>User identifier.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Account e-mail address.</summary>
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    /// <summary>First name.</summary>
    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    /// <summary>Last name.</summary>
    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    /// <summary>Two letter country code.</summary>
    [JsonPropertyName("country")]
    public string? Country { get; set; }

    /// <summary>Account creation timestamp.</summary>
    [JsonPropertyName("created_on")]
    public DateTimeOffset? CreatedOn { get; set; }

    /// <summary>Last modification timestamp.</summary>
    [JsonPropertyName("modified_on")]
    public DateTimeOffset? ModifiedOn { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>Result of <c>GET /user/tokens/verify</c>.</summary>
public sealed class CloudflareTokenVerification
{
    /// <summary>Identifier of the token that was verified.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary><c>active</c> when the token is valid.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>True when <see cref="Status"/> equals <c>active</c>.</summary>
    [JsonIgnore]
    public bool IsActive => string.Equals(Status, "active", StringComparison.OrdinalIgnoreCase);

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}
