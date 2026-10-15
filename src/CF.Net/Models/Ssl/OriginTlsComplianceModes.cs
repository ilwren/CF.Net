using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Models.Ssl;

/// <summary>
/// The <c>origin_tls_compliance_modes</c> zone setting: the list of compliance regimes that constrain which
/// key-exchange algorithms Cloudflare may use when it connects to the zone's origin.
/// </summary>
/// <remarks>
/// <para>
/// This is the constraint that <c>auto_origin_tls_kex</c> works inside. With that setting on, Cloudflare
/// scans a zone's origins and picks a preferred key exchange; every mode listed here narrows what it is
/// allowed to pick, and several modes combine as the <b>intersection</b> of their permitted algorithms - a
/// combination whose intersection is empty is rejected rather than silently loosened.
/// </para>
/// <para>
/// Cloudflare's own description of <see cref="Id"/> calls it "the identifier of the caching setting", which
/// is a copy-paste mistake in the published schema: nothing here is about caching.
/// </para>
/// </remarks>
public sealed class OriginTlsComplianceModes
{
    /// <summary>
    /// The setting's id - and not the one it was requested by. The path takes
    /// <c>origin_tls_compliance_modes</c>; the result reports <c>origin_tls_cmpl</c>, observed live on
    /// 2026-10-13. Both spellings are constants on <c>CFNet.Models.Zones.ZoneSettingIds</c>, so a caller
    /// comparing this against the request id should compare against the reported one instead.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Whether the setting can be changed with the credentials in use.</summary>
    [JsonPropertyName("editable")]
    public bool? Editable { get; set; }

    /// <summary>
    /// The modes currently in force. An empty list means no constraint at all, and the delete response
    /// omits the field entirely rather than sending an empty list.
    /// </summary>
    /// <remarks>
    /// Deliberately a list of plain strings rather than an enumeration: Cloudflare says future modes (it
    /// names <c>cnsa2</c> as an example) may appear and that clients should treat unknown values as opaque
    /// strings, so a value this library has not heard of must survive the round trip.
    /// </remarks>
    [JsonPropertyName("value")]
    public List<string>? Value { get; set; }

    /// <summary>When the setting was last modified.</summary>
    [JsonPropertyName("modified_on")]
    public DateTimeOffset? ModifiedOn { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Whether any constraint is in force at all.</summary>
    [JsonIgnore]
    public bool IsConstrained => Value is { Count: > 0 };

    /// <inheritdoc />
    public override string ToString() =>
        Value is { Count: > 0 } modes ? string.Join(", ", modes) : "(no constraint)";
}

/// <summary>
/// Body of both <c>PUT</c> and <c>PATCH /zones/{zone_id}/settings/origin_tls_compliance_modes</c>.
/// </summary>
/// <remarks>
/// <para>
/// Both verbs do the same thing, and it is not what <c>PATCH</c> usually means: each performs a <b>full
/// replace</b> of the list, not a merge. Anything not in the body is removed, so dropping one mode means
/// sending the remaining ones. An empty list clears the constraint.
/// </para>
/// <para>
/// Invalid mode values are rejected with a 4xx, and so is a combination whose permitted-algorithm
/// intersection is empty - neither of which this library can check without knowing Cloudflare's current
/// algorithm tables, so both are left to the API.
/// </para>
/// </remarks>
public sealed class UpdateOriginTlsComplianceModesRequest
{
    /// <summary>
    /// The complete new list of modes. Required, and may be empty - an empty list is how the constraint is
    /// cleared, which is not the same as leaving the field out.
    /// </summary>
    [JsonPropertyName("value")]
    public List<string>? Value { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Builds the body that sets exactly these modes.</summary>
    /// <param name="modes">Modes to put in force, for example <c>fips</c> and <c>pqh</c>.</param>
    public static UpdateOriginTlsComplianceModesRequest With(params string[] modes) =>
        new() { Value = modes.ToList() };

    /// <summary>Builds the body that clears the constraint, which is an empty list rather than no list.</summary>
    public static UpdateOriginTlsComplianceModesRequest Clear() =>
        new() { Value = new List<string>() };

    /// <summary>Rejects a body with no list at all, which Cloudflare could not read as a replacement.</summary>
    /// <exception cref="Exceptions.CloudflareConfigurationException">The value list is missing.</exception>
    public void EnsureValid()
    {
        if (Value is null)
        {
            throw new Exceptions.CloudflareConfigurationException(
                "Replacing the origin TLS compliance modes needs the complete new list, even when that list is empty.");
        }
    }

    /// <inheritdoc />
    public override string ToString() =>
        Value is null ? "(no list)" : Value.Count == 0 ? "clear the constraint" : string.Join(", ", Value);
}

/// <summary>
/// The compliance modes Cloudflare supports today.
/// </summary>
/// <remarks>
/// Not an exhaustive enumeration on purpose: the published schema says future modes may be added and that
/// unknown values should be treated as opaque strings, so <see cref="All"/> lists what is known now and
/// nothing rejects a value that is not in it.
/// </remarks>
public static class OriginTlsComplianceModeValues
{
    /// <summary>Restrict the key exchange to FIPS-approved curves.</summary>
    public const string Fips = "fips";

    /// <summary>Require a post-quantum hybrid key exchange.</summary>
    public const string PostQuantumHybrid = "pqh";

    /// <summary>The modes the schema names today.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Fips, PostQuantumHybrid };
}
