using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Http;
using CFNet.Models.Common;

namespace CFNet.Models.Pages;

/// <summary>
/// A Cloudflare Pages project, from <c>GET /accounts/{account_id}/pages/projects</c>.
/// </summary>
public sealed class PagesProject
{
    /// <summary>Project identifier.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Project name. Most Pages endpoints address the project by name, not by id.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Owning account.</summary>
    [JsonPropertyName("account_id")]
    public string? AccountId { get; set; }

    /// <summary>The generated <c>*.pages.dev</c> hostname.</summary>
    [JsonPropertyName("subdomain")]
    public string? Subdomain { get; set; }

    /// <summary>Custom domains attached to the project.</summary>
    [JsonPropertyName("domains")]
    public List<string>? Domains { get; set; }

    /// <summary>Branch whose deployments are treated as production.</summary>
    [JsonPropertyName("production_branch")]
    public string? ProductionBranch { get; set; }

    /// <summary>Git integration configuration, when the project is connected to a repository.</summary>
    [JsonPropertyName("source")]
    public PagesProjectSource? Source { get; set; }

    /// <summary>Build configuration.</summary>
    [JsonPropertyName("build_config")]
    public PagesBuildConfig? BuildConfig { get; set; }

    /// <summary>Per-environment deployment configuration (preview and production).</summary>
    [JsonPropertyName("deployment_configs")]
    public PagesDeploymentConfigs? DeploymentConfigs { get; set; }

    /// <summary>The deployment currently serving production traffic.</summary>
    [JsonPropertyName("canonical_deployment")]
    public PagesDeployment? CanonicalDeployment { get; set; }

    /// <summary>The most recent deployment, which may still be building.</summary>
    [JsonPropertyName("latest_deployment")]
    public PagesDeployment? LatestDeployment { get; set; }

    /// <summary>Name of the underlying Worker script.</summary>
    [JsonPropertyName("script_name")]
    public string? ScriptName { get; set; }

    /// <summary>Creation timestamp.</summary>
    [JsonPropertyName("created_on")]
    public DateTimeOffset? CreatedOn { get; set; }

