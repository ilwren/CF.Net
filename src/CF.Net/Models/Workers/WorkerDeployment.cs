using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Http;

namespace CFNet.Models.Workers;

/// <summary>
/// The annotations Cloudflare attaches to a Worker version or deployment.
/// </summary>
/// <remarks>
/// <para>
/// The deployment schema names both keys: <c>workers/message</c> is a human-readable note truncated to
/// 1000 bytes, and <c>workers/triggered_by</c> is the operation that created the object. The version schema
/// does not name its annotations at all, and a live version uploaded through the API carried
/// <c>{"workers/triggered_by": "upload"}</c> - the same vocabulary - so one type serves both rather than a
/// string map on one side and properties on the other.
/// </para>
/// <para>
/// The keys contain a slash, which is why they need explicit <see cref="JsonPropertyNameAttribute"/> values:
/// no naming policy produces them.
/// </para>
/// </remarks>
public sealed class WorkerAnnotations
{
    /// <summary>Human-readable message about the version or deployment. Truncated to 1000 bytes.</summary>
    [JsonPropertyName("workers/message")]
    public string? Message { get; set; }

    /// <summary>
    /// The operation that created the version or deployment. Observed as <c>upload</c> for a version created
    /// by uploading a script; the other values Cloudflare uses are not published, so they are not listed
    /// here as constants.
    /// </summary>
    [JsonPropertyName("workers/triggered_by")]
    public string? TriggeredBy { get; set; }

    /// <summary>Captures any annotation key Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>True when nothing is annotated.</summary>
    [JsonIgnore]
    public bool IsEmpty =>
        string.IsNullOrEmpty(Message) && string.IsNullOrEmpty(TriggeredBy) &&
        AdditionalData is not { Count: > 0 };

    /// <inheritdoc />
    public override string ToString() =>
        TriggeredBy is { Length: > 0 } triggeredBy
            ? Message is { Length: > 0 } ? $"{triggeredBy}: {Message}" : triggeredBy
            : Message ?? string.Empty;
}

/// <summary>
/// The <c>result</c> of <c>GET /accounts/{account_id}/workers/scripts/{script_name}/deployments</c>.
/// </summary>
/// <remarks>
/// Like the versions endpoint this nests its array inside the result - under <c>deployments</c>, not
/// <c>items</c> - so <see cref="CloudflarePage{T}"/>, which expects <c>result</c> to be the array itself,
/// does not describe it. Unlike versions it <i>does</i> return <c>result_info</i>, so the paging figures are
/// carried here on <see cref="Info"/>, filled in by the service from the envelope. Cloudflare documents the
/// first entry as the latest deployment actively serving traffic, which <see cref="Latest"/> exposes.
/// </remarks>
public sealed class WorkerDeploymentListResult
{
    /// <summary>The deployments, newest first.</summary>
    [JsonPropertyName("deployments")]
    public List<WorkerDeployment>? Deployments { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>
    /// The envelope's <c>result_info</c>, set by the service. Not part of the JSON body of
    /// <c>result</c>, hence ignored for serialisation.
    /// </summary>
    [JsonIgnore]
    public CloudflareResultInfo? Info { get; set; }

    /// <summary>How many deployments came back on this page.</summary>
    [JsonIgnore]
    public int Count => Deployments?.Count ?? 0;

    /// <summary>The deployment currently serving traffic, which Cloudflare puts first.</summary>
    [JsonIgnore]
    public WorkerDeployment? Latest =>
        Deployments is { Count: > 0 } deployments ? deployments[0] : null;

    /// <summary>Total number of deployments across every page, when Cloudflare reported it.</summary>
    [JsonIgnore]
    public int? TotalCount => Info?.TotalCount;

    /// <summary>Whether another page follows this one.</summary>
    [JsonIgnore]
    public bool HasMore => Info?.HasNextPage ?? false;

    /// <summary>The page number to ask for next, or <c>null</c> when this was the last page.</summary>
    [JsonIgnore]
    public int? NextPage => HasMore && Info?.Page is { } page ? page + 1 : null;

    /// <inheritdoc />
    public override string ToString() => $"{Count} deployment(s)";
}

/// <summary>One deployment of a Worker script: which versions serve what share of traffic.</summary>
public sealed class WorkerDeployment
{
    /// <summary>Deployment id, a UUID.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>When the deployment was created.</summary>
    [JsonPropertyName("created_on")]
    public DateTimeOffset? CreatedOn { get; set; }

