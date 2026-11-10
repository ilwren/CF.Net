using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Http;

namespace CFNet.Models.DurableObjects;

/// <summary>
/// A Durable Object namespace: one class exported by one Worker script, and the identity space its objects live
/// in.
/// </summary>
/// <remarks>
/// A namespace is created by a script upload rather than by a call of its own: the deployment's
/// <c>durable_object_classes</c> binding (and, for a migration, <see cref="Workers.WorkerMigration"/>) is what
/// brings it into existence, and removing the class removes the namespace. That is why this resource is
/// read-only - there is nothing here to create, update or delete.
/// </remarks>
public sealed class DurableObjectNamespace
{
    /// <summary>The namespace's id, which is what the objects call is keyed by.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>The Durable Object class the script exports.</summary>
    [JsonPropertyName("class")]
    public string? Class { get; set; }

    /// <summary>The namespace's name, which is usually the class name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>The Worker script that exports the class.</summary>
    [JsonPropertyName("script")]
    public string? Script { get; set; }

    /// <summary>Whether the class uses SQLite storage rather than the key-value storage.</summary>
    [JsonPropertyName("use_sqlite")]
    public bool? UseSqlite { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"{Class ?? Name ?? Id ?? "?"} on {Script ?? "?"}" + (UseSqlite == true ? " (sqlite)" : string.Empty);
}

/// <summary>
/// Query parameters for listing Durable Object namespaces. The endpoint publishes no filters beyond paging, so
/// this carries only what <see cref="CloudflarePagedParameters"/> does - it exists because that base class is
/// abstract and a caller needs a concrete type to set <c>page</c> and <c>per_page</c> on.
/// </summary>
public sealed class DurableObjectNamespaceListParameters : CloudflarePagedParameters
{
}

/// <summary>One Durable Object inside a namespace.</summary>
/// <remarks>
/// This is the only place in the API this library wraps where Cloudflare publishes a field in camel case:
/// <c>hasStoredData</c>, not <c>has_stored_data</c>. The property is named for the wire rather than corrected,
/// because a caller comparing what arrives against what the reference publishes has to see the same spelling.
/// </remarks>
public sealed class DurableObject
{
    /// <summary>The object's id, in the hexadecimal form Cloudflare prints it.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Whether the object has data stored against it.</summary>
    [JsonPropertyName("hasStoredData")]
    public bool? HasStoredData { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"{Id ?? "?"}" + (HasStoredData == true ? " (has stored data)" : string.Empty);
}

/// <summary>
/// Query parameters for listing the objects in a namespace. This endpoint pages by cursor, and it sizes a page
/// with <c>limit</c> rather than the <c>per_page</c> that <see cref="CloudflareCursorParameters"/> sends.
/// </summary>
public sealed class DurableObjectListParameters : ICloudflareQueryParameters
{
    /// <summary>Continuation cursor from a previous response's <c>result_info</c>.</summary>
    public string? Cursor { get; set; }

    /// <summary>How many objects to return.</summary>
    public int? Limit { get; set; }

    /// <inheritdoc />
    public void Apply(QueryStringBuilder query)
    {
        query.AddIfNotEmpty("cursor", Cursor);
        query.Add("limit", Limit);
    }

    /// <summary>A copy of these parameters with a different cursor.</summary>
    /// <param name="cursor">The cursor for the next page.</param>
    /// <returns>The new parameters.</returns>
    public DurableObjectListParameters WithCursor(string? cursor) =>
        new() { Cursor = cursor, Limit = Limit };
}
