using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Models.Workers;

/// <summary>
/// One binding attached to a Worker: environment variables, secrets and references to KV, R2, D1,
/// Queues, other Workers and so on.
/// </summary>
/// <remarks>
/// A single type covers every binding kind because Cloudflare models them as a discriminated union
/// with a <c>type</c> field; each kind only populates a handful of properties. Use the factory methods
/// (<see cref="PlainText"/>, <see cref="KvNamespace"/>, ...) rather than filling the members by hand -
/// they know which property each binding type expects.
/// <para>
/// <see cref="Type"/> is deliberately a <c>string</c> rather than an enum: Cloudflare adds binding
/// kinds regularly and an enum would make an unknown value a hard failure. <see cref="WorkerBindingTypes"/>
/// lists the literals this library knows about.
/// </para>
/// </remarks>
public sealed class WorkerBinding
{
    /// <summary>The name the script uses to reach the binding (<c>env.MY_KV</c>).</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Binding kind literal. See <see cref="WorkerBindingTypes"/>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Value of a <c>plain_text</c> or <c>secret_text</c> binding.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>KV namespace id, for <c>kv_namespace</c> bindings.</summary>
    [JsonPropertyName("namespace_id")]
    public string? NamespaceId { get; set; }

    /// <summary>D1 database id, for D1 bindings.</summary>
    [JsonPropertyName("database_id")]
    public string? DatabaseId { get; set; }

    /// <summary>R2 bucket name, for <c>r2_bucket</c> bindings.</summary>
    [JsonPropertyName("bucket_name")]
    public string? BucketName { get; set; }

    /// <summary>R2 jurisdiction, for jurisdiction restricted buckets.</summary>
    [JsonPropertyName("jurisdiction")]
    public string? Jurisdiction { get; set; }

    /// <summary>Queue name, for <c>queue</c> bindings.</summary>
    [JsonPropertyName("queue_name")]
    public string? QueueName { get; set; }

    /// <summary>Queue id, returned by the API for existing bindings.</summary>
    [JsonPropertyName("queue_id")]
    public string? QueueId { get; set; }

    /// <summary>Target script name, for <c>service</c> bindings.</summary>
    [JsonPropertyName("service")]
    public string? Service { get; set; }

    /// <summary>Target environment, for <c>service</c> bindings.</summary>
    [JsonPropertyName("environment")]
    public string? Environment { get; set; }

    /// <summary>Dispatch namespace, for Workers for Platforms service bindings.</summary>
    [JsonPropertyName("namespace")]
    public string? Namespace { get; set; }

    /// <summary>Analytics Engine dataset, for <c>analytics_engine</c> bindings.</summary>
    [JsonPropertyName("dataset")]
    public string? Dataset { get; set; }

    /// <summary>Vectorize index name, for <c>vectorize</c> bindings.</summary>
    [JsonPropertyName("index_name")]
    public string? IndexName { get; set; }

    /// <summary>Hyperdrive configuration id, for <c>hyperdrive</c> bindings.</summary>
    [JsonPropertyName("hyperdrive_id")]
    public string? HyperdriveId { get; set; }

    /// <summary>Durable Object class name, for <c>do_namespace</c> bindings.</summary>
    [JsonPropertyName("class_name")]
    public string? ClassName { get; set; }

    /// <summary>Script hosting the Durable Object class, when it lives in another Worker.</summary>
    [JsonPropertyName("script_name")]
    public string? ScriptName { get; set; }

    /// <summary>Module name for module based bindings (WASM, text blobs).</summary>
    [JsonPropertyName("module")]
    public string? Module { get; set; }

    /// <summary>Zone name, for bindings that target a zone.</summary>
    [JsonPropertyName("zone_name")]
    public string? ZoneName { get; set; }

    /// <summary>Arbitrary JSON payload, for <c>json</c> bindings.</summary>
    [JsonPropertyName("json")]
    public object? Json { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>True when the binding holds a secret and its value is not returned by the API.</summary>
    [JsonIgnore]
    public bool IsSecret =>
        Type is WorkerBindingTypes.SecretText or WorkerBindingTypes.SecretBytes or WorkerBindingTypes.MtlsCertificate;