    /// <summary>
    /// What produced the deployment. Documented as a required string without an enumerated set of values,
    /// so this library does not invent one.
    /// </summary>
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    /// <summary>
    /// How traffic is split. The schema documents a single value, <c>percentage</c>, which
    /// <see cref="WorkerDeploymentStrategies.Percentage"/> names.
    /// </summary>
    [JsonPropertyName("strategy")]
    public string? Strategy { get; set; }

    /// <summary>
    /// The versions in this deployment and the share of traffic each serves. Percentages across the entries
    /// total 100.
    /// </summary>
    [JsonPropertyName("versions")]
    public List<WorkerDeploymentVersion>? Versions { get; set; }

    /// <summary>Annotations attached to the deployment.</summary>
    [JsonPropertyName("annotations")]
    public WorkerAnnotations? Annotations { get; set; }

    /// <summary>Email of whoever created the deployment, when Cloudflare has one.</summary>
    [JsonPropertyName("author_email")]
    public string? AuthorEmail { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>
    /// True when more than one version serves traffic, which is what a gradual deployment looks like.
    /// </summary>
    [JsonIgnore]
    public bool IsGradual => Versions is { Count: > 1 };

    /// <inheritdoc />
    public override string ToString() =>
        Versions is { Count: > 0 } versions
            ? $"deployment {Id}: " + string.Join(
                  ", ",
                  versions.Select(version => $"{version.VersionId} at {version.Percentage}%"))
            : $"deployment {Id}";
}

/// <summary>One version's share of a deployment's traffic.</summary>
public sealed class WorkerDeploymentVersion
{
    /// <summary>Id of the Worker version, a UUID.</summary>
    [JsonPropertyName("version_id")]
    public string? VersionId { get; set; }

    /// <summary>
    /// Percentage of traffic this version serves. Between 0.01 and 100, and the percentages across a
    /// deployment's versions must total 100.
    /// </summary>
    [JsonPropertyName("percentage")]
    public decimal? Percentage { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"{VersionId} at {Percentage}%";
}

/// <summary>The traffic-splitting strategies Cloudflare documents for a deployment.</summary>
public static class WorkerDeploymentStrategies
{
    /// <summary>Split traffic by percentage across one or more versions. The only published value.</summary>
    public const string Percentage = "percentage";
}

/// <summary>Query parameters for listing Worker deployments.</summary>
public sealed class WorkerDeploymentListParameters
{
    /// <summary>Page number, from 1.</summary>
    public int? Page { get; set; }

    /// <summary>Items per page, between 1 and 100.</summary>
    public int? PerPage { get; set; }

    /// <summary>Start of the deployment creation time range, inclusive.</summary>
    public DateTimeOffset? Since { get; set; }

    /// <summary>End of the deployment creation time range, inclusive.</summary>
    public DateTimeOffset? Until { get; set; }

    /// <summary>
    /// Checks the bounds Cloudflare documents, so a request that would be refused for an out-of-range page
    /// size says so here instead.
    /// </summary>
    /// <exception cref="Exceptions.CloudflareConfigurationException">A bound is broken.</exception>
    public void EnsureValid()
    {
        var problems = new List<string>();

        if (Page is < 1)
        {
            problems.Add($"Page must be at least 1, but was {Page}.");
        }

        if (PerPage is < 1 or > 100)
        {
            problems.Add($"PerPage must be between 1 and 100, but was {PerPage}.");
        }

        if (Since is { } since && Until is { } until && until < since)
        {
            problems.Add($"Until ({until:O}) is before Since ({since:O}), which is an empty range.");
        }

        if (problems.Count > 0)
        {
            throw new Exceptions.CloudflareConfigurationException(problems);
        }
    }
}
