using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Http;

namespace CFNet.Models.Workers;

/// <summary>
/// Everything needed for <c>PUT /accounts/{account_id}/workers/scripts/{script_name}</c>.
/// </summary>
/// <remarks>
/// <para>
/// Cloudflare expects this call as <c>multipart/form-data</c> with two kinds of part:
/// </para>
/// <list type="bullet">
///   <item><description>a <c>metadata</c> part containing the JSON below, and</description></item>
///   <item>
///     <description>
///       one part per module whose field name <em>is</em> the module name, carrying the code with a
///       media type of <c>application/javascript+module</c> (or <c>text/x-python</c>, ...).
///     </description>
///   </item>
/// </list>
/// <para>
/// Only the members of this class marked with <see cref="JsonPropertyNameAttribute"/> end up inside the
/// metadata part; <see cref="Modules"/> and <see cref="ScriptName"/> drive the multipart layout instead.
/// A very common failure is uploading an ES module with the wrong media type - Cloudflare then reports
/// <c>10021: Main module name is not present in bundle</c> or an unexpected-token syntax error.
/// <see cref="Validate"/> catches the mistakes that produce those messages.
/// </para>
/// </remarks>
public sealed class UploadWorkerScriptRequest
{
    /// <summary>Creates a request for a single-module Worker.</summary>
    /// <param name="scriptName">Name the Worker will be deployed under.</param>
    /// <param name="mainModuleSource">JavaScript module source.</param>
    /// <param name="compatibilityDate">Runtime compatibility date, e.g. <c>2025-01-01</c>.</param>
    /// <param name="mainModuleName">File name of the entry module; defaults to <c>worker.mjs</c>.</param>
    public UploadWorkerScriptRequest(
        string scriptName,
        string mainModuleSource,
        string? compatibilityDate = null,
        string mainModuleName = "worker.mjs")
    {
        ScriptName = scriptName;
        MainModule = mainModuleName;
        CompatibilityDate = compatibilityDate;
        Modules = new List<WorkerModule>
        {
            new(mainModuleName, mainModuleSource, WorkerModuleContentTypes.JavaScriptModule),
        };
    }

    /// <summary>Parameterless constructor for object initialisers and deserialisation.</summary>
    [System.Text.Json.Serialization.JsonConstructor]
    public UploadWorkerScriptRequest()
    {
    }

    /// <summary>Worker name. Travels in the URL, not in the metadata part.</summary>
    [JsonIgnore]
    public string? ScriptName { get; set; }

    /// <summary>The modules that make up the bundle. Travels as separate multipart parts.</summary>
    [JsonIgnore]
    public List<WorkerModule>? Modules { get; set; }

    /// <summary>Name of the entry module; must match one of <see cref="Modules"/>.</summary>
    [JsonPropertyName("main_module")]
    public string? MainModule { get; set; }

    /// <summary>Name of the part holding a service-worker-syntax body. Mutually exclusive with <see cref="MainModule"/>.</summary>
    [JsonPropertyName("body_part")]
    public string? BodyPart { get; set; }

    /// <summary>Runtime compatibility date.</summary>
    [JsonPropertyName("compatibility_date")]
    public string? CompatibilityDate { get; set; }

    /// <summary>Runtime compatibility flags.</summary>
    [JsonPropertyName("compatibility_flags")]
    public List<string>? CompatibilityFlags { get; set; }

    /// <summary>Bindings to attach.</summary>
    [JsonPropertyName("bindings")]
    public List<WorkerBinding>? Bindings { get; set; }

    /// <summary>
    /// Binding types that should survive the upload unchanged. Without this, an upload that omits a
    /// binding kind removes every binding of that kind.
    /// </summary>
    [JsonPropertyName("keep_bindings")]
    public List<string>? KeepBindings { get; set; }

    /// <summary>Reuse the assets already uploaded for this Worker.</summary>
    [JsonPropertyName("keep_assets")]
    public bool? KeepAssets { get; set; }

    /// <summary>Static asset configuration (upload session JWT, routing behaviour).</summary>
    [JsonPropertyName("assets")]
    public WorkerAssetsConfiguration? Assets { get; set; }

