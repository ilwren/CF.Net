using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Models.Zones;

/// <summary>
/// One zone setting, in the shape every <c>/zones/{zone_id}/settings</c> endpoint returns it.
/// </summary>
/// <remarks>
/// The value is deliberately not strongly typed, because Cloudflare does not type it either: most
/// switches are the string <c>"on"</c> or <c>"off"</c>, <c>browser_cache_ttl</c> is a number of seconds,
/// <c>aegis</c> is an object like <c>{"enabled":true,"pool_id":"..."}</c>, and <c>ciphers</c> is an array.
/// <see cref="AsString"/>, <see cref="AsNumber"/>, <see cref="AsBoolean"/> and <see cref="IsOn"/> read the
/// common shapes without a cast at the call site, and <see cref="Value"/> is there for the rest.
/// </remarks>
public sealed class ZoneSetting
{
    /// <summary>Setting id, for example <c>always_online</c>. See <see cref="ZoneSettingIds"/>.</summary>
    public string? Id { get; set; }

    /// <summary>Current value, in whatever shape this particular setting uses.</summary>
    public JsonElement? Value { get; set; }

    /// <summary>Whether the zone's plan allows this setting to be changed.</summary>
    public bool? Editable { get; set; }

    /// <summary>When the setting was last changed, when Cloudflare reports it.</summary>
    public DateTimeOffset? ModifiedOn { get; set; }

    /// <summary>Members Cloudflare added after this library was released.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The value as a string, or <c>null</c> when it is not a JSON string.</summary>
    public string? AsString() =>
        Value is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;