    /// <summary>Last modification timestamp.</summary>
    [JsonPropertyName("modified_on")]
    public DateTimeOffset? ModifiedOn { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => Name ?? Id ?? string.Empty;
}

/// <summary>Git source configuration of a Pages project.</summary>
public sealed class PagesProjectSource
{
    /// <summary>Source type, e.g. <c>github</c> or <c>gitlab</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Repository URL.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>Provider specific configuration.</summary>
    [JsonPropertyName("config")]
    public PagesSourceConfig? Config { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>Repository details inside <see cref="PagesProjectSource"/>.</summary>
public sealed class PagesSourceConfig
{
    /// <summary>Owner of the repository.</summary>
    [JsonPropertyName("owner")]
    public string? Owner { get; set; }

    /// <summary>Repository name.</summary>
    [JsonPropertyName("repo_name")]
    public string? RepoName { get; set; }

    /// <summary>Branch that triggers production deployments.</summary>
    [JsonPropertyName("production_branch")]
    public string? ProductionBranch { get; set; }

    /// <summary>Branch patterns that should not be deployed.</summary>
    [JsonPropertyName("pr_comments_enabled")]
    public bool? PrCommentsEnabled { get; set; }

    /// <summary>Whether deployments are created on push.</summary>
    [JsonPropertyName("deployments_enabled")]
    public bool? DeploymentsEnabled { get; set; }

    /// <summary>Branches excluded from preview deployments.</summary>
    [JsonPropertyName("preview_branch_excludes")]
    public List<string>? PreviewBranchExcludes { get; set; }

    /// <summary>Branches included in preview deployments.</summary>
    [JsonPropertyName("preview_branch_includes")]
    public List<string>? PreviewBranchIncludes { get; set; }

    /// <summary>How preview branches are selected.</summary>
    [JsonPropertyName("preview_deployment_setting")]
    public string? PreviewDeploymentSetting { get; set; }

    /// <summary>Whether production deployments are created from all branches.</summary>
    [JsonPropertyName("production_deployment_setting")]
    public string? ProductionDeploymentSetting { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>Build configuration of a Pages project.</summary>
public sealed class PagesBuildConfig
{
    /// <summary>Build command, e.g. <c>npm run build</c>.</summary>
    [JsonPropertyName("build_command")]
    public string? BuildCommand { get; set; }

    /// <summary>Directory containing the built output, e.g. <c>dist</c>.</summary>
    [JsonPropertyName("destination_dir")]
    public string? DestinationDir { get; set; }

    /// <summary>Directory the build command runs in.</summary>
    [JsonPropertyName("root_dir")]
    public string? RootDir { get; set; }

    /// <summary>Whether build caching is enabled.</summary>
    [JsonPropertyName("build_caching")]
    public bool? BuildCaching { get; set; }

    /// <summary>Web Analytics tag.</summary>
    [JsonPropertyName("web_analytics_tag")]
    public string? WebAnalyticsTag { get; set; }

    /// <summary>Web Analytics token.</summary>
    [JsonPropertyName("web_analytics_token")]
    public string? WebAnalyticsToken { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>The <c>deployment_configs</c> block: one configuration per environment.</summary>
public sealed class PagesDeploymentConfigs
{
    /// <summary>Configuration applied to preview deployments.</summary>
    [JsonPropertyName("preview")]
    public PagesDeploymentConfig? Preview { get; set; }

    /// <summary>Configuration applied to production deployments.</summary>
    [JsonPropertyName("production")]
    public PagesDeploymentConfig? Production { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>
/// Runtime configuration of one Pages environment. Bindings are dictionaries keyed by the binding
/// name, which is the shape Cloudflare uses here - unlike Workers, where bindings are an array.
/// </summary>
public sealed class PagesDeploymentConfig
{
    /// <summary>Environment variables and secrets, keyed by name.</summary>
    [JsonPropertyName("env_vars")]
    public Dictionary<string, PagesEnvVar>? EnvVars { get; set; }

    /// <summary>Runtime compatibility date.</summary>
    [JsonPropertyName("compatibility_date")]
    public string? CompatibilityDate { get; set; }

    /// <summary>Runtime compatibility flags.</summary>
    [JsonPropertyName("compatibility_flags")]
    public List<string>? CompatibilityFlags { get; set; }

    /// <summary>Always use the newest compatibility date.</summary>
    [JsonPropertyName("always_use_latest_compatibility_date")]
    public bool? AlwaysUseLatestCompatibilityDate { get; set; }

    /// <summary>Billing/runtime model.</summary>
    [JsonPropertyName("usage_model")]
    public string? UsageModel { get; set; }

    /// <summary>Serve stale content when a Function throws.</summary>
    [JsonPropertyName("fail_open")]
    public bool? FailOpen { get; set; }

    /// <summary>KV namespace bindings, keyed by binding name.</summary>
    [JsonPropertyName("kv_namespaces")]
    public Dictionary<string, PagesKvNamespaceBinding>? KvNamespaces { get; set; }

    /// <summary>D1 database bindings, keyed by binding name.</summary>
    [JsonPropertyName("d1_databases")]
    public Dictionary<string, PagesD1Binding>? D1Databases { get; set; }

    /// <summary>R2 bucket bindings, keyed by binding name.</summary>
    [JsonPropertyName("r2_buckets")]
    public Dictionary<string, PagesR2Binding>? R2Buckets { get; set; }

    /// <summary>Durable Object namespace bindings, keyed by binding name.</summary>
    [JsonPropertyName("durable_object_namespaces")]
    public Dictionary<string, PagesDurableObjectBinding>? DurableObjectNamespaces { get; set; }

    /// <summary>Queue producer bindings, keyed by binding name.</summary>
    [JsonPropertyName("queue_producers")]
    public Dictionary<string, PagesQueueBinding>? QueueProducers { get; set; }

    /// <summary>Queue consumer bindings, keyed by queue name.</summary>
    [JsonPropertyName("queue_consumers")]
    public Dictionary<string, PagesQueueConsumerBinding>? QueueConsumers { get; set; }

    /// <summary>Service bindings, keyed by binding name.</summary>
    [JsonPropertyName("services")]
    public Dictionary<string, PagesServiceBinding>? Services { get; set; }

    /// <summary>Observability configuration.</summary>
    [JsonPropertyName("observability")]
    public Workers.WorkerObservability? Observability { get; set; }

    /// <summary>Smart placement configuration.</summary>
    [JsonPropertyName("placement")]
    public Workers.WorkerPlacement? Placement { get; set; }

    /// <summary>Resource limits.</summary>
    [JsonPropertyName("limits")]
    public Workers.WorkerLimits? Limits { get; set; }

    /// <summary>Major version of the build image.</summary>
    [JsonPropertyName("build_image_major_version")]
    public int? BuildImageMajorVersion { get; set; }

    /// <summary>Analytics Engine dataset bindings, kept untyped because the shape varies.</summary>
    [JsonPropertyName("analytics_engine_datasets")]
    public JsonElement? AnalyticsEngineDatasets { get; set; }

    /// <summary>Browser Rendering bindings.</summary>
    [JsonPropertyName("browsers")]
    public JsonElement? Browsers { get; set; }

    /// <summary>Workers AI bindings.</summary>
    [JsonPropertyName("ai_bindings")]
    public JsonElement? AiBindings { get; set; }

    /// <summary>Vectorize bindings.</summary>
    [JsonPropertyName("vectorize")]
    public JsonElement? Vectorize { get; set; }

    /// <summary>Hyperdrive bindings.</summary>
    [JsonPropertyName("hyperdrive_bindings")]
    public JsonElement? HyperdriveBindings { get; set; }

    /// <summary>Log forwarding bindings.</summary>
    [JsonPropertyName("logfwdr")]
    public JsonElement? LogForwarder { get; set; }

    /// <summary>mTLS certificate bindings.</summary>
    [JsonPropertyName("mtls_certificates")]
    public JsonElement? MtlsCertificates { get; set; }

    /// <summary>Bindings Cloudflare does not model publicly yet.</summary>
    [JsonPropertyName("unsafe_bindings")]
    public JsonElement? UnsafeBindings { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>An environment variable or secret inside <see cref="PagesDeploymentConfig.EnvVars"/>.</summary>
public sealed class PagesEnvVar
{
    /// <summary>Creates a plain text variable.</summary>
    /// <param name="value">Variable value.</param>
    public static PagesEnvVar PlainText(string value) => new() { Type = "plain_text", Value = value };

    /// <summary>Creates a secret variable.</summary>
    /// <param name="value">Secret value.</param>
    public static PagesEnvVar SecretText(string value) => new() { Type = "secret_text", Value = value };

    /// <summary>Variable kind: <c>plain_text</c>, <c>secret_text</c> or <c>json</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Variable value. Secrets are never returned by the API.</summary>
    /// <remarks>
    /// Typed as <see cref="object"/> because Pages accepts a string, a number or a JSON object here
    /// depending on <see cref="Type"/>. System.Text.Json hands back a <see cref="JsonElement"/>, so
    /// read it through <see cref="ValueAsString"/> unless you need the raw element.
    /// </remarks>
    [JsonPropertyName("value")]
    public object? Value { get; set; }

    /// <summary>
    /// <see cref="Value"/> rendered as text, whatever JSON kind Cloudflare sent: the string itself,
    /// a number's literal, or the raw JSON of an object.
    /// </summary>
    [JsonIgnore]
    public string? ValueAsString
    {
        get
        {
            var value = Value;
            return value switch
            {
                null => null,
                string text => text,
                JsonElement element => element.ValueKind switch
                {
                    JsonValueKind.Null or JsonValueKind.Undefined => null,
                    JsonValueKind.String => element.GetString(),
                    _ => element.GetRawText(),
                },
                _ => value.ToString(),
            };
        }
    }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>A KV binding inside a Pages deployment config.</summary>
public sealed class PagesKvNamespaceBinding
{
    /// <summary>KV namespace id.</summary>
    [JsonPropertyName("namespace_id")]
    public string? NamespaceId { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>A D1 binding inside a Pages deployment config.</summary>
public sealed class PagesD1Binding
{
    /// <summary>D1 database id.</summary>
    [JsonPropertyName("database_id")]
    public string? DatabaseId { get; set; }

    /// <summary>Directory holding migration files.</summary>
    [JsonPropertyName("migrations_dir")]
    public string? MigrationsDir { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>An R2 binding inside a Pages deployment config.</summary>
public sealed class PagesR2Binding
{
    /// <summary>R2 bucket name.</summary>
    [JsonPropertyName("bucket_name")]
    public string? BucketName { get; set; }

    /// <summary>Bucket jurisdiction.</summary>
    [JsonPropertyName("jurisdiction")]
    public string? Jurisdiction { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>A Durable Object binding inside a Pages deployment config.</summary>
public sealed class PagesDurableObjectBinding
{
    /// <summary>Durable Object class name.</summary>
    [JsonPropertyName("class_name")]
    public string? ClassName { get; set; }

    /// <summary>Script hosting the class.</summary>
    [JsonPropertyName("script_name")]
    public string? ScriptName { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>A queue producer binding inside a Pages deployment config.</summary>
public sealed class PagesQueueBinding
{
    /// <summary>Queue name.</summary>
    [JsonPropertyName("queue_name")]
    public string? QueueName { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>A queue consumer binding inside a Pages deployment config.</summary>
public sealed class PagesQueueConsumerBinding
{
    /// <summary>Queue name.</summary>
    [JsonPropertyName("queue_name")]
    public string? QueueName { get; set; }

    /// <summary>Largest batch delivered to the consumer.</summary>
    [JsonPropertyName("max_batch_size")]
    public int? MaxBatchSize { get; set; }

    /// <summary>Longest wait before a partial batch is delivered.</summary>
    [JsonPropertyName("max_batch_timeout")]
    public int? MaxBatchTimeout { get; set; }

    /// <summary>Number of delivery attempts.</summary>
    [JsonPropertyName("max_retries")]
    public int? MaxRetries { get; set; }

    /// <summary>Dead letter queue name.</summary>
    [JsonPropertyName("dead_letter_queue")]
    public string? DeadLetterQueue { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>A service binding inside a Pages deployment config.</summary>
public sealed class PagesServiceBinding
{
    /// <summary>Target Worker name.</summary>
    [JsonPropertyName("service")]
    public string? Service { get; set; }

    /// <summary>Target environment.</summary>
    [JsonPropertyName("environment")]
    public string? Environment { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>Query filters for <c>GET /accounts/{account_id}/pages/projects</c>.</summary>
public sealed class PagesProjectListParameters : CloudflarePagedParameters
{
    /// <summary>Substring to search for in project names.</summary>
    public string? Search { get; set; }

    /// <inheritdoc />
    public override void Apply(QueryStringBuilder query)
    {
        base.Apply(query);
        query.AddIfNotEmpty("search", Search);
    }
}
