using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Serialization;

namespace CFNet.Models.Zones;

/// <summary>
/// Which requests a zone's URL normalization applies to.
/// </summary>
[JsonConverter(typeof(CloudflareEnumConverter<UrlNormalizationScope>))]
public enum UrlNormalizationScope
{
    /// <summary>A value this version of the library has not met. Reading one is tolerated; sending one is not.</summary>
    Unknown = 0,

    /// <summary>Normalize the URL of incoming requests before rules see it.</summary>
    [CloudflareValue("incoming")]
    Incoming = 1,

    /// <summary>Normalize incoming requests and the requests Cloudflare sends on to the origin.</summary>
    [CloudflareValue("both")]
    Both = 2,

    /// <summary>Do not normalize anything.</summary>
    [CloudflareValue("none")]
    None = 3,
}

/// <summary>
/// Which normalizer a zone uses.
/// </summary>
[JsonConverter(typeof(CloudflareEnumConverter<UrlNormalizationType>))]
public enum UrlNormalizationType
{
    /// <summary>A value this version of the library has not met. Reading one is tolerated; sending one is not.</summary>
    Unknown = 0,

    /// <summary>Cloudflare's normalizer.</summary>
    [CloudflareValue("cloudflare")]
    Cloudflare = 1,

    /// <summary>The normalizer RFC 3986 describes.</summary>
    [CloudflareValue("rfc3986")]
    Rfc3986 = 2,
}

/// <summary>
/// A zone's URL normalization setting: which normalizer to run, and which of the zone's traffic to run it on.
/// </summary>
/// <remarks>
/// The same shape is what <c>GET</c> answers with and what <c>PUT</c> takes, and Cloudflare publishes both fields
/// as optional on each side: a read can therefore hand back a settings object with neither field filled in, and a
/// write has to fill both, because a <c>PUT</c> that sent only one would be sending the other as nothing.
/// <see cref="EnsureValid"/> is what enforces that. A <c>GET</c> can also answer with no result at all, which
/// <c>IUrlNormalizationService.GetAsync</c> reports as <see langword="null"/>.
/// </remarks>
public sealed class UrlNormalizationSettings
{
    /// <summary>Which requests the normalizer applies to.</summary>
    [JsonPropertyName("scope")]
    public UrlNormalizationScope? Scope { get; set; }

    /// <summary>Which normalizer to run.</summary>
    [JsonPropertyName("type")]
    public UrlNormalizationType? Type { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>
    /// Checks the settings can be sent, so that a <c>PUT</c> does not reach Cloudflare missing half of what it
    /// asks for.
    /// </summary>
    /// <exception cref="CFNet.Exceptions.CloudflareConfigurationException">Thrown when something is missing or
    /// was never published.</exception>
    public void EnsureValid()
    {
        var problems = new List<string>();

        if (Scope is null)
        {
            problems.Add(
                "a URL normalization update has to say which requests it applies to - Cloudflare requires the " +
                "scope, and this one carries none.");
        }
        else if (Scope == UrlNormalizationScope.Unknown)
        {
            problems.Add(
                "the scope Cloudflare answered with is not one this version of the library knows about, so it " +
                "cannot be sent back. Set Scope to one of incoming, both or none.");
        }

        if (Type is null)
        {
            problems.Add(
                "a URL normalization update has to say which normalizer to run - Cloudflare requires the type, " +
                "and this one carries none.");
        }
        else if (Type == UrlNormalizationType.Unknown)
        {
            problems.Add(
                "the normalizer Cloudflare answered with is not one this version of the library knows about, so " +
                "it cannot be sent back. Set Type to cloudflare or rfc3986.");
        }

        if (problems.Count > 0)
        {
            throw new CFNet.Exceptions.CloudflareConfigurationException(problems);
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var scope = Scope is { } s && s != UrlNormalizationScope.Unknown
            ? CloudflareEnumConverter<UrlNormalizationScope>.ToWire(s)
            : "no scope";
        var type = Type is { } t && t != UrlNormalizationType.Unknown
            ? CloudflareEnumConverter<UrlNormalizationType>.ToWire(t)
            : "no normalizer";
        return $"URL normalization: {type} applied to {scope}";
    }
}