    /// <summary>Creates a <c>plain_text</c> binding (an environment variable).</summary>
    /// <param name="name">Binding name.</param>
    /// <param name="text">Value.</param>
    public static WorkerBinding PlainText(string name, string text) =>
        new() { Type = WorkerBindingTypes.PlainText, Name = name, Text = text };

    /// <summary>
    /// Creates a <c>secret_text</c> binding. Cloudflare stores the value encrypted and never returns
    /// it again.
    /// </summary>
    /// <param name="name">Binding name.</param>
    /// <param name="text">Secret value.</param>
    public static WorkerBinding SecretText(string name, string text) =>
        new() { Type = WorkerBindingTypes.SecretText, Name = name, Text = text };

    /// <summary>Creates a <c>kv_namespace</c> binding.</summary>
    /// <param name="name">Binding name.</param>
    /// <param name="namespaceId">KV namespace id.</param>
    public static WorkerBinding KvNamespace(string name, string namespaceId) =>
        new() { Type = WorkerBindingTypes.KvNamespace, Name = name, NamespaceId = namespaceId };

    /// <summary>Creates a <c>r2_bucket</c> binding.</summary>
    /// <param name="name">Binding name.</param>
    /// <param name="bucketName">Bucket name.</param>
    /// <param name="jurisdiction">Optional jurisdiction for restricted buckets.</param>
    public static WorkerBinding R2Bucket(string name, string bucketName, string? jurisdiction = null) =>
        new() { Type = WorkerBindingTypes.R2Bucket, Name = name, BucketName = bucketName, Jurisdiction = jurisdiction };

    /// <summary>
    /// Creates a D1 binding. Cloudflare labels these <c>d1_database_binding</c> in the API; the
    /// database id goes into <see cref="DatabaseId"/>.
    /// </summary>
    /// <param name="name">Binding name.</param>
    /// <param name="databaseId">D1 database id (UUID).</param>
    public static WorkerBinding D1Database(string name, string databaseId) =>
        new() { Type = WorkerBindingTypes.D1DatabaseBinding, Name = name, DatabaseId = databaseId };

    /// <summary>Creates a <c>service</c> binding to another Worker.</summary>
    /// <param name="name">Binding name.</param>
    /// <param name="service">Target Worker name.</param>
    /// <param name="environment">Optional target environment.</param>
    public static WorkerBinding ToService(string name, string service, string? environment = null) =>
        new() { Type = WorkerBindingTypes.Service, Name = name, Service = service, Environment = environment };

    /// <summary>Creates a <c>queue</c> producer binding.</summary>
    /// <param name="name">Binding name.</param>
    /// <param name="queueName">Queue name.</param>
    public static WorkerBinding Queue(string name, string queueName) =>
        new() { Type = WorkerBindingTypes.Queue, Name = name, QueueName = queueName };

    /// <summary>Creates a <c>do_namespace</c> binding for a Durable Object class in this Worker.</summary>
    /// <param name="name">Binding name.</param>
    /// <param name="className">Durable Object class name.</param>
    /// <param name="scriptName">Set when the class lives in a different Worker.</param>
    public static WorkerBinding DurableObject(string name, string className, string? scriptName = null) =>
        new() { Type = WorkerBindingTypes.DoNamespace, Name = name, ClassName = className, ScriptName = scriptName };

    /// <summary>Creates an <c>analytics_engine</c> binding.</summary>
    /// <param name="name">Binding name.</param>
    /// <param name="dataset">Analytics Engine dataset.</param>
    public static WorkerBinding AnalyticsEngine(string name, string dataset) =>
        new() { Type = WorkerBindingTypes.AnalyticsEngine, Name = name, Dataset = dataset };

    /// <summary>Creates an <c>assets</c> binding for static assets.</summary>
    /// <param name="name">Binding name; conventionally <c>ASSETS</c>.</param>
    public static WorkerBinding Assets(string name = "ASSETS") =>
        new() { Type = WorkerBindingTypes.Assets, Name = name };

    /// <summary>Creates a <c>browser</c> binding for Browser Rendering.</summary>
    /// <param name="name">Binding name.</param>
    public static WorkerBinding Browser(string name) =>
        new() { Type = WorkerBindingTypes.Browser, Name = name };

