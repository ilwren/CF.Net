using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Serialization;

namespace CFNet.Models.Ssl;

/// <summary>
/// How a zone asks its origin to use a post-quantum key agreement.
/// </summary>
/// <remarks>
/// These are the three values Cloudflare publishes for the setting. Note that the API itself has been left in
/// place while the feature it used to control was replaced: Cloudflare now negotiates post-quantum key agreements
/// through <c>automatic key exchange</c>, and says in its documentation that requests to this endpoint are no-ops
/// which do not change a zone's post-quantum key agreement. Reading it still answers, and a write still answers
/// success, so a caller has to treat "the call worked" and "the zone changed" as different claims - which is what
/// <c>IOriginPostQuantumEncryptionService.UpdateAsync</c> says out loud, and what the live integration test
/// checks for.
/// </remarks>
[JsonConverter(typeof(CloudflareEnumConverter<OriginPostQuantumEncryptionMode>))]
public enum OriginPostQuantumEncryptionMode
{
    /// <summary>A value this version of the library has not met. Reading one is tolerated; sending one is not.</summary>
    Unknown = 0,

    /// <summary>Do not offer a post-quantum key agreement to the origin.</summary>
    [CloudflareValue("off")]
    Off = 1,

    /// <summary>Offer post-quantum key agreements, but let the origin choose whether to use one.</summary>
    [CloudflareValue("supported")]
    Supported = 2,

    /// <summary>Offer post-quantum key agreements and prefer one when the origin supports it.</summary>
    [CloudflareValue("preferred")]
    Preferred = 3,
}

/// <summary>
/// A zone's origin post-quantum encryption setting, as Cloudflare publishes it: a setting id, whether the zone's
/// plan lets it be changed, the value it holds, and when it last changed.
/// </summary>
public sealed class OriginPostQuantumEncryption
{
    /// <summary>The setting's id, which is <c>origin_pqe</c>.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Whether this zone can change the setting.</summary>
    [JsonPropertyName("editable")]
    public bool? Editable { get; set; }

    /// <summary>The value the setting holds.</summary>
    [JsonPropertyName("value")]
    public OriginPostQuantumEncryptionMode? Value { get; set; }

    /// <summary>When the setting last changed, if Cloudflare reports one.</summary>
    [JsonPropertyName("modified_on")]
    public DateTimeOffset? ModifiedOn { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString()
    {
        var mode = Value is { } value && value != OriginPostQuantumEncryptionMode.Unknown
            ? CloudflareEnumConverter<OriginPostQuantumEncryptionMode>.ToWire(value)
            : "an unpublished value";
        var editable = Editable == true ? "editable" : Editable == false ? "not editable" : "editability unstated";
        return $"origin post-quantum encryption ({Id ?? "?"}): {mode}, {editable}";
    }
}

/// <summary>
/// The body of an origin post-quantum encryption update, which carries the one field Cloudflare asks for.
/// </summary>
public sealed class OriginPostQuantumEncryptionRequest
{
    /// <summary>The value to set.</summary>
    [JsonPropertyName("value")]
    public OriginPostQuantumEncryptionMode? Value { get; set; }

    /// <summary>
    /// Checks the request can be sent: Cloudflare requires <c>value</c>, and a value this library has not met
    /// cannot be turned back into the string the API wants.
    /// </summary>
    /// <exception cref="CFNet.Exceptions.CloudflareConfigurationException">Thrown when the value is missing or
    /// unpublished.</exception>
    public void EnsureValid()
    {
        var problems = new List<string>();

        if (Value is null)
        {
            problems.Add(
                "an origin post-quantum encryption update has to say which value to set - Cloudflare requires " +
                "the value, and this request carries none.");
        }
        else if (Value == OriginPostQuantumEncryptionMode.Unknown)
        {
            problems.Add(
                "the value to set is not one this version of the library knows about, so it cannot be sent. Set " +
                "Value to off, supported or preferred.");
        }

        if (problems.Count > 0)
        {
            throw new CFNet.Exceptions.CloudflareConfigurationException(problems);
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var mode = Value is { } value && value != OriginPostQuantumEncryptionMode.Unknown
            ? CloudflareEnumConverter<OriginPostQuantumEncryptionMode>.ToWire(value)
            : "no value";
        return $"set origin post-quantum encryption to {mode}";
    }
}
