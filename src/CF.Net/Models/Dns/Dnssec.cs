using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Models.Dns;

/// <summary>
/// A zone's DNSSEC state, from <c>GET /zones/{zone_id}/dnssec</c>: whether it is signed, the DS record the
/// registrar needs, and the three switches that change how the signing is done.
/// </summary>
/// <remarks>
/// <para>
/// DNSSEC is not a zone setting. It has its own resource, its own path and its own permissions -
/// <c>DNS Read</c> and <c>DNS Write</c>, not <c>Zone Settings</c> - so <c>client.Zones.GetSettingAsync</c>
/// does not reach it and this model does not reuse <c>ZoneSetting</c>.
/// </para>
/// <para>
/// Enabling DNSSEC is not finished when the call returns: <see cref="Status"/> goes to <c>pending</c> and
/// stays there until the DS record in <see cref="Ds"/> is published at the registrar, at which point it
/// becomes <c>active</c>. <see cref="Ds"/>, <see cref="KeyTag"/>, <see cref="Algorithm"/>,
/// <see cref="Digest"/> and friends are all parts of that one record, so either the assembled string or the
/// fields can be handed to a registrar.
/// </para>
/// </remarks>
public sealed class Dnssec
{
    /// <summary>Algorithm key code, for example <c>13</c> for ECDSA P-256 with SHA-256.</summary>
    [JsonPropertyName("algorithm")]
    public string? Algorithm { get; set; }

    /// <summary>Digest hash.</summary>
    [JsonPropertyName("digest")]
    public string? Digest { get; set; }

    /// <summary>Type of digest algorithm.</summary>
    [JsonPropertyName("digest_algorithm")]
    public string? DigestAlgorithm { get; set; }

    /// <summary>Coded type for the digest algorithm.</summary>
    [JsonPropertyName("digest_type")]
    public string? DigestType { get; set; }

    /// <summary>
    /// Whether multi-signer DNSSEC is on, which lets several providers serve the same signed zone at once.
    /// Cloudflare requires it before DNSKEY records other than its own generated ones can be added.
    /// </summary>
    [JsonPropertyName("dnssec_multi_signer")]
    public bool? DnssecMultiSigner { get; set; }

    /// <summary>
    /// Whether Cloudflare may transfer in an already-signed zone, signatures from an external provider
    /// included, without signing records itself. Used when Cloudflare is the secondary.
    /// </summary>
    [JsonPropertyName("dnssec_presigned")]
    public bool? DnssecPresigned { get; set; }

    /// <summary>
    /// Whether NSEC3 is used with DNSSEC. With <see cref="DnssecPresigned"/> on, NSEC3 records come in from
    /// the external provider; with it off, the default, they are generated and signed per request.
    /// </summary>
    [JsonPropertyName("dnssec_use_nsec3")]
    public bool? DnssecUseNsec3 { get; set; }

    /// <summary>The full DS record, assembled: key tag, algorithm, digest type and digest.</summary>
    [JsonPropertyName("ds")]
    public string? Ds { get; set; }

    /// <summary>Flag for the DNSSEC record.</summary>
    [JsonPropertyName("flags")]
    public int? Flags { get; set; }

    /// <summary>Code for the key tag.</summary>
    [JsonPropertyName("key_tag")]
    public int? KeyTag { get; set; }

    /// <summary>Algorithm key type.</summary>
    [JsonPropertyName("key_type")]
    public string? KeyType { get; set; }

    /// <summary>When DNSSEC was last modified.</summary>
    [JsonPropertyName("modified_on")]
    public DateTimeOffset? ModifiedOn { get; set; }

    /// <summary>Public key for the DS record.</summary>
    [JsonPropertyName("public_key")]
    public string? PublicKey { get; set; }

    /// <summary>
    /// Status of DNSSEC, based on the desired state and whether the necessary records are present. One of
    /// <see cref="DnssecStatuses"/> - five values, of which only two can be asked for.
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Whether DNSSEC is actually signing, which is <c>active</c> and nothing else.</summary>
    [JsonIgnore]
    public bool IsActive => Status == DnssecStatuses.Active;

    /// <summary>
    /// Whether the zone is waiting on something - either the DS record at the registrar, or the registrar
    /// still publishing the old one after a disable.
    /// </summary>
    [JsonIgnore]
    public bool IsPending =>
        Status is DnssecStatuses.Pending or DnssecStatuses.PendingDisabled;

    /// <summary>
    /// One line of status: the state, and the key tag and algorithm a registrar needs. The DS record and the
    /// public key are left out of the summary because they are long, not because they are secret - both are
    /// published in DNS.
    /// </summary>
    /// <returns>Status, key tag and algorithm.</returns>
    public override string ToString()
    {
        if (Status is not { Length: > 0 } status)
        {
            return "(no DNSSEC state)";
        }

        return KeyTag is { } keyTag
            ? $"{status} (key tag {keyTag}, algorithm {Algorithm ?? "unknown"})"
            : status;
    }
}

