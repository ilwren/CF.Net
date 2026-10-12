using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Http;
using CFNet.Models.Common;
using CFNet.Serialization;

namespace CFNet.Models.R2;

/// <summary>
/// An R2 bucket, from <c>GET /accounts/{account_id}/r2/buckets</c>.
/// </summary>
public sealed class R2Bucket
{
    /// <summary>Bucket name. R2 endpoints address a bucket by name, not by id.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Creation timestamp.</summary>
    [JsonPropertyName("creation_date")]
    public DateTimeOffset? CreationDate { get; set; }

    /// <summary>Location hint the bucket was created with.</summary>
    [JsonPropertyName("location")]
    public R2LocationHint? Location { get; set; }

    /// <summary>Data residency jurisdiction, when one was selected.</summary>
    [JsonPropertyName("jurisdiction")]
    public R2Jurisdiction? Jurisdiction { get; set; }

    /// <summary>Default storage class applied to newly uploaded objects.</summary>
    [JsonPropertyName("storage_class")]
    public R2StorageClass? StorageClass { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => Name ?? string.Empty;
}

/// <summary>
/// Body of <c>POST /accounts/{account_id}/r2/buckets</c>.
/// </summary>
/// <remarks>
/// This is one of the few Cloudflare request bodies that uses <c>camelCase</c>: the location and storage
/// class fields are <c>locationHint</c> and <c>storageClass</c>, while the <em>response</em> calls them
/// <c>location</c> and <c>storage_class</c>. The explicit <see cref="JsonPropertyNameAttribute"/> values
/// below pin each side to what Cloudflare actually expects.
/// </remarks>
public sealed class CreateR2BucketRequest
{
    /// <summary>Creates a request.</summary>
    /// <param name="name">Bucket name: 3-64 characters, lowercase letters, digits and hyphens.</param>
    /// <param name="locationHint">Optional preferred location.</param>
    /// <param name="storageClass">Optional default storage class for new objects.</param>
    public CreateR2BucketRequest(string name, R2LocationHint? locationHint = null, R2StorageClass? storageClass = null)
    {
        Name = name;
        LocationHint = locationHint;
        StorageClass = storageClass;
    }

    /// <summary>Parameterless constructor for object initialisers and deserialisation.</summary>
    [System.Text.Json.Serialization.JsonConstructor]
    public CreateR2BucketRequest()
    {
    }

    /// <summary>Bucket name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Preferred location. Best effort, not a guarantee.</summary>
    [JsonPropertyName("locationHint")]
    public R2LocationHint? LocationHint { get; set; }

    /// <summary>Default storage class for newly uploaded objects.</summary>
    [JsonPropertyName("storageClass")]
    public R2StorageClass? StorageClass { get; set; }

    /// <summary>
    /// Jurisdiction, sent as the <c>cf-r2-jurisdiction</c> header rather than in the body.
    /// </summary>
    [JsonIgnore]
    public R2Jurisdiction? Jurisdiction { get; set; }
}

/// <summary>
/// Wrapper for the <c>result</c> of <c>GET /accounts/{account_id}/r2/buckets</c>, which nests the
/// array under a <c>buckets</c> property.
/// </summary>
public sealed class R2BucketListResult
{
    /// <summary>The buckets on this page.</summary>
    [JsonPropertyName("buckets")]
    public List<R2Bucket>? Buckets { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>
/// Query filters for <c>GET /accounts/{account_id}/r2/buckets</c>. R2 paginates with cursors.
/// </summary>
public sealed class R2BucketListParameters : CloudflareCursorParameters
{
    /// <summary>Only buckets whose name contains this phrase.</summary>
    public string? NameContains { get; set; }

    /// <summary>Field to order by. Cloudflare currently only supports <c>name</c>.</summary>
    public string? Order { get; set; }

    /// <summary>Sort direction.</summary>
    public SortDirection? Direction { get; set; }

    /// <summary>Bucket name to start after; buckets are ordered lexicographically.</summary>
    public string? StartAfter { get; set; }

    /// <summary>Jurisdiction to list, sent as the <c>cf-r2-jurisdiction</c> header.</summary>
    public R2Jurisdiction? Jurisdiction { get; set; }

    /// <inheritdoc />
    public override void Apply(QueryStringBuilder query)
    {
        base.Apply(query);
        query.AddIfNotEmpty("name_contains", NameContains);
        query.AddIfNotEmpty("order", Order);
        if (Direction.HasValue)
        {
            query.Add("direction", QueryEnums.ToWire(Direction.Value));
        }

        query.AddIfNotEmpty("start_after", StartAfter);
    }
}

/// <summary>Storage class of an R2 bucket.</summary>
[JsonConverter(typeof(CloudflareEnumConverter<R2StorageClass>))]
public enum R2StorageClass
{
    /// <summary>Value Cloudflare returned that this version of the library does not know.</summary>
    Unknown = 0,

    /// <summary>Standard storage, optimised for frequent access.</summary>
    [CloudflareValue("Standard")]
    Standard = 1,

    /// <summary>Infrequent access storage, cheaper storage but with a retrieval cost.</summary>
    [CloudflareValue("InfrequentAccess")]
    InfrequentAccess = 2,
}

/// <summary>Location hint of an R2 bucket.</summary>
[JsonConverter(typeof(CloudflareEnumConverter<R2LocationHint>))]
public enum R2LocationHint
{
    /// <summary>No preference, or an unrecognised value.</summary>
    Unknown = 0,

    /// <summary>Asia Pacific.</summary>
    [CloudflareValue("apac")]
    Apac = 1,

    /// <summary>Eastern Europe.</summary>
    [CloudflareValue("eeur")]
    Eeur = 2,

    /// <summary>Eastern North America.</summary>
    [CloudflareValue("enam")]
    Enam = 3,

    /// <summary>Western Europe.</summary>
    [CloudflareValue("weur")]
    Weur = 4,

    /// <summary>Western North America.</summary>
    [CloudflareValue("wnam")]
    Wnam = 5,

    /// <summary>Oceania.</summary>
    [CloudflareValue("oc")]
    Oc = 6,
}

/// <summary>
/// Data residency jurisdiction of an R2 bucket. Sent as the <c>cf-r2-jurisdiction</c> request header.
/// </summary>
[JsonConverter(typeof(CloudflareEnumConverter<R2Jurisdiction>))]
public enum R2Jurisdiction
{
    /// <summary>No jurisdiction restriction, or an unrecognised value.</summary>
    Unknown = 0,

    /// <summary>Default: no residency restriction.</summary>
    [CloudflareValue("default")]
    Default = 1,

    /// <summary>European Union residency.</summary>
    [CloudflareValue("eu")]
    Eu = 2,

    /// <summary>United States residency.</summary>
    [CloudflareValue("us")]
    Us = 3,

    /// <summary>FedRAMP residency.</summary>
    [CloudflareValue("fedramp")]
    Fedramp = 4,

    /// <summary>FedRAMP High residency.</summary>
    [CloudflareValue("fedramp-high")]
    FedrampHigh = 5,
}
