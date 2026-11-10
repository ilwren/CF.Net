using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Models.Common;

/// <summary>
/// A single entry of the top level <c>errors</c> array every Cloudflare v4 response carries.
/// </summary>
public sealed class CloudflareErrorInfo
{
    /// <summary>Numeric Cloudflare error code, e.g. <c>10000</c> or <c>9109</c>.</summary>
    [JsonPropertyName("code")]
    public int? Code { get; set; }

    /// <summary>Human readable description.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>Optional link to documentation for this error.</summary>
    [JsonPropertyName("documentation_url")]
    public string? DocumentationUrl { get; set; }

    /// <summary>Optional pointer at the offending part of the request payload.</summary>
    [JsonPropertyName("source")]
    public CloudflareErrorSource? Source { get; set; }

    /// <summary>
    /// Nested failures. Some endpoints return a chain of underlying errors (for example when a
    /// DNS record cannot be created because of a registrar problem).
    /// </summary>
    [JsonPropertyName("error_chain")]
    public List<CloudflareErrorInfo>? ErrorChain { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"[{Code}] {Message}";
}

/// <summary>Points at the request element that caused an error.</summary>
public sealed class CloudflareErrorSource
{
    /// <summary>JSON pointer or query parameter name.</summary>
    [JsonPropertyName("pointer")]
    public string? Pointer { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}