    /// <summary>Tags attached to the script.</summary>
    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    /// <summary>Smart placement configuration.</summary>
    [JsonPropertyName("placement")]
    public WorkerPlacement? Placement { get; set; }

    /// <summary>Observability configuration.</summary>
    [JsonPropertyName("observability")]
    public WorkerObservability? Observability { get; set; }

    /// <summary>Whether to keep the Worker warm.</summary>
    [JsonPropertyName("prewarm_enabled")]
    public bool? PrewarmEnabled { get; set; }

    /// <summary>Enable Logpush for the script.</summary>
    [JsonPropertyName("logpush")]
    public bool? Logpush { get; set; }

    /// <summary>Billing/runtime model.</summary>
    [JsonPropertyName("usage_model")]
    public string? UsageModel { get; set; }

    /// <summary>Durable Object migrations to apply with this upload.</summary>
    [JsonPropertyName("migrations")]
    public List<WorkerMigration>? Migrations { get; set; }

    /// <summary>Additional metadata fields, merged into the metadata part as-is.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>
    /// Rejects the combinations Cloudflare refuses, with a message that explains the fix. Called
    /// automatically by the service before the request is sent.
    /// </summary>
    /// <exception cref="Exceptions.CloudflareConfigurationException">The request cannot succeed.</exception>
    public void Validate()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(ScriptName))
        {
            problems.Add($"{nameof(ScriptName)} is required.");
        }

        var isModuleWorker = !string.IsNullOrWhiteSpace(MainModule);
        var isServiceWorker = !string.IsNullOrWhiteSpace(BodyPart);

        if (!isModuleWorker && !isServiceWorker)
        {
            problems.Add($"Either {nameof(MainModule)} (module syntax) or {nameof(BodyPart)} (service worker syntax) must be set.");
        }

        if (isModuleWorker && isServiceWorker)
        {
            problems.Add($"{nameof(MainModule)} and {nameof(BodyPart)} are mutually exclusive.");
        }

        if (Modules is null || Modules.Count == 0)
        {
            problems.Add($"At least one entry in {nameof(Modules)} is required.");
        }
        else
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var module in Modules)
            {
                if (string.IsNullOrWhiteSpace(module.Name))
                {
                    problems.Add("Every module needs a name.");
                    continue;
                }

                if (!seen.Add(module.Name!))
                {
                    problems.Add($"Duplicate module name '{module.Name}'.");
                }

                if (module.Bytes is null && string.IsNullOrEmpty(module.ContentBase64))
                {
                    problems.Add($"Module '{module.Name}' has no content.");
                }

                if (string.IsNullOrWhiteSpace(module.ContentType))
                {
                    problems.Add(
                        $"Module '{module.Name}' has no content type. An ES module must be sent as " +
                        $"'{WorkerModuleContentTypes.JavaScriptModule}'.");
                }
            }

            if (isModuleWorker && !seen.Contains(MainModule!))
            {
                problems.Add(
                    $"'{nameof(MainModule)}' is '{MainModule}' but no module with that name was supplied. " +
                    "Cloudflare rejects this with error 10021.");
            }

            if (isServiceWorker && !seen.Contains(BodyPart!))
            {
                problems.Add($"'{nameof(BodyPart)}' is '{BodyPart}' but no module with that name was supplied.");
            }
        }

        if (isModuleWorker && CompatibilityDate is null && CompatibilityFlags is null)
        {
            // Not fatal, but Cloudflare then applies the oldest runtime, which surprises people.
            problems.Add(
                $"Consider setting {nameof(CompatibilityDate)} (for example '{DateTime.UtcNow:yyyy-MM-dd}'); " +
                "without it Cloudflare targets the oldest Workers runtime.");
        }

        if (problems.Count > 0)
        {
            throw new Exceptions.CloudflareConfigurationException(problems);
        }
    }
}

/// <summary>Static asset configuration for a Worker.</summary>
public sealed class WorkerAssetsConfiguration
{
    /// <summary>Completion token returned by the asset upload session endpoint.</summary>
    [JsonPropertyName("jwt")]
    public string? Jwt { get; set; }

