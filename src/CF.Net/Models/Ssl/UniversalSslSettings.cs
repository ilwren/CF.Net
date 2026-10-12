using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Models.Ssl;

/// <summary>
/// Universal SSL settings of a zone, from <c>GET /zones/{zone_id}/ssl/universal/settings</c> and the
/// matching <c>PATCH</c>.
/// </summary>
/// <remarks>
/// One boolean, with a disproportionate blast radius: disabling Universal SSL removes every active
/// universal certificate for the zone from the edge and stops Cloudflare ordering new ones. If nothing
/// else covers the hostname - no advanced certificate pack, no uploaded custom certificate - visitors can
/// no longer reach the site over HTTPS, and the settings that assume a certificate is there (HSTS, Always
/// Use HTTPS, Opportunistic Encryption, Onion Routing, page rules redirecting to HTTPS) will turn that
/// into a hard failure rather than a fallback to HTTP. An HTTP-to-HTTPS redirect at the origin does the
/// same while the proxy is on.
/// </remarks>
public sealed class UniversalSslSettings
{
    /// <summary>Whether Universal SSL is on for the zone.</summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>True only when Cloudflare explicitly reported Universal SSL as on.</summary>
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
