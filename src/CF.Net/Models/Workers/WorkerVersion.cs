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
/// The list endpoint's page gives a version an <c>id</c>, a <c>number</c> and a <c>metadata</c> object with
/// <c>author_email</c>, <c>author_id</c> and <c>created_on</c>, and says metadata has three more fields it
/// does not name there. The versions model page does name them - <c>source</c>, whose eleven values
/// <see cref="WorkerVersionSources"/> lists, <c>modified_on</c> and <c>hasPreview</c> - and a live answer
/// confirmed <c>source</c> as <c>api</c> while also carrying <see cref="Annotations"/>, which no version
/// schema publishes but the deployments schema describes. Anything else lands in <see cref="AdditionalData"/>,
/// which the integration suite reports, so the next undocumented field shows up the same way instead of
/// disappearing silently. A version with bindings or secrets may well carry more than a bare upload did.
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

    /// <summary>
    /// Annotations Cloudflare attaches to a version. A version uploaded through the API by an account-owned
    /// token carries <c>workers/triggered_by: upload</c> and nothing else, which is what a live run
    /// reported; the type is shared with deployments, whose schema names both annotation keys.
    /// </summary>
    [JsonPropertyName("annotations")]
    public WorkerAnnotations? Annotations { get; set; }

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

    /// <summary>
    /// What created the version - <c>api</c> for one uploaded through this endpoint, which is what a live
    /// run reported. One of the <see cref="WorkerVersionSources"/> values.
    /// </summary>
    /// <remarks>
    /// The list endpoint's page declines to name this field, but the versions model page publishes it along
    /// with the eleven values it can take, so the constants are documented rather than guessed.
    /// </remarks>
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    /// <summary>Whether the version can be previewed.</summary>
    /// <remarks>
    /// The reference spells this one property <c>hasPreview</c> while every other metadata property is snake
    /// case, but a live answer sent <c>has_preview</c>, and that is what this reads. Were Cloudflare to send
    /// the camelCase spelling instead it would land in <see cref="AdditionalData"/>, which the integration
    /// run reports, rather than being dropped.
    /// </remarks>
    [JsonPropertyName("has_preview")]
    public bool? HasPreview { get; set; }

    /// <summary>When the version was last modified.</summary>
    [JsonPropertyName("modified_on")]
    public DateTimeOffset? ModifiedOn { get; set; }

    /// <summary>Captures the further metadata fields this endpoint returns but does not document.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        string.IsNullOrWhiteSpace(AuthorEmail)
            ? AuthorId ?? "(unknown author)"
            : AuthorEmail;
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

/// <summary>The eleven values <c>metadata.source</c> can take, as published on the versions model page.</summary>
public static class WorkerVersionSources
{
    /// <summary>Cloudflare could not attribute the upload.</summary>
    public const string Unknown = "unknown";

    /// <summary>Uploaded through the API - what this library's uploads report.</summary>
    public const string Api = "api";

    /// <summary>Uploaded by Wrangler.</summary>
    public const string Wrangler = "wrangler";

    /// <summary>Uploaded by the Terraform provider.</summary>
    public const string Terraform = "terraform";

    /// <summary>Uploaded from the dashboard.</summary>
    public const string Dash = "dash";

    /// <summary>Uploaded by the <c>cf</c> CLI.</summary>
    public const string CfCli = "cf_cli";

    /// <summary>Created from a dashboard template.</summary>
    public const string DashTemplate = "dash_template";

    /// <summary>Uploaded by an integration.</summary>
    public const string Integration = "integration";

    /// <summary>Uploaded through the quick editor.</summary>
    public const string QuickEditor = "quick_editor";

    /// <summary>Uploaded from the playground.</summary>
    public const string Playground = "playground";

    /// <summary>Uploaded by Workers CI.</summary>
    public const string WorkersCi = "workersci";
}

/// <summary>The usage models a Worker runtime can be billed under.</summary>
public static class WorkerUsageModels
{
    /// <summary>The bundled request model.</summary>
    public const string Bundled = "bundled";

    /// <summary>The unbound request model, for long running CPU.</summary>
    public const string Unbound = "unbound";

    /// <summary>The standard model.</summary>
    public const string Standard = "standard";
}

/// <summary>
/// The <c>result</c> of <c>GET /accounts/{account_id}/workers/scripts/{script_name}/versions/{version_id}</c>.
/// </summary>
/// <remarks>
/// The list endpoint answers with an id, a number and metadata; reading one version by id adds
/// <see cref="Resources"/>, which is where the bindings, the exported handlers and the runtime
/// configuration of that specific version live. It is the only way to see a version's runtime as it was
/// uploaded, since the script settings endpoint describes the script rather than a version.
/// </remarks>
public sealed class WorkerVersionDetail
{
    /// <summary>Unique identifier for the version.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Sequential version number.</summary>
    [JsonPropertyName("number")]
    public int? Number { get; set; }

    /// <summary>Who uploaded it and when.</summary>
    [JsonPropertyName("metadata")]
    public WorkerVersionMetadata? Metadata { get; set; }