    /// <summary>Creates a <c>vectorize</c> binding.</summary>
    /// <param name="name">Binding name.</param>
    /// <param name="indexName">Vectorize index name.</param>
    public static WorkerBinding Vectorize(string name, string indexName) =>
        new() { Type = WorkerBindingTypes.Vectorize, Name = name, IndexName = indexName };

    /// <summary>Creates a <c>hyperdrive</c> binding.</summary>
    /// <param name="name">Binding name.</param>
    /// <param name="hyperdriveId">Hyperdrive configuration id.</param>
    public static WorkerBinding Hyperdrive(string name, string hyperdriveId) =>
        new() { Type = WorkerBindingTypes.Hyperdrive, Name = name, HyperdriveId = hyperdriveId };

    /// <summary>Creates an <c>ai</c> binding for Workers AI.</summary>
    /// <param name="name">Binding name.</param>
    public static WorkerBinding Ai(string name = "AI") =>
        new() { Type = WorkerBindingTypes.Ai, Name = name };

    /// <inheritdoc />
    public override string ToString() => $"{Type}:{Name}";
}

/// <summary>
/// Literal <c>type</c> values Cloudflare uses for Worker bindings.
/// </summary>
public static class WorkerBindingTypes
{
    /// <summary>Plain environment variable.</summary>
    public const string PlainText = "plain_text";

    /// <summary>Encrypted secret.</summary>
    public const string SecretText = "secret_text";

    /// <summary>Encrypted binary secret.</summary>
    public const string SecretBytes = "secret_bytes";

    /// <summary>KV namespace.</summary>
    public const string KvNamespace = "kv_namespace";

    /// <summary>Durable Object namespace.</summary>
    public const string DoNamespace = "do_namespace";

    /// <summary>D1 database.</summary>
    public const string D1DatabaseBinding = "d1_database_binding";

    /// <summary>R2 bucket.</summary>
    public const string R2Bucket = "r2_bucket";

    /// <summary>Queue producer.</summary>
    public const string Queue = "queue";

    /// <summary>Service binding to another Worker.</summary>
    public const string Service = "service";

    /// <summary>Analytics Engine dataset.</summary>
    public const string AnalyticsEngine = "analytics_engine";

    /// <summary>Static assets.</summary>
    public const string Assets = "assets";

    /// <summary>Browser Rendering.</summary>
    public const string Browser = "browser";

    /// <summary>Vectorize index.</summary>
    public const string Vectorize = "vectorize";

    /// <summary>Hyperdrive configuration.</summary>
    public const string Hyperdrive = "hyperdrive";

    /// <summary>Workers AI.</summary>
    public const string Ai = "ai";

    /// <summary>Log forwarding.</summary>
    public const string LogForwarder = "logfwdr";

    /// <summary>Rate limiting.</summary>
    public const string RateLimit = "ratelimit";

    /// <summary>mTLS certificate.</summary>
    public const string MtlsCertificate = "mtls_certificate";

    /// <summary>Inline JSON value.</summary>
    public const string Json = "json";

    /// <summary>WASM module.</summary>
    public const string WasmModule = "wasm_module";

    /// <summary>Text blob module.</summary>
    public const string TextBlob = "text_blob";

    /// <summary>Buffer module.</summary>
    public const string Buffer = "buffer";

    /// <summary>Base64 module.</summary>
    public const string Base64 = "base64";

    /// <summary>Workflow.</summary>
    public const string Workflow = "workflow";
}

/// <summary>
/// One module of a Worker bundle. A Worker is a set of named modules, one of which is the
/// <c>main_module</c>.
/// </summary>
public sealed class WorkerModule
{
    /// <summary>
    /// Creates an empty module. Also the constructor the serializer uses, since this type has more
    /// than one and System.Text.Json needs to be told which one to pick.
    /// </summary>
    [JsonConstructor]
    public WorkerModule()
    {
    }

    /// <summary>Creates a module from text content.</summary>
    /// <param name="name">Module name, e.g. <c>worker.mjs</c>.</param>
    /// <param name="content">Module source.</param>
    /// <param name="contentType">Media type; see <see cref="WorkerModuleContentTypes"/>.</param>
    public WorkerModule(string name, string content, string contentType = WorkerModuleContentTypes.JavaScriptModule)
    {
        Name = name;
        ContentType = contentType;
        Bytes = Encoding.UTF8.GetBytes(content ?? string.Empty);
    }

