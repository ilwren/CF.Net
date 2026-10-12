using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Models.Ssl;

/// <summary>
/// Auto-Origin TLS KEX enrolment for a zone, from <c>GET /zones/{zone_id}/settings/auto_origin_tls_kex</c>
/// and the matching <c>PATCH</c>.
/// </summary>
/// <remarks>
/// Cloudflare files this under the SSL resource but serves it from the zone settings path. When enabled,
/// Cloudflare periodically scans the zone's origins to work out which key-exchange algorithm they prefer,
/// writes that preference to the edge, and sends it first in the TLS ClientHello to the origin; the choices
/// are limited by the zone's <c>origin_tls_compliance_modes</c> setting. When disabled, the default
/// key-exchange ordering is used. Note the result carries <c>enabled</c> rather than the <c>value</c> that
/// almost every other zone setting uses, which is why it has its own model instead of reusing
/// <c>ZoneSetting</c>.
/// </remarks>
public sealed class AutoOriginTlsKex
{
    /// <summary>Setting id, always <c>auto_origin_tls_kex</c>.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Whether Auto-Origin TLS KEX selection is enabled for the zone.</summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    /// <summary>When the setting was last changed.</summary>
    [JsonPropertyName("modified_on")]
    public DateTimeOffset? ModifiedOn { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>True only when Cloudflare explicitly reported the setting as on.</summary>
    [JsonIgnore]
    public bool IsEnabled => Enabled == true;

    /// <inheritdoc />
    public override string ToString() => Enabled switch
    {
        true => "enabled",
        false => "disabled",
        _ => "unknown",
    };
}

/// <summary>Body of <c>PATCH /zones/{zone_id}/settings/auto_origin_tls_kex</c>.</summary>
/// <remarks>
/// <c>enabled</c> is required by the schema, so <see cref="With"/> is the normal way to build one. Sending
/// <c>false</c> has to be explicit: an empty body would leave the setting alone rather than turning it off.
/// </remarks>
public sealed class UpdateAutoOriginTlsKexRequest
{
    /// <summary>Whether Auto-Origin TLS KEX selection should be on.</summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    /// <summary>Carries any extra field a caller wants to send.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>A body turning the setting on or off.</summary>
    public static UpdateAutoOriginTlsKexRequest With(bool enabled) => new() { Enabled = enabled };

    /// <summary>Throws when the body would be rejected for having no <c>enabled</c>.</summary>
    /// <exception cref="Exceptions.CloudflareConfigurationException">The flag was never set.</exception>
    public void EnsureValid()
    {
        if (Enabled is null)
        {
            throw new Exceptions.CloudflareConfigurationException(
                "'enabled' is required: Auto-Origin TLS KEX is switched with {\"enabled\":true|false}.");
        }
    }
}

/// <summary>
/// Automatic SSL/TLS enrolment for a zone, from <c>GET|PATCH /zones/{zone_id}/settings/ssl_automatic_mode</c>,
/// and the shape <c>GET /zones/{zone_id}/ssl/recommendation</c> returns as well.
/// </summary>
/// <remarks>
/// <c>auto</c> means the automatic system is enrolled: Cloudflare scans the zone and moves its SSL/TLS
/// encryption mode on its own. <c>custom</c> means it is not, and the mode stays whatever the zone says.
/// <see cref="NextScheduledScan"/> is when that scan happens next, and is omitted when no scan is planned.
/// </remarks>
public sealed class SslAutomaticMode
{
    /// <summary>Setting id, <c>ssl_automatic_mode</c>.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Whether the zone's plan allows this to be changed.</summary>
    [JsonPropertyName("editable")]
    public bool? Editable { get; set; }

    /// <summary>When the setting was last changed.</summary>
    [JsonPropertyName("modified_on")]
    public DateTimeOffset? ModifiedOn { get; set; }

    /// <summary>Current setting: <c>auto</c> or <c>custom</c>. See <see cref="SslAutomaticModes"/>.</summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    /// <summary>Next time the zone will be scanned by Automatic SSL/TLS, when one is planned.</summary>
    [JsonPropertyName("next_scheduled_scan")]
    public DateTimeOffset? NextScheduledScan { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>True only when the zone is explicitly enrolled in the automatic system.</summary>
    [JsonIgnore]
    public bool IsAutomatic => string.Equals(Value, SslAutomaticModes.Auto, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override string ToString() => Value ?? "unknown";
}

/// <summary>Body of <c>PATCH /zones/{zone_id}/settings/ssl_automatic_mode</c>.</summary>
/// <remarks>
/// Anything other than <c>auto</c> or <c>custom</c> is refused by Cloudflare with error 1008, "Invalid
/// SSL/TLS encryption configuration value, only auto or custom accepted", so <see cref="EnsureValid"/> rejects
/// it locally instead of spending a request to find out.
/// </remarks>
public sealed class UpdateSslAutomaticModeRequest
{
    /// <summary>Desired enrolment: <c>auto</c> to enrol, <c>custom</c> to leave the mode alone.</summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    /// <summary>Carries any extra field a caller wants to send.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>A body with an arbitrary value.</summary>
    public static UpdateSslAutomaticModeRequest With(string value) => new() { Value = value };

    /// <summary>A body enrolling the zone in Automatic SSL/TLS.</summary>
    public static UpdateSslAutomaticModeRequest Automatic() => With(SslAutomaticModes.Auto);

    /// <summary>A body taking the zone out of Automatic SSL/TLS.</summary>
    public static UpdateSslAutomaticModeRequest Custom() => With(SslAutomaticModes.Custom);

    /// <summary>Throws when Cloudflare would refuse the value.</summary>
    /// <exception cref="Exceptions.CloudflareConfigurationException">The value is not <c>auto</c> or <c>custom</c>.</exception>
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            throw new Exceptions.CloudflareConfigurationException(
                "'value' is required: Automatic SSL/TLS takes \"auto\" or \"custom\".");
        }

        if (!SslAutomaticModes.All.Contains(Value, StringComparer.OrdinalIgnoreCase))
        {
            throw new Exceptions.CloudflareConfigurationException(
                $"Automatic SSL/TLS value '{Value}' is not one of {string.Join(", ", SslAutomaticModes.All)}.");
        }
    }

    /// <inheritdoc />
    public override string ToString() => Value ?? "(no value)";
}

/// <summary>The two values Automatic SSL/TLS enrolment takes.</summary>
public static class SslAutomaticModes
{
    /// <summary>Enrolled: Cloudflare scans the zone and manages the encryption mode.</summary>
    public const string Auto = "auto";

    /// <summary>Not enrolled: the zone's own SSL/TLS mode is left as it is.</summary>
    public const string Custom = "custom";

    /// <summary>Every value Cloudflare accepts, in the order the schema lists them.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Auto, Custom };
}