/// <summary>Body of <c>PATCH /zones/{zone_id}/dnssec</c>.</summary>
/// <remarks>
/// Every field is optional and only what is set is sent, so this is a genuine merge - unlike the compliance
/// modes setting, where PATCH replaces the whole list. <see cref="Status"/> accepts two values even though
/// the response reports five, which is why <see cref="EnsureValid"/> checks it locally: asking for
/// <c>pending</c> or <c>error</c> would be refused by Cloudflare, and those states are outcomes rather than
/// instructions.
/// </remarks>
public sealed class UpdateDnssecRequest
{
    /// <summary>Desired state: <c>active</c> to enable, <c>disabled</c> to turn it off.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Turns multi-signer DNSSEC on or off.</summary>
    [JsonPropertyName("dnssec_multi_signer")]
    public bool? DnssecMultiSigner { get; set; }

    /// <summary>Lets Cloudflare take an already-signed zone as secondary.</summary>
    [JsonPropertyName("dnssec_presigned")]
    public bool? DnssecPresigned { get; set; }

    /// <summary>Uses NSEC3 rather than NSEC.</summary>
    [JsonPropertyName("dnssec_use_nsec3")]
    public bool? DnssecUseNsec3 { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Builds the body that enables DNSSEC.</summary>
    /// <returns>The request.</returns>
    public static UpdateDnssecRequest Enable() => new() { Status = DnssecSettableStatuses.Active };

    /// <summary>Builds the body that disables it.</summary>
    /// <returns>The request.</returns>
    public static UpdateDnssecRequest Disable() => new() { Status = DnssecSettableStatuses.Disabled };

    /// <summary>Builds a body that only changes the three signing switches and leaves the state alone.</summary>
    /// <param name="multiSigner">Multi-signer DNSSEC, or null to leave it.</param>
    /// <param name="presigned">Accept an externally signed zone, or null to leave it.</param>
    /// <param name="useNsec3">Use NSEC3, or null to leave it.</param>
    /// <returns>The request.</returns>
    public static UpdateDnssecRequest SigningOptions(
        bool? multiSigner = null,
        bool? presigned = null,
        bool? useNsec3 = null) =>
        new()
        {
            DnssecMultiSigner = multiSigner,
            DnssecPresigned = presigned,
            DnssecUseNsec3 = useNsec3,
        };

    /// <summary>Checks the request before anything is sent.</summary>
    /// <exception cref="Exceptions.CloudflareConfigurationException">
    /// The status is not one of the two that can be asked for, or nothing is set at all.
    /// </exception>
    public void EnsureValid()
    {
        if (Status is { } status && !DnssecSettableStatuses.All.Contains(status))
        {
            throw new Exceptions.CloudflareConfigurationException(
                $"DNSSEC status can only be set to '{DnssecSettableStatuses.Active}' or '{DnssecSettableStatuses.Disabled}'; '{status}' is a state Cloudflare reports, not one to ask for.");
        }

        if (Status is null && DnssecMultiSigner is null && DnssecPresigned is null && DnssecUseNsec3 is null)
        {
            throw new Exceptions.CloudflareConfigurationException(
                "PATCHing DNSSEC needs something to change: a status or one of the three signing switches.");
        }
    }

    /// <summary>Describes what the body asks for.</summary>
    /// <returns>The requested change.</returns>
    public override string ToString()
    {
        var parts = new List<string>();
        if (Status is { } status)
        {
            parts.Add($"status {status}");
        }

        if (DnssecMultiSigner is { } multiSigner)
        {
            parts.Add($"multi-signer {(multiSigner ? "on" : "off")}");
        }

        if (DnssecPresigned is { } presigned)
        {
            parts.Add($"presigned {(presigned ? "on" : "off")}");
        }

        if (DnssecUseNsec3 is { } nsec3)
        {
            parts.Add($"NSEC3 {(nsec3 ? "on" : "off")}");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "(no change)";
    }
}

/// <summary>
/// One of the zone's DNSSEC Zone Signing Keys, from <c>GET /zones/{zone_id}/dnssec/zsk</c>.
/// </summary>
/// <remarks>
/// <para>
/// This response is spelled in PascalCase - <c>DNSKEY</c>, <c>Hdr</c>, <c>Rdlength</c> - because Cloudflare
/// is serialising a DNS library's structs straight out, which is unlike every other endpoint in the API. The
/// names are pinned with explicit attributes rather than left to the snake_case policy.
/// </para>
/// <para>
/// The schema counts five fields at this level. <c>DNSKEY</c> is the one whose shape is published, so it is
/// the one that is typed; <c>Location</c>, <c>Name</c> and the other two arrive in
/// <see cref="AdditionalData"/> rather than being guessed - inventing a type for a field whose type has not
/// been confirmed is how a model stops being a contract.
/// </para>
/// </remarks>
public sealed class DnssecZoneSigningKey
{
    /// <summary>The DNSKEY record itself.</summary>
    [JsonPropertyName("DNSKEY")]
    public DnssecDnsKey? DnsKey { get; set; }

    /// <summary>Captures the fields whose types are not published, and anything added later.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>One line of status: the algorithm and flags, and how many untyped fields came with it.</summary>
    /// <returns>Algorithm, flags and the extra field count.</returns>
    public override string ToString()
    {
        var extras = AdditionalData?.Count ?? 0;
        return DnsKey is { } key
            ? $"algorithm {key.Algorithm?.ToString() ?? "?"}, flags {key.Flags?.ToString() ?? "?"}{(extras > 0 ? $", {extras} untyped field(s)" : string.Empty)}"
            : extras > 0 ? $"{extras} untyped field(s)" : "(empty)";
    }
}

/// <summary>The DNSKEY record inside a Zone Signing Key.</summary>
public sealed class DnssecDnsKey
{
    /// <summary>Algorithm key code.</summary>
    [JsonPropertyName("Algorithm")]
    public int? Algorithm { get; set; }

    /// <summary>DNSKEY flags: 256 for a ZSK, 257 for a KSK.</summary>
    [JsonPropertyName("Flags")]
    public int? Flags { get; set; }

    /// <summary>The record header.</summary>
    [JsonPropertyName("Hdr")]
    public DnssecRecordHeader? Hdr { get; set; }

    /// <summary>Captures the two fields whose types are not published, and anything added later.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Whether this is a key signing key rather than a zone signing key.</summary>
    [JsonIgnore]
    public bool IsKeySigningKey => Flags == 257;

    /// <summary>One line of status.</summary>
    /// <returns>Flags, algorithm and the name from the header.</returns>
    public override string ToString() =>
        $"{(IsKeySigningKey ? "KSK" : "ZSK")} flags={Flags?.ToString() ?? "?"} algorithm={Algorithm?.ToString() ?? "?"} name={Hdr?.Name ?? "?"}";
}

/// <summary>The header of a DNS record inside a Zone Signing Key.</summary>
public sealed class DnssecRecordHeader
{
    /// <summary>DNS class, 1 for IN.</summary>
    [JsonPropertyName("Class")]
    public int? Class { get; set; }

    /// <summary>The owner name.</summary>
    [JsonPropertyName("Name")]
    public string? Name { get; set; }

    /// <summary>Length of the record data.</summary>
    [JsonPropertyName("Rdlength")]
    public int? Rdlength { get; set; }

    /// <summary>Record type, 48 for DNSKEY.</summary>
    [JsonPropertyName("Rrtype")]
    public int? Rrtype { get; set; }

    /// <summary>Time to live in seconds.</summary>
    [JsonPropertyName("Ttl")]
    public int? Ttl { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>One line of status.</summary>
    /// <returns>Name, type, class and TTL.</returns>
    public override string ToString() =>
        $"{Name ?? "?"} type={Rrtype?.ToString() ?? "?"} class={Class?.ToString() ?? "?"} ttl={Ttl?.ToString() ?? "?"}";
}

/// <summary>The five statuses DNSSEC reports.</summary>
/// <remarks>
/// <c>pending</c> means DNSSEC is wanted but the DS record is not published at the registrar yet;
/// <c>pending-disabled</c> is the mirror image, a disable that is waiting for the registrar to drop the old
/// DS record; <c>error</c> means the necessary records are missing or wrong. Only <see cref="Active"/> and
/// <see cref="Disabled"/> can be asked for - see <see cref="DnssecSettableStatuses"/>.
/// </remarks>
public static class DnssecStatuses
{
    /// <summary>Signing and validated by the DS record at the registrar.</summary>
    public const string Active = "active";

    /// <summary>Enabled, waiting for the DS record to be published.</summary>
    public const string Pending = "pending";

    /// <summary>Off.</summary>
    public const string Disabled = "disabled";

    /// <summary>Turning off, waiting for the registrar to drop the DS record.</summary>
    public const string PendingDisabled = "pending-disabled";

    /// <summary>The necessary records are missing or wrong.</summary>
    public const string Error = "error";

    /// <summary>Every status the response can report.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Active, Pending, Disabled, PendingDisabled, Error,
    };
}

/// <summary>The two statuses <c>PATCH /zones/{zone_id}/dnssec</c> accepts.</summary>
/// <remarks>
/// Kept apart from <see cref="DnssecStatuses"/> on purpose: the response enum has five values and the request
/// enum has two, and one list serving both would let a caller ask for a state that only Cloudflare can set.
/// The same split exists for custom hostname certificate authorities, which can be issued by four and
/// filtered by three.
/// </remarks>
public static class DnssecSettableStatuses
{
    /// <summary>Enable DNSSEC.</summary>
    public const string Active = "active";

    /// <summary>Disable DNSSEC.</summary>
    public const string Disabled = "disabled";

    /// <summary>Both, which is the whole request enum.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Active, Disabled };
}
