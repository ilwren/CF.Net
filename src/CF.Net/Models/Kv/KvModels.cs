using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Http;

namespace CFNet.Models.Kv;

/// <summary>
/// A Workers KV namespace, from <c>GET /accounts/{account_id}/storage/kv/namespaces</c>.
/// </summary>
public sealed class KvNamespace
{
    /// <summary>Namespace identifier. Required by every key and value operation.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Human readable title.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Whether the namespace percent-encodes keys internally.</summary>
    [JsonPropertyName("supports_url_encoding")]
    public bool? SupportsUrlEncoding { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"{Title} ({Id})";
}

/// <summary>Request body of <c>POST /accounts/{account_id}/storage/kv/namespaces</c>.</summary>
public sealed class CreateKvNamespaceRequest
{
    /// <summary>Creates a request.</summary>
    /// <param name="title">Namespace title.</param>
    public CreateKvNamespaceRequest(string title)
    {
        Title = title;
    }

    /// <summary>Parameterless constructor for deserialisation.</summary>
    [System.Text.Json.Serialization.JsonConstructor]
    public CreateKvNamespaceRequest()
    {
    }

    /// <summary>Namespace title. Must be unique within the account.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }
}

/// <summary>Request body of <c>PUT .../storage/kv/namespaces/{namespace_id}</c>.</summary>
public sealed class UpdateKvNamespaceRequest
{
    /// <summary>Creates a request.</summary>
    /// <param name="title">The new title.</param>
    public UpdateKvNamespaceRequest(string title)
    {
        Title = title;
    }

    /// <summary>Parameterless constructor for deserialisation.</summary>
    [System.Text.Json.Serialization.JsonConstructor]
    public UpdateKvNamespaceRequest()
    {
    }

    /// <summary>New namespace title.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }
}

/// <summary>Query filters for <c>GET .../storage/kv/namespaces</c>.</summary>
public sealed class KvNamespaceListParameters : CloudflarePagedParameters
{
    /// <summary>Only namespaces whose title contains this text.</summary>
    public string? Search { get; set; }

    /// <inheritdoc />
    public override void Apply(QueryStringBuilder query)
    {
        base.Apply(query);
        query.AddIfNotEmpty("search", Search);
    }
}

/// <summary>One key listed by <c>GET .../storage/kv/namespaces/{namespace_id}/keys</c>.</summary>
/// <remarks>Values are never included in a key listing - fetch them individually.</remarks>
public sealed class KvKey
{
    /// <summary>The key name. At most 512 bytes; all printable non-whitespace characters are valid.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Unix timestamp in seconds when the key expires, when an expiration was set.</summary>
    [JsonPropertyName("expiration")]
    public long? Expiration { get; set; }

    /// <summary>JSON metadata stored alongside the value.</summary>
    [JsonPropertyName("metadata")]
    public JsonElement? Metadata { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>
    /// The absolute expiry moment, or <c>null</c> when the key does not expire.
    /// </summary>
    [JsonIgnore]
    public DateTimeOffset? ExpiresAt =>
        Expiration.HasValue ? DateTimeOffset.FromUnixTimeSeconds(Expiration.Value) : null;

    /// <summary>Projects <see cref="Metadata"/> onto a typed model.</summary>
    /// <typeparam name="T">The metadata type.</typeparam>
    public T? GetMetadata<T>() =>
        Metadata is { ValueKind: JsonValueKind.Object } element
            ? element.Deserialize<T>(Serialization.CloudflareJson.Default)
            : default;

    /// <inheritdoc />
    public override string ToString() => Name ?? string.Empty;
}

/// <summary>
/// Query filters for <c>GET .../storage/kv/namespaces/{namespace_id}/keys</c>.
/// </summary>
/// <remarks>
/// This endpoint paginates with <c>cursor</c> and <c>limit</c> - note that it does <em>not</em> use
/// <c>per_page</c> like the other list endpoints, hence the dedicated parameter type.
/// </remarks>
public sealed class KvKeyListParameters : ICloudflareQueryParameters
{
    /// <summary>Only keys starting with this prefix.</summary>
    public string? Prefix { get; set; }

    /// <summary>Maximum number of keys to return. Cloudflare requires 10-1000.</summary>
    public int? Limit { get; set; }

    /// <summary>Opaque continuation token from <c>result_info.cursor</c> of the previous page.</summary>
    public string? Cursor { get; set; }

    /// <inheritdoc />
    public void Apply(QueryStringBuilder query)
    {
        query.AddIfNotEmpty("prefix", Prefix);
        query.Add("limit", Limit);
        query.AddIfNotEmpty("cursor", Cursor);
    }
}

/// <summary>
/// A KV value together with the metadata Cloudflare returns for it.
/// </summary>
/// <remarks>
/// <c>GET .../values/{key}</c> is unusual in the v4 API: it returns the stored bytes verbatim rather
/// than a JSON envelope, and reports the JSON metadata in the <c>CF-KV-Metadata</c> response header.
/// This type bundles both.
/// </remarks>
public sealed class KvValue
{
    /// <summary>The key the value was read from.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>The stored bytes.</summary>
    public byte[] Content { get; init; } = Array.Empty<byte>();

    /// <summary>
    /// JSON metadata stored with the value, parsed from the <c>CF-KV-Metadata</c> header.
    /// <c>null</c> when no metadata was attached.
    /// </summary>
    public JsonElement? Metadata { get; init; }

    /// <summary>Content type reported by Cloudflare.</summary>
    public string? ContentType { get; init; }

    /// <summary>HTTP status code of the read.</summary>
    public System.Net.HttpStatusCode StatusCode { get; init; }

