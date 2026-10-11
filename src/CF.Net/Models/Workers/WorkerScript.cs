using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Http;
using CFNet.Models.Common;

namespace CFNet.Models.Workers;

/// <summary>
/// A deployed Worker script, as returned by <c>GET /accounts/{account_id}/workers/scripts</c> and
/// <c>GET /accounts/{account_id}/workers/scripts/{script_name}</c>.
/// </summary>
/// <remarks>
/// Cloudflare keeps widening this model (Worker Versions, exports, cache options, ...). Every field
/// this library does not know about yet is preserved in <see cref="AdditionalData"/> rather than being
/// dropped, so a round trip never loses data.
/// </remarks>
public sealed class WorkerScript
{
    /// <summary>The script name. Cloudflare uses <c>id</c> and <c>script_name</c> interchangeably.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Alias of <see cref="Id"/> that some endpoints return.</summary>
    [JsonPropertyName("script_name")]
    public string? ScriptName { get; set; }

    /// <summary>Hash of the script content; usable as an <c>If-None-Match</c> header on upload.</summary>
    [JsonPropertyName("etag")]
    public string? Etag { get; set; }

    /// <summary>Handlers the script exports, e.g. <c>fetch</c>, <c>scheduled</c>, <c>queue</c>.</summary>
    [JsonPropertyName("handlers")]
    public List<string>? Handlers { get; set; }

    /// <summary>Named entrypoints and the handlers each of them exports.</summary>
    [JsonPropertyName("named_handlers")]
    public List<WorkerNamedHandlers>? NamedHandlers { get; set; }

    /// <summary>Creation timestamp.</summary>
    [JsonPropertyName("created_on")]
    public DateTimeOffset? CreatedOn { get; set; }

    /// <summary>Last modification timestamp.</summary>
    [JsonPropertyName("modified_on")]
    public DateTimeOffset? ModifiedOn { get; set; }

    /// <summary>Runtime compatibility date, e.g. <c>2025-01-01</c>.</summary>
    [JsonPropertyName("compatibility_date")]
    public string? CompatibilityDate { get; set; }

    /// <summary>Runtime compatibility flags, e.g. <c>nodejs_compat</c>.</summary>
    [JsonPropertyName("compatibility_flags")]
    public List<string>? CompatibilityFlags { get; set; }

    /// <summary>Billing/runtime model, e.g. <c>bundled</c> or <c>unbound</c>.</summary>
    [JsonPropertyName("usage_model")]
    public string? UsageModel { get; set; }

    /// <summary>Whether Logpush is enabled for the script.</summary>
    [JsonPropertyName("logpush")]
    public bool? Logpush { get; set; }

    /// <summary>Whether the script is kept warm.</summary>
    [JsonPropertyName("prewarm_enabled")]
    public bool? PrewarmEnabled { get; set; }

    /// <summary>Observability configuration.</summary>
    [JsonPropertyName("observability")]
    public WorkerObservability? Observability { get; set; }

    /// <summary>Smart placement configuration.</summary>
    [JsonPropertyName("placement")]
    public WorkerPlacement? Placement { get; set; }

    /// <summary>Resource limits, e.g. CPU time.</summary>
    [JsonPropertyName("limits")]
    public WorkerLimits? Limits { get; set; }

    /// <summary>Scripts this Worker tails.</summary>
    [JsonPropertyName("tail_consumers")]
    public List<WorkerTailConsumer>? TailConsumers { get; set; }

    /// <summary>Where the currently deployed version came from.</summary>
    [JsonPropertyName("last_deployed_from")]
    public string? LastDeployedFrom { get; set; }

    /// <summary>Identifier of the active deployment.</summary>
    [JsonPropertyName("deployment_id")]
    public string? DeploymentId { get; set; }

    /// <summary>The name a caller should prefer, whichever of the two aliases Cloudflare supplied.</summary>
    [JsonIgnore]
    public string Name => ScriptName ?? Id ?? string.Empty;

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>Handlers exported by one named entrypoint.</summary>
public sealed class WorkerNamedHandlers
{
    /// <summary>Name of the entrypoint.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Handlers the entrypoint exports.</summary>
    [JsonPropertyName("handlers")]
    public List<string>? Handlers { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>Observability (invocation log) configuration of a Worker.</summary>
public sealed class WorkerObservability
{
    /// <summary>Whether invocation logs are collected.</summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    /// <summary>Sampling rate between 0 and 1. <c>1</c> means every invocation.</summary>
    [JsonPropertyName("head_sampling_rate")]
    public double? HeadSamplingRate { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>Smart placement configuration of a Worker.</summary>
public sealed class WorkerPlacement
{
    /// <summary>Placement mode; <c>off</c> or <c>smart</c>.</summary>
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>Resource limits of a Worker.</summary>
public sealed class WorkerLimits
{
    /// <summary>CPU time budget in milliseconds.</summary>
    [JsonPropertyName("cpu_ms")]
    public int? CpuMs { get; set; }

    /// <summary>Maximum number of subrequests.</summary>
    [JsonPropertyName("subrequests")]
    public int? Subrequests { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>A script whose logs this Worker consumes.</summary>
public sealed class WorkerTailConsumer
{
    /// <summary>Name of the tailing script.</summary>
    [JsonPropertyName("service")]
    public string? Service { get; set; }

    /// <summary>Environment of the tailing script.</summary>
    [JsonPropertyName("environment")]
    public string? Environment { get; set; }

    /// <summary>Dispatch namespace of the tailing script, for Workers for Platforms.</summary>
    [JsonPropertyName("namespace")]
    public string? Namespace { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>Query filters for <c>GET /accounts/{account_id}/workers/scripts</c>.</summary>
public sealed class WorkerScriptListParameters : CloudflareCursorParameters
{
    /// <summary>Filter by script name substring.</summary>
    public string? Name { get; set; }

    /// <summary>Field to sort by.</summary>
    public string? Order { get; set; }

    /// <summary>Sort direction.</summary>
    public SortDirection? Direction { get; set; }

    /// <inheritdoc />
    public override void Apply(QueryStringBuilder query)
    {
        base.Apply(query);
        query.AddIfNotEmpty("name", Name);
        query.AddIfNotEmpty("order", Order);
        if (Direction.HasValue)
        {
            query.Add("direction", QueryEnums.ToWire(Direction.Value));
        }
    }
}