    /// <summary>
    /// The value as a number, or <c>null</c> when it is not one. Settings that hold a number of seconds
    /// are sent as strings by some plans, so those are parsed too.
    /// </summary>
    public double? AsNumber()
    {
        var value = Value;
        if (value is null)
        {
            return null;
        }

        return value.Value.ValueKind switch
        {
            JsonValueKind.Number => value.Value.GetDouble(),
            JsonValueKind.String when double.TryParse(
                value.Value.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => null,
        };
    }

    /// <summary>The value as a JSON boolean, or <c>null</c> when it is not one.</summary>
    public bool? AsBoolean()
    {
        var value = Value;
        if (value is null)
        {
            return null;
        }

        return value.Value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    /// <summary>
    /// True for both spellings of "enabled" that Cloudflare uses: the JSON boolean <c>true</c> and the
    /// string <c>"on"</c>.
    /// </summary>
    public bool IsOn =>
        AsBoolean() == true ||
        string.Equals(AsString(), ZoneSettingValues.On, StringComparison.OrdinalIgnoreCase);
}

/// <summary>The literal values most zone settings take.</summary>
public static class ZoneSettingValues
{
    /// <summary>How a switch setting spells "enabled".</summary>
    public const string On = "on";

    /// <summary>How a switch setting spells "disabled".</summary>
    public const string Off = "off";
}

/// <summary>
/// Setting ids Cloudflare names in its published schema.
/// </summary>
/// <remarks>
/// <para>
/// This is a convenience, not a closed set: every method that takes a setting id takes it as a string,
/// and Cloudflare has around sixty of them. The full list is what <c>ListSettingsAsync</c> returns for a
/// zone, since which settings exist depends on the plan the zone is on.
/// </para>
/// <para>
/// Only ids that appear in the published schema are declared here. A setting id that Cloudflare does not
/// recognise is answered with an error rather than ignored, so it is worth reading back what the zone
/// actually reported before guessing at one.
/// </para>
/// </remarks>
public static class ZoneSettingIds
{
    /// <summary>0-RTT session resumption. Value <c>"on"</c> or <c>"off"</c>.</summary>
    public const string ZeroRtt = "0rtt";

    /// <summary>
    /// Advanced DDoS protection. Value <c>"on"</c> or <c>"off"</c>; it is <c>"on"</c> and not editable on
    /// Business and Enterprise zones.
    /// </summary>
    public const string AdvancedDdos = "advanced_ddos";

    /// <summary>
    /// Dedicated egress IPs towards the origin. Value is an object: <c>{"enabled":bool,"pool_id":"..."}</c>.
    /// </summary>
    public const string Aegis = "aegis";

    /// <summary>
    /// Serve cached copies from the Wayback Machine when the origin is down. Value <c>"on"</c> or
    /// <c>"off"</c>.
    /// </summary>
    public const string AlwaysOnline = "always_online";

    /// <summary>Redirect every <c>http://</c> request to <c>https://</c>. Value <c>"on"</c> or <c>"off"</c>.</summary>
    public const string AlwaysUseHttps = "always_use_https";

    /// <summary>Rewrite <c>http://</c> links in HTML to <c>https://</c>. Value <c>"on"</c> or <c>"off"</c>.</summary>
    public const string AutomaticHttpsRewrites = "automatic_https_rewrites";

    /// <summary>
    /// Automatic Platform Optimization for WordPress. Value is an object with <c>enabled</c>, <c>cf</c>,
    /// <c>wordpress</c>, <c>wp_plugin</c>, <c>cache_by_device_type</c> and <c>hostnames</c>.
    /// </summary>
    public const string AutomaticPlatformOptimization = "automatic_platform_optimization";
}

/// <summary>
/// Body of <c>PATCH /zones/{zone_id}/settings/{setting_id}</c>: either a value or an enabled flag, never
/// both.
/// </summary>
/// <remarks>
/// Cloudflare documents two body shapes for the single-setting edit. Almost every setting takes
/// <c>{"value": ...}</c>; a handful - the SSL recommender enrolment among them - take
/// <c>{"enabled": bool}</c> instead.
/// </remarks>
public sealed class UpdateZoneSettingRequest
{
    /// <summary>The value to set, in the shape this setting expects.</summary>
    public object? Value { get; set; }

    /// <summary>The alternative body, for the settings that take an enabled flag rather than a value.</summary>
    public bool? Enabled { get; set; }

    /// <summary>Builds the body that sets a value.</summary>
    /// <param name="value">The value to send.</param>
    public static UpdateZoneSettingRequest OfValue(object? value) => new() { Value = value };

    /// <summary>Builds the body that sets an enabled flag.</summary>
    /// <param name="enabled">The flag to send.</param>
    public static UpdateZoneSettingRequest OfEnabled(bool enabled) => new() { Enabled = enabled };

    /// <summary>Builds the body that switches a setting on or off.</summary>
    /// <param name="on">True for <c>"on"</c>, false for <c>"off"</c>.</param>
    public static UpdateZoneSettingRequest OfOnOff(bool on) =>
        new() { Value = on ? ZoneSettingValues.On : ZoneSettingValues.Off };

    /// <summary>
    /// Rejects a body Cloudflare would refuse: an empty one, or one that mixes the two shapes.
    /// </summary>
    /// <exception cref="Exceptions.CloudflareConfigurationException">The body cannot be sent as it is.</exception>
    public void EnsureValid()
    {
        if (Value is null && Enabled is null)
        {
            throw new Exceptions.CloudflareConfigurationException(
                "A zone setting edit must carry either a Value or an Enabled flag.");
        }

        if (Value is not null && Enabled is not null)
        {
            throw new Exceptions.CloudflareConfigurationException(
                "A zone setting edit takes a Value or an Enabled flag, not both: Cloudflare documents the body as one shape or the other.");
        }
    }
}

/// <summary>
/// One entry of <c>PATCH /zones/{zone_id}/settings</c>, which takes an array of these rather than an
/// object keyed by id.
/// </summary>
public sealed class ZoneSettingChange
{
    /// <summary>Setting id to change, for example <c>always_online</c>.</summary>
    public string? Id { get; set; }

    /// <summary>The value to set, in the shape this setting expects.</summary>
    public object? Value { get; set; }

    /// <summary>Builds one change.</summary>
    /// <param name="id">Setting id.</param>
    /// <param name="value">Value to set.</param>
    public static ZoneSettingChange Of(string id, object? value) => new() { Id = id, Value = value };

    /// <summary>Builds a change that switches a setting on or off.</summary>
    /// <param name="id">Setting id.</param>
    /// <param name="on">True for <c>"on"</c>, false for <c>"off"</c>.</param>
    public static ZoneSettingChange OnOff(string id, bool on) =>
        new() { Id = id, Value = on ? ZoneSettingValues.On : ZoneSettingValues.Off };

    /// <summary>Rejects a change with no setting id, which Cloudflare could not apply.</summary>
    /// <exception cref="Exceptions.CloudflareConfigurationException">The id is missing.</exception>
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new Exceptions.CloudflareConfigurationException(
                "Every entry of a bulk zone setting edit needs an Id.");
        }
    }
}
