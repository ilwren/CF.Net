using System.Text.Json.Serialization;

namespace CFNet.Configuration;

/// <summary>
/// Which credential scheme is used to authenticate against the Cloudflare API.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CloudflareAuthenticationMode>))]
public enum CloudflareAuthenticationMode
{
    /// <summary>No usable credentials were supplied.</summary>
    None = 0,

    /// <summary>
    /// <c>Authorization: Bearer &lt;api token&gt;</c>. This is the scheme Cloudflare recommends:
    /// tokens are scoped to individual resources and permissions.
    /// </summary>
    ApiToken = 1,

    /// <summary>
    /// <c>X-Auth-Key</c> + <c>X-Auth-Email</c>. The legacy account wide Global API key.
    /// </summary>
    GlobalApiKey = 2,
}
