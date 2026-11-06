using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Http;

namespace CFNet.Models.WorkersAi;

/// <summary>
/// One model in the Workers AI catalogue: what it is called, what task it performs, and how it is limited.
/// </summary>
/// <remarks>
/// <para>
/// Answered by <c>GET /accounts/{account_id}/ai/models/search</c>, whose published schema types the items as
/// <c>unknown</c> - Cloudflare documents the query parameters of this endpoint but not what it answers with,
/// and its own 200 example is <c>[{}]</c>. These types are therefore the catalogue as Cloudflare's Workers AI
/// documentation describes it, deliberately narrow: a field this library does not name is preserved in
/// <see cref="AdditionalData"/> rather than guessed at, and the integration run reports anything that arrives
/// unmodelled so the shape can be widened against a real answer instead of against a guess.
/// </para>
/// <para>
/// What a model can be asked for is not in this type at all: <c>GetModelSchemaAsync</c> answers the
/// model-specific input and output JSON Schema, which is the only published source for it.
/// </para>
/// </remarks>
public sealed class WorkersAiModel
{
    /// <summary>The model's id, which is a UUID rather than the name a call addresses it by.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>The name an inference call addresses the model by, such as <c>@cf/meta/llama-2-7b-chat-int8</c>.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>What the model is for, as the catalogue words it.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>The task the model performs, which is also what <c>task</c> filters the search by.</summary>
    [JsonPropertyName("task")]
    public WorkersAiModelTask? Task { get; set; }

    /// <summary>The catalogue's tags for the model.</summary>
    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    /// <summary>
    /// The catalogue's properties for the model - the flags and dates it carries, such as whether it is a
    /// beta or when it is deprecated.
    /// </summary>
    [JsonPropertyName("properties")]
    public List<WorkersAiModelProperty>? Properties { get; set; }

    /// <summary>How much the model can be given and how much it can answer with.</summary>
    [JsonPropertyName("limits")]
    public WorkersAiModelLimits? Limits { get; set; }

    /// <summary>How the model is batched, when it is batched at all.</summary>
    [JsonPropertyName("batching")]
    public WorkersAiModelBatching? Batching { get; set; }

