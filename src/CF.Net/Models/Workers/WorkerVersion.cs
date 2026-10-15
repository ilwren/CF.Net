using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Models.Workers;

/// <summary>
/// The <c>result</c> of <c>GET /accounts/{account_id}/workers/scripts/{script_name}/versions</c>.
/// </summary>
/// <remarks>
/// This endpoint does not page the way the rest of the v4 API does. It takes <c>page</c> and
/// <c>per_page</c>, but answers with an object holding an <c>items</c> array and no <c>result_info</c>, so
/// there are no totals to read and <see cref="Http.CloudflarePage{T}"/> - which expects <c>result</c> to be
/// the array itself - does not describe it. Hence a dedicated type rather than the usual page.
/// <c>deployable</c> is documented as ignoring pagination altogether.
/// </remarks>
public sealed class WorkerVersionListResult
{
    /// <summary>The versions, newest first: Cloudflare documents the first entry as the latest version.</summary>
    [JsonPropertyName("items")]
    public List<WorkerVersion>? Items { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>How many versions came back.</summary>
    [JsonIgnore]
    public int Count => Items?.Count ?? 0;

    /// <summary>The latest version, which Cloudflare puts first, or <c>null</c> when there are none.</summary>
    [JsonIgnore]
    public WorkerVersion? Latest => Items is { Count: > 0 } items ? items[0] : null;

    /// <inheritdoc />
    public override string ToString() => $"{Count} version(s)";
}

/// <summary>One uploaded version of a Worker script.</summary>
/// <remarks>
/// Modelled from the published schema, which gives a version an <c>id</c>, a <c>number</c> and a
/// <c>metadata</c> object with <c>author_email</c>, <c>author_id</c>, <c>created_on</c> and three further
/// fields the reference does not name on this page. Those three, and anything else a version carries -
/// resources, secrets, annotations - land in <see cref="AdditionalData"/> rather than being dropped, and the
/// integration suite reports what a live version actually contains so they can be promoted to properties
/// once observed rather than guessed at.
/// </remarks>
public sealed class WorkerVersion
{
    /// <summary>Unique identifier for the version.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>The version's ordinal within the script. Version 0 is the first upload.</summary>
    [JsonPropertyName("number")]
    public int? Number { get; set; }

    /// <summary>Who uploaded it and when.</summary>
    [JsonPropertyName("metadata")]
    public WorkerVersionMetadata? Metadata { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        Number is { } number ? $"version {number} ({Id})" : $"version {Id}";
}

/// <summary>Authorship of a Worker version.</summary>
public sealed class WorkerVersionMetadata
{
    /// <summary>Email of whoever uploaded the version.</summary>
    [JsonPropertyName("author_email")]
    public string? AuthorEmail { get; set; }

    /// <summary>User or token id of whoever uploaded the version.</summary>
    [JsonPropertyName("author_id")]
    public string? AuthorId { get; set; }

    /// <summary>When the version was uploaded.</summary>
    [JsonPropertyName("created_on")]
    public DateTimeOffset? CreatedOn { get; set; }

    /// <summary>Captures the further metadata fields this endpoint returns but does not document.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => AuthorEmail ?? AuthorId ?? "(unknown author)";
}

/// <summary>Query parameters for listing Worker versions.</summary>
public sealed class WorkerVersionListParameters
{
    /// <summary>
    /// Only return versions that can be used in a deployment. Documented as ignoring pagination, so
    /// <see cref="Page"/> and <see cref="PerPage"/> are pointless alongside it.
    /// </summary>
    public bool? Deployable { get; set; }

    /// <summary>Page number.</summary>
    public int? Page { get; set; }

    /// <summary>Items per page.</summary>
    public int? PerPage { get; set; }
}
