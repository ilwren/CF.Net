using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Http;

namespace CFNet.Models.Workers;

/// <summary>
/// One row of <c>GET /accounts/{account_id}/workers/scripts-search</c>.
/// </summary>
/// <remarks>
/// A compact descriptor rather than the full <see cref="WorkerScript"/> the scripts list returns: it names
/// the script, says when it was created and last modified, and identifies the environment and service it
/// belongs to. Bindings, usage model and compatibility date still come from <c>GetSettingsAsync</c>.
/// </remarks>
public sealed class WorkerScriptSearchItem
{
    /// <summary>Script identifier, up to 32 characters.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>When the script was created.</summary>
    [JsonPropertyName("created_on")]
    public DateTimeOffset? CreatedOn { get; set; }

    /// <summary>When the script was last modified.</summary>
    [JsonPropertyName("modified_on")]
    public DateTimeOffset? ModifiedOn { get; set; }

    /// <summary>Name of the script.</summary>
    [JsonPropertyName("script_name")]
    public string? ScriptName { get; set; }

    /// <summary>Whether the environment is the default environment.</summary>
    [JsonPropertyName("environment_is_default")]
    public bool? EnvironmentIsDefault { get; set; }

    /// <summary>Name of the environment.</summary>
    [JsonPropertyName("environment_name")]
    public string? EnvironmentName { get; set; }

    /// <summary>Name of the service.</summary>
    [JsonPropertyName("service_name")]
    public string? ServiceName { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The script name, with its environment appended when that environment is not the default.</summary>
    public override string ToString() =>
        EnvironmentIsDefault == false && EnvironmentName is { Length: > 0 } environment
            ? $"{ScriptName} ({environment})"
            : ScriptName ?? string.Empty;
}

/// <summary>
/// The three values <c>order_by</c> accepts on the script search endpoint. Results always come back
/// ascending; the endpoint publishes no direction parameter.
/// </summary>
public static class WorkerScriptSearchOrderBy
{
    /// <summary>Sort by creation time.</summary>
    public const string CreatedOn = "created_on";

    /// <summary>Sort by last modification time.</summary>
    public const string ModifiedOn = "modified_on";

    /// <summary>Sort by script name.</summary>
    public const string Name = "name";
}

/// <summary>
/// Query parameters for <c>GET /accounts/{account_id}/workers/scripts-search</c>.
/// </summary>
/// <remarks>
/// <see cref="Id"/> is matched exactly only, while <see cref="Name"/> matches both exactly and partially -
/// the reference is explicit about that difference, so searching by a Worker's tag and searching by its
/// name are not interchangeable. Paging is by page number, which is why this derives from
/// <see cref="CloudflarePagedParameters"/> rather than the cursor base the scripts list uses.
/// </remarks>
public sealed class WorkerScriptSearchParameters : CloudflarePagedParameters
{
    /// <summary>Worker id, also called a tag. Only exact matches are returned.</summary>
    public string? Id { get; set; }

    /// <summary>Worker name. Both exact and partial matches are returned.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Property to sort by, ascending. One of the <see cref="WorkerScriptSearchOrderBy"/> values.
    /// </summary>
    public string? OrderBy { get; set; }

    /// <inheritdoc />
    public override void Apply(QueryStringBuilder query)
    {
        base.Apply(query);
        query.AddIfNotEmpty("id", Id);
        query.AddIfNotEmpty("name", Name);
        query.AddIfNotEmpty("order_by", OrderBy);
    }

    /// <summary>
    /// Checks the bounds the reference documents - <c>page</c> from 1, <c>per_page</c> between 1 and 100 -
    /// so a request Cloudflare would refuse for an out-of-range page size says so here instead.
    /// </summary>
    /// <exception cref="Exceptions.CloudflareConfigurationException">A bound is broken.</exception>
    public void EnsureValid()
    {
        var problems = new List<string>();

        if (Page is < 1)
        {
            problems.Add($"Page must be at least 1 (got {Page}).");
        }

        if (PerPage is < 1 or > 100)
        {
            problems.Add($"PerPage must be between 1 and 100 (got {PerPage}).");
        }

        if (problems.Count > 0)
        {
            throw new Exceptions.CloudflareConfigurationException(problems);
        }
    }
}