    /// <summary>Captures any field Cloudflare adds, which for this endpoint is not a small possibility.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Whether the model carries one of the catalogue's tags.</summary>
    /// <param name="tag">The tag to look for.</param>
    /// <returns>True when the model is tagged with it.</returns>
    public bool HasTag(string tag) =>
        Tags is not null &&
        Tags.Any(candidate => string.Equals(candidate, tag, StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads one of the catalogue's properties by id.</summary>
    /// <param name="propertyId">The property's id, such as <c>beta</c> or <c>deprecation_date</c>.</param>
    /// <returns>The property, or null when the model does not carry it.</returns>
    public WorkersAiModelProperty? Property(string propertyId) =>
        Properties?.FirstOrDefault(property =>
            string.Equals(property.PropertyId, propertyId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Name and task, on one line.</summary>
    /// <returns>The model described in one line.</returns>
    public override string ToString() => $"{Name} ({Task?.Name})";
}

/// <summary>The task a model performs: the family an inference call's shape belongs to.</summary>
public sealed class WorkersAiModelTask
{
    /// <summary>The task's id, which is a UUID.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>The task's name, such as <c>text-generation</c> or <c>text-embeddings</c>.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>What the task does, as the catalogue words it.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The task's name, on one line.</summary>
    /// <returns>The task described in one line.</returns>
    public override string ToString() => $"{Name}";
}

/// <summary>One of the catalogue's properties on a model: an id and whatever value it carries.</summary>
/// <remarks>
/// The value's type moves with the property - a flag arrives as a boolean or as the string <c>"true"</c>, a
/// date as a string, a size as a number - so it is read as the JSON it arrived as and
/// <see cref="ValueText"/> prints whichever it was.
/// </remarks>
public sealed class WorkersAiModelProperty
{
    /// <summary>The property's id, such as <c>beta</c>, <c>deprecation_date</c> or <c>recommended</c>.</summary>
    [JsonPropertyName("property_id")]
    public string? PropertyId { get; set; }

    /// <summary>The value it carries, as the JSON Cloudflare sent.</summary>
    [JsonPropertyName("value")]
    public JsonElement? Value { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The value as text, whichever kind of it Cloudflare sent: a string unquoted, the rest as written.</summary>
    [JsonIgnore]
    public string? ValueText
    {
        get
        {
            if (Value is not { } value)
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
        }
    }

    /// <summary>Id and value, on one line.</summary>
    /// <returns>The property described in one line.</returns>
    public override string ToString() => $"{PropertyId}={ValueText}";
}

/// <summary>How much a model can be given and how much it can answer with.</summary>
public sealed class WorkersAiModelLimits
{
    /// <summary>The most input tokens one call can send the model.</summary>
    [JsonPropertyName("max_input_tokens")]
    public long? MaxInputTokens { get; set; }

    /// <summary>The most output tokens one call can get back.</summary>
    [JsonPropertyName("max_output_tokens")]
    public long? MaxOutputTokens { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Both limits, on one line.</summary>
    /// <returns>The limits described in one line.</returns>
    public override string ToString() =>
        $"{MaxInputTokens?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"} in, " +
        $"{MaxOutputTokens?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"} out";
}

/// <summary>How a model's work is batched, when Cloudflare batches it at all.</summary>
public sealed class WorkersAiModelBatching
{
    /// <summary>The most requests one batch holds.</summary>
    [JsonPropertyName("max_batch_size")]
    public long? MaxBatchSize { get; set; }

    /// <summary>The least a batch waits to fill up before running, in seconds.</summary>
    [JsonPropertyName("min_delay_seconds")]
    public double? MinDelaySeconds { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Batch size and delay, on one line.</summary>
    /// <returns>The batching described in one line.</returns>
    public override string ToString() =>
        $"{MaxBatchSize?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"} per batch";
}

/// <summary>One task type in the Workers AI catalogue, such as <c>text-generation</c> or <c>embeddings</c>.</summary>
/// <remarks>
/// Answered by <c>GET /accounts/{account_id}/ai/tasks/search</c>, whose published schema types the items as
/// <c>unknown</c> the same way the model search does - so this carries the three fields the catalogue is
/// documented with and preserves anything else in <see cref="AdditionalData"/>. It is what the model search's
/// <c>task</c> filter takes.
/// </remarks>
public sealed class WorkersAiTask
{
    /// <summary>The task's id, which is a UUID.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>The task's name, which is what a model search filters by.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>What the task does, as the catalogue words it.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The task's name, on one line.</summary>
    /// <returns>The task described in one line.</returns>
    public override string ToString() => $"{Name}";
}

/// <summary>One author or organisation behind the models in the Workers AI catalogue.</summary>
/// <remarks>
/// Answered by <c>GET /accounts/{account_id}/ai/authors/search</c>, whose published schema types the items as
/// <c>unknown</c> and whose query the published schema leaves empty - there is not even a search term, so
/// this lists every author there is. It is what the model search's <c>author</c> filter takes.
/// </remarks>
public sealed class WorkersAiAuthor
{
    /// <summary>The author's id.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>The author's name, which is what a model search filters by.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Who the author is, as the catalogue words it.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The author's name, on one line.</summary>
    /// <returns>The author described in one line.</returns>
    public override string ToString() => $"{Name}";
}

/// <summary>The input and output JSON Schema of one model.</summary>
/// <remarks>
/// Answered by <c>GET /accounts/{account_id}/ai/models/schema?model={model}</c>. This is the only published
/// source for what a model's inference call takes and answers, and it is worth asking before building one:
/// the shapes differ per task, and the inference endpoint's own schema is a union of a dozen of them.
/// </remarks>
public sealed class WorkersAiModelSchema
{
    /// <summary>The JSON Schema of the model's input.</summary>
    [JsonPropertyName("input")]
    public WorkersAiJsonSchema? Input { get; set; }

    /// <summary>The JSON Schema of the model's output.</summary>
    [JsonPropertyName("output")]
    public WorkersAiJsonSchema? Output { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Both schemas' types, on one line.</summary>
    /// <returns>The model schema described in one line.</returns>
    public override string ToString() => $"{Input?.Type} in, {Output?.Type} out";
}

/// <summary>One side of a model's schema, as Cloudflare publishes it.</summary>
/// <remarks>
/// The published shape is these three fields, which is a long way from a whole JSON Schema - the properties
/// that say what a model actually takes are not in it. They are preserved in <see cref="AdditionalData"/>,
/// which for this type is where the useful half is likely to be.
/// </remarks>
public sealed class WorkersAiJsonSchema
{
    /// <summary>Whether the schema allows fields it does not describe.</summary>
    [JsonPropertyName("additionalProperties")]
    public bool? AdditionalProperties { get; set; }

    /// <summary>What this side of the call is, as the schema words it.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>The JSON type of this side, such as <c>object</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Captures the rest of the schema, which the published shape does not describe.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Type and whether it is open, on one line.</summary>
    /// <returns>The schema side described in one line.</returns>
    public override string ToString() => $"{Type}{(AdditionalProperties == true ? " (open)" : string.Empty)}";
}

/// <summary>Filters for a search of the Workers AI model catalogue.</summary>
/// <remarks>
/// All of these are query parameters and all are spelled in snake_case, including the paging pair this
/// endpoint shares with the other page-numbered lists.
/// </remarks>
public sealed class WorkersAiModelListParameters : CloudflarePagedParameters
{
    /// <summary>Only models by this author, as <c>ListAuthorsAsync</c> names one.</summary>
    public string? Author { get; set; }

    /// <summary>
    /// Asks for a marketplace-format answer instead of the default one: one of
    /// <c>WorkersAiModelFormats</c>. The default shape is the only one this library types, so setting this
    /// changes what comes back into something it does not read.
    /// </summary>
    public string? Format { get; set; }

    /// <summary>Whether to leave experimental models out.</summary>
    public bool? HideExperimental { get; set; }

    /// <summary>
    /// Whether to include models for up to three months past their deprecation date. False by default.
    /// </summary>
    public bool? IncludeDeprecated { get; set; }

    /// <summary>A term matched against the models' names and descriptions.</summary>
    public string? Search { get; set; }

    /// <summary>Only models from this source, by its id.</summary>
    public long? Source { get; set; }

    /// <summary>Only models performing this task, as <c>ListTasksAsync</c> names one.</summary>
    public string? Task { get; set; }

    /// <inheritdoc />
    public override void Apply(QueryStringBuilder query)
    {
        base.Apply(query);
        query.AddIfNotEmpty("author", Author);
        query.AddIfNotEmpty("format", Format);
        query.Add("hide_experimental", HideExperimental);
        query.Add("include_deprecated", IncludeDeprecated);
        query.AddIfNotEmpty("search", Search);
        query.Add("source", Source);
        query.AddIfNotEmpty("task", Task);
    }
}

/// <summary>The marketplace formats the model search can answer in.</summary>
public static class WorkersAiModelFormats
{
    /// <summary>
    /// The OpenRouter model format, which arrives as <c>{data: [...]}</c> rather than as the default array.
    /// </summary>
    public const string OpenRouter = "openrouter";

    /// <summary>Every format the schema lists.</summary>
    public static readonly IReadOnlyList<string> All = new[] { OpenRouter };
}