    /// <summary>
    /// Annotations on the version. No published version schema names this field, but a live list answer
    /// carried <c>workers/triggered_by: upload</c>, and the deployments schema documents both keys.
    /// </summary>
    [JsonPropertyName("annotations")]
    public WorkerAnnotations? Annotations { get; set; }

    /// <summary>The version's bindings, exported handlers and runtime configuration.</summary>
    [JsonPropertyName("resources")]
    public WorkerVersionResources? Resources { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        Number is { } number ? $"version {number} ({Id})" : $"version {Id}";
}

/// <summary>The <c>resources</c> block of a single Worker version.</summary>
public sealed class WorkerVersionResources
{
    /// <summary>
    /// Bindings attached to this version. The reference publishes no item schema for this array and points
    /// at the multipart upload metadata documentation instead, so these are read with the same
    /// <see cref="WorkerBinding"/> vocabulary an upload writes - which carries its own
    /// <see cref="WorkerBinding.AdditionalData"/> for anything a binding type adds.
    /// </summary>
    [JsonPropertyName("bindings")]
    public List<WorkerBinding>? Bindings { get; set; }

    /// <summary>The uploaded script itself: its hash and the handlers it exports.</summary>
    [JsonPropertyName("script")]
    public WorkerVersionScript? Script { get; set; }

    /// <summary>Runtime configuration for the Worker.</summary>
    [JsonPropertyName("script_runtime")]
    public WorkerVersionScriptRuntime? ScriptRuntime { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString()
    {
        var handlers = Script?.Handlers?.Count ?? 0;
        var bindings = Bindings?.Count ?? 0;
        return $"{bindings} binding(s), {handlers} default handler(s)";
    }
}

/// <summary>The <c>resources.script</c> block of a single Worker version.</summary>
public sealed class WorkerVersionScript
{
    /// <summary>Hashed script content.</summary>
    [JsonPropertyName("etag")]
    public string? Etag { get; set; }

    /// <summary>The names of handlers exported as part of the default export.</summary>
    [JsonPropertyName("handlers")]
    public List<string>? Handlers { get; set; }

    /// <summary>The client most recently used to deploy this Worker.</summary>
    [JsonPropertyName("last_deployed_from")]
    public string? LastDeployedFrom { get; set; }

    /// <summary>Named exports, such as Durable Object classes and named entrypoints.</summary>
    [JsonPropertyName("named_handlers")]
    public List<WorkerNamedExport>? NamedHandlers { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>One named export of a Worker version.</summary>
public sealed class WorkerNamedExport
{
    /// <summary>The name of the exported class or entrypoint.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>The names of handlers exported as part of this named export.</summary>
    [JsonPropertyName("handlers")]
    public List<string>? Handlers { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"{Name}: {string.Join(", ", Handlers ?? new List<string>())}";
}

/// <summary>The <c>resources.script_runtime</c> block of a single Worker version.</summary>
public sealed class WorkerVersionScriptRuntime
{
    /// <summary>
    /// Date indicating targeted support in the Workers runtime. Backwards incompatible fixes after this
    /// date do not affect the Worker. A <c>yyyy-MM-dd</c> string rather than a timestamp, as published.
    /// </summary>
    [JsonPropertyName("compatibility_date")]
    public string? CompatibilityDate { get; set; }

    /// <summary>Flags that enable or disable features in the Workers runtime.</summary>
    [JsonPropertyName("compatibility_flags")]
    public List<string>? CompatibilityFlags { get; set; }

    /// <summary>Resource limits for the Worker.</summary>
    [JsonPropertyName("limits")]
    public WorkerVersionLimits? Limits { get; set; }

    /// <summary>The tag of the Durable Object migration most recently applied for this Worker.</summary>
    [JsonPropertyName("migration_tag")]
    public string? MigrationTag { get; set; }

    /// <summary>Usage model for the Worker's invocations. One of the <see cref="WorkerUsageModels"/>.</summary>
    [JsonPropertyName("usage_model")]
    public string? UsageModel { get; set; }

    /// <summary>
    /// Declarative exports for this version - Durable Object classes with their storage backend, tombstoned
    /// lifecycle entries and named Worker entrypoints - as a map from export name to its entry.
    /// </summary>
    /// <remarks>
    /// Deliberately left raw. The reference types each entry as a union of six variants distinguished by
    /// <c>type</c> and lifecycle <c>state</c>, several of which describe a Durable Object transfer in
    /// progress; modelling that from documentation alone, for a shape no live answer has shown this
    /// library, is how a wrong model gets shipped. The raw entries are readable through this dictionary and
    /// the integration run reports which keys and variants actually arrive.
    /// </remarks>
    [JsonPropertyName("exports")]
    public IDictionary<string, JsonElement>? Exports { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"{UsageModel ?? "runtime"}, compatibility {CompatibilityDate ?? "unset"}";
}

/// <summary>Resource limits for a Worker version.</summary>
public sealed class WorkerVersionLimits
{
    /// <summary>The amount of CPU time this Worker can use, in milliseconds.</summary>
    [JsonPropertyName("cpu_ms")]
    public int? CpuMs { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => CpuMs is { } cpu ? $"{cpu} ms CPU" : "no CPU limit reported";
}