    /// <summary>True when the key existed.</summary>
    public bool Found { get; init; }

    /// <summary>The stored value decoded as UTF-8 text.</summary>
    public string Text => System.Text.Encoding.UTF8.GetString(Content);

    /// <summary>Projects <see cref="Metadata"/> onto a typed model.</summary>
    /// <typeparam name="T">The metadata type.</typeparam>
    public T? GetMetadata<T>() =>
        Metadata is { ValueKind: JsonValueKind.Object } element
            ? element.Deserialize<T>(Serialization.CloudflareJson.Default)
            : default;

    /// <summary>Deserialises the stored text as JSON into <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="options">Optional serializer options.</param>
    public T? AsJson<T>(JsonSerializerOptions? options = null) =>
        Content.Length == 0
            ? default
            : JsonSerializer.Deserialize<T>(Content, options ?? Serialization.CloudflareJson.Default);
}

/// <summary>
/// Parameters for <c>PUT .../storage/kv/namespaces/{namespace_id}/values/{key}</c>.
/// </summary>
/// <remarks>
/// The value travels as the raw request body, while <see cref="Expiration"/> and
/// <see cref="ExpirationTtl"/> are query parameters. Per-key JSON metadata can only be attached through
/// the bulk write endpoint - see <see cref="KvBulkWriteOperation.Metadata"/>.
/// </remarks>
public sealed class KvPutValueRequest
{
    /// <summary>Creates a request storing UTF-8 text.</summary>
    /// <param name="text">The value.</param>
    public static KvPutValueRequest FromText(string text) =>
        new() { Content = System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty), ContentType = "text/plain" };

    /// <summary>Creates a request storing arbitrary bytes.</summary>
    /// <param name="content">The value.</param>
    /// <param name="contentType">Media type to send.</param>
    public static KvPutValueRequest FromBytes(byte[] content, string contentType = "application/octet-stream") =>
        new() { Content = content, ContentType = contentType };

    /// <summary>Creates a request storing a serialised object as JSON.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The object to serialise.</param>
    public static KvPutValueRequest FromJson<T>(T value) =>
        new()
        {
            Content = System.Text.Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(value, Serialization.CloudflareJson.Default)),
            ContentType = "application/json",
        };

    /// <summary>Raw bytes to store.</summary>
    public byte[] Content { get; set; } = Array.Empty<byte>();

    /// <summary>Media type sent as <c>Content-Type</c>. Cloudflare stores it and echoes it back on read.</summary>
    public string ContentType { get; set; } = "text/plain";

    /// <summary>Absolute expiry as a Unix timestamp in seconds.</summary>
    public long? Expiration { get; set; }

    /// <summary>Relative expiry in seconds, at least 60.</summary>
    public int? ExpirationTtl { get; set; }

    /// <summary>
    /// Cloudflare rejects a write that sets both an absolute and a relative expiry.
    /// </summary>
    /// <exception cref="Exceptions.CloudflareConfigurationException">Both expiry options are set.</exception>
    public void Validate()
    {
        if (Expiration.HasValue && ExpirationTtl.HasValue)
        {
            throw new Exceptions.CloudflareConfigurationException(
                "KV writes accept either Expiration (absolute, Unix seconds) or ExpirationTtl (relative seconds), not both.");
        }

        if (ExpirationTtl is { } ttl && ttl < 60)
        {
            throw new Exceptions.CloudflareConfigurationException(
                $"KV ExpirationTtl must be at least 60 seconds (got {ttl}).");
        }

        if (Expiration is { } expires && expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            throw new Exceptions.CloudflareConfigurationException(
                "KV Expiration must be a Unix timestamp in the future; Cloudflare rejects keys that are already expired.");
        }
    }
}

/// <summary>
/// One operation of the bulk write endpoint
/// <c>PUT .../storage/kv/namespaces/{namespace_id}/bulk</c>.
/// </summary>
public sealed class KvBulkWriteOperation
{
    /// <summary>Creates a text write.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">UTF-8 text value.</param>
    public static KvBulkWriteOperation Put(string key, string value) => new() { Key = key, Value = value };

    /// <summary>Creates a base64 write, for binary values.</summary>
    /// <param name="key">The key.</param>
    /// <param name="content">Bytes to store.</param>
    public static KvBulkWriteOperation PutBytes(string key, byte[] content) =>
        new() { Key = key, Value = Convert.ToBase64String(content ?? Array.Empty<byte>()), Base64 = true };

    /// <summary>Creates a delete.</summary>
    /// <param name="key">The key to remove.</param>
    public static KvBulkWriteOperation Delete(string key) => new() { Key = key, Delete = true };

    /// <summary>The key. At most 512 bytes.</summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    /// <summary>The value, either as text or base64.</summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    /// <summary>Set when <see cref="Value"/> is base64 encoded binary.</summary>
    [JsonPropertyName("base64")]
    public bool? Base64 { get; set; }

    /// <summary>Absolute expiry as a Unix timestamp in seconds.</summary>
    [JsonPropertyName("expiration")]
    public long? Expiration { get; set; }

    /// <summary>Relative expiry in seconds, at least 60.</summary>
    [JsonPropertyName("expiration_ttl")]
    public int? ExpirationTtl { get; set; }

    /// <summary>Arbitrary JSON metadata to store with the value. Only settable through bulk writes.</summary>
    [JsonPropertyName("metadata")]
    public object? Metadata { get; set; }

    /// <summary>Set to <c>true</c> to delete the key instead of writing it.</summary>
    [JsonPropertyName("delete")]
    public bool? Delete { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}