    /// <summary>Routing behaviour for asset requests.</summary>
    [JsonPropertyName("config")]
    public WorkerAssetsRoutingConfiguration? Config { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>Asset routing behaviour.</summary>
public sealed class WorkerAssetsRoutingConfiguration
{
    /// <summary>
    /// How HTML requests are resolved: <c>auto-trailing-slash</c>, <c>force-trailing-slash</c>,
    /// <c>drop-trailing-slash</c> or <c>none</c>.
    /// </summary>
    [JsonPropertyName("html_handling")]
    public string? HtmlHandling { get; set; }

    /// <summary>Behaviour for missing assets: <c>auto-trailing-slash</c>, <c>404-page</c> or <c>single-page-application</c>.</summary>
    [JsonPropertyName("not_found_handling")]
    public string? NotFoundHandling { get; set; }

    /// <summary>Whether the Worker runs for requests that match an asset.</summary>
    [JsonPropertyName("run_worker_first")]
    public bool? RunWorkerFirst { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>A Durable Object migration applied during a script upload.</summary>
public sealed class WorkerMigration
{
    /// <summary>Migration tag.</summary>
    [JsonPropertyName("tag")]
    public string? Tag { get; set; }

    /// <summary>Durable Object classes introduced by this migration.</summary>
    [JsonPropertyName("new_classes")]
    public List<string>? NewClasses { get; set; }

    /// <summary>Durable Object classes removed by this migration.</summary>
    [JsonPropertyName("deleted_classes")]
    public List<string>? DeletedClasses { get; set; }

    /// <summary>Classes renamed by this migration.</summary>
    [JsonPropertyName("renamed_classes")]
    public List<WorkerRenamedClass>? RenamedClasses { get; set; }

    /// <summary>Apply the migration to existing storage.</summary>
    [JsonPropertyName("new_sqlite_classes")]
    public List<string>? NewSqliteClasses { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>A Durable Object class rename.</summary>
public sealed class WorkerRenamedClass
{
    /// <summary>Previous class name.</summary>
    [JsonPropertyName("from")]
    public string? From { get; set; }

    /// <summary>New class name.</summary>
    [JsonPropertyName("to")]
    public string? To { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>A Worker deployment (gradual rollout state).</summary>
public sealed class WorkerDeployment
{
    /// <summary>Deployment identifier.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Deployment number, monotonically increasing.</summary>
    [JsonPropertyName("number")]
    public int? Number { get; set; }

    /// <summary>Rollout strategy, e.g. <c>percentage</c>.</summary>
    [JsonPropertyName("strategy")]
    public string? Strategy { get; set; }

    /// <summary>Annotations attached to the deployment.</summary>
    [JsonPropertyName("annotations")]
    public JsonElement? Annotations { get; set; }

    /// <summary>Versions taking traffic, with their percentages.</summary>
    [JsonPropertyName("versions")]
    public List<WorkerDeploymentVersion>? Versions { get; set; }

    /// <summary>Creation timestamp.</summary>
    [JsonPropertyName("created_on")]
    public DateTimeOffset? CreatedOn { get; set; }

    /// <summary>Last modification timestamp.</summary>
    [JsonPropertyName("modified_on")]
    public DateTimeOffset? ModifiedOn { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>One version inside a Worker deployment.</summary>
public sealed class WorkerDeploymentVersion
{
    /// <summary>Version identifier.</summary>
    [JsonPropertyName("version_id")]
    public string? VersionId { get; set; }

    /// <summary>Percentage of traffic routed to this version.</summary>
    [JsonPropertyName("percentage")]
    public double? Percentage { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>Request body of <c>POST /accounts/{account_id}/workers/scripts/{script_name}/deployments</c>.</summary>
public sealed class CreateWorkerDeploymentRequest
{
    /// <summary>Creates a request that routes all traffic to one version.</summary>
    /// <param name="versionId">The version to deploy.</param>
    public static CreateWorkerDeploymentRequest SingleVersion(string versionId) => new()
    {
        Strategy = "percentage",
        Versions = new List<WorkerDeploymentVersion>
        {
            new() { VersionId = versionId, Percentage = 100 },
        },
    };

    /// <summary>Rollout strategy; currently <c>percentage</c>.</summary>
    [JsonPropertyName("strategy")]
    public string? Strategy { get; set; }

    /// <summary>Versions taking traffic. Percentages must add up to 100.</summary>
    [JsonPropertyName("versions")]
    public List<WorkerDeploymentVersion>? Versions { get; set; }

    /// <summary>Free form annotations.</summary>
    [JsonPropertyName("annotations")]
    public JsonElement? Annotations { get; set; }
}

/// <summary>Query filters for <c>GET .../workers/scripts/{script_name}/deployments</c>.</summary>
public sealed class WorkerDeploymentListParameters : CloudflareCursorParameters
{
    /// <summary>Field to sort by.</summary>
    public string? Order { get; set; }

    /// <summary>Sort direction.</summary>
    public SortDirection? Direction { get; set; }

    /// <inheritdoc />
    public override void Apply(QueryStringBuilder query)
    {
        base.Apply(query);
        query.AddIfNotEmpty("order", Order);
        if (Direction.HasValue)
        {
            query.Add("direction", QueryEnums.ToWire(Direction.Value));
        }
    }
}

/// <summary>
/// Account level <c>*.workers.dev</c> subdomain, from
/// <c>GET /accounts/{account_id}/workers/subdomain</c>.
/// </summary>
public sealed class WorkerSubdomain
{
    /// <summary>The subdomain itself, e.g. <c>my-team</c>.</summary>
    [JsonPropertyName("subdomain")]
    public string? Subdomain { get; set; }

    /// <summary>Whether <c>*.workers.dev</c> deployments are enabled for the account.</summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    /// <summary>Whether pre-release (preview) URLs are enabled.</summary>
    [JsonPropertyName("previews_enabled")]
    public bool? PreviewsEnabled { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>Request body of <c>PATCH /accounts/{account_id}/workers/subdomain</c>.</summary>
public sealed class UpdateWorkerSubdomainRequest
{
    /// <summary>New subdomain value.</summary>
    [JsonPropertyName("subdomain")]
    public string? Subdomain { get; set; }

    /// <summary>Enable or disable <c>*.workers.dev</c> deployments.</summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    /// <summary>Enable or disable preview URLs.</summary>
    [JsonPropertyName("previews_enabled")]
    public bool? PreviewsEnabled { get; set; }
}

/// <summary>
/// Per-script <c>workers.dev</c> route state, from
/// <c>GET /accounts/{account_id}/workers/scripts/{script_name}/subdomain</c>.
/// </summary>
public sealed class WorkerScriptSubdomain
{
    /// <summary>Whether the script is published on <c>*.workers.dev</c>.</summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    /// <summary>Whether preview URLs are enabled for the script.</summary>
    [JsonPropertyName("previews_enabled")]
    public bool? PreviewsEnabled { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>Request body of <c>POST .../workers/scripts/{script_name}/subdomain</c>.</summary>
public sealed class UpdateWorkerScriptSubdomainRequest
{
    /// <summary>Creates a request that publishes or unpublishes the script.</summary>
    /// <param name="enabled">Whether the script should be reachable on <c>*.workers.dev</c>.</param>
    /// <param name="previewsEnabled">Whether preview URLs should be enabled.</param>
    public UpdateWorkerScriptSubdomainRequest(bool enabled, bool? previewsEnabled = null)
    {
        Enabled = enabled;
        PreviewsEnabled = previewsEnabled;
    }

    /// <summary>Parameterless constructor for object initialisers.</summary>
    [System.Text.Json.Serialization.JsonConstructor]
    public UpdateWorkerScriptSubdomainRequest()
    {
    }

    /// <summary>Whether the script is published on <c>*.workers.dev</c>.</summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    /// <summary>Whether preview URLs are enabled.</summary>
    [JsonPropertyName("previews_enabled")]
    public bool? PreviewsEnabled { get; set; }
}