    /// <summary>Creates a module from raw bytes.</summary>
    /// <param name="name">Module name.</param>
    /// <param name="content">Module bytes.</param>
    /// <param name="contentType">Media type.</param>
    public WorkerModule(string name, byte[] content, string contentType)
    {
        Name = name;
        ContentType = contentType;
        Bytes = content;
    }

    /// <summary>Module name. This is also the multipart field name used on upload.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Media type of the module.</summary>
    [JsonPropertyName("content_type")]
    public string? ContentType { get; set; }

    /// <summary>Hash of the module content, returned by the API.</summary>
    [JsonPropertyName("etag")]
    public string? Etag { get; set; }

    /// <summary>Size in bytes, returned by the API.</summary>
    [JsonPropertyName("size")]
    public long? Size { get; set; }

    /// <summary>
    /// Module payload. Not serialised: on upload the bytes travel as their own multipart part, and the
    /// download endpoint returns them as <see cref="ContentBase64"/>.
    /// </summary>
    [JsonIgnore]
    public byte[]? Bytes { get; set; }

    /// <summary>Base64 encoded payload, used by the Worker Versions endpoints.</summary>
    [JsonPropertyName("content_base64")]
    public string? ContentBase64 { get; set; }

    /// <summary>The module source as UTF-8 text, or <c>null</c> when the payload is binary.</summary>
    [JsonIgnore]
    public string? Text => Bytes is null ? null : Encoding.UTF8.GetString(Bytes);

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"{Name} ({ContentType})";
}

/// <summary>Media types Cloudflare accepts for Worker modules.</summary>
public static class WorkerModuleContentTypes
{
    /// <summary>An ES module. Required for <c>export default</c> syntax.</summary>
    public const string JavaScriptModule = "application/javascript+module";

    /// <summary>A classic (service worker syntax) JavaScript file.</summary>
    public const string JavaScript = "application/javascript";

    /// <summary>A Python Worker module.</summary>
    public const string Python = "text/x-python";

    /// <summary>A WASM binary.</summary>
    public const string Wasm = "application/wasm";

    /// <summary>An opaque binary buffer.</summary>
    public const string Buffer = "buffer";

    /// <summary>Base64 encoded data.</summary>
    public const string Base64 = "base64";

    /// <summary>Plain text.</summary>
    public const string Text = "text";
}

/// <summary>
/// The <c>result</c> of <c>GET /accounts/{account_id}/workers/scripts/{script_name}/content</c>:
/// the module list that makes up the deployed bundle.
/// </summary>
public sealed class WorkerScriptContent
{
    /// <summary>Name of the entry module.</summary>
    [JsonPropertyName("main_module")]
    public string? MainModule { get; set; }

    /// <summary>All modules in the bundle.</summary>
    [JsonPropertyName("modules")]
    public List<WorkerModule>? Modules { get; set; }

    /// <summary>Runtime compatibility date.</summary>
    [JsonPropertyName("compatibility_date")]
    public string? CompatibilityDate { get; set; }

    /// <summary>Runtime compatibility flags.</summary>
    [JsonPropertyName("compatibility_flags")]
    public List<string>? CompatibilityFlags { get; set; }

    /// <summary>Name of the multipart part holding the body, for service worker syntax scripts.</summary>
    [JsonPropertyName("body_part")]
    public string? BodyPart { get; set; }

    /// <summary>Bindings configured on the deployed version.</summary>
    [JsonPropertyName("bindings")]
    public List<WorkerBinding>? Bindings { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>
/// Script level settings returned by
/// <c>GET /accounts/{account_id}/workers/scripts/{script_name}/script-settings</c>.
/// </summary>
public sealed class WorkerScriptSettings
{
    /// <summary>Whether Logpush is enabled.</summary>
    [JsonPropertyName("logpush")]
    public bool? Logpush { get; set; }

    /// <summary>Observability configuration.</summary>
    [JsonPropertyName("observability")]
    public WorkerObservability? Observability { get; set; }

    /// <summary>Tags attached to the script.</summary>
    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    /// <summary>Tail consumers attached to the script.</summary>
    [JsonPropertyName("tail_consumers")]
    public List<WorkerTailConsumer>? TailConsumers { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}
