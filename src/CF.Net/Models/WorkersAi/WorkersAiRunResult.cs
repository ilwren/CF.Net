using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Serialization;

namespace CFNet.Models.WorkersAi;

/// <summary>What an inference call answered, whichever shape the model in the path answers with.</summary>
/// <remarks>
/// <para>
/// Cloudflare publishes the result of <c>POST /accounts/{account_id}/ai/run/{model_name}</c> as a union of
/// thirteen shapes: an object for a chat or embedding model, an <b>array</b> of objects for a classification
/// or detection model, and a bare <b>string</b> for a few others. One CLR type cannot be all three, and
/// guessing which one a model answers from its name is exactly the kind of guess this library avoids, so this
/// holds the JSON that arrived and reads it into the arm a caller asks for. <see cref="Raw"/> is always the
/// truth; the <c>As…</c> methods are projections of it and answer <c>null</c> or an empty list, rather than
/// throwing, when the answer was a different shape.
/// </para>
/// <para>
/// <see cref="Text"/> is the convenience most callers want: whichever field of whichever arm carries the
/// words the model produced - <c>response</c>, <c>text</c>, <c>translated_text</c>, <c>summary</c>,
/// <c>description</c>, or the whole answer when it is a bare string.
/// </para>
/// </remarks>
public sealed class WorkersAiRunResult
{
    /// <summary>Builds a result from the JSON Cloudflare put in <c>result</c>.</summary>
    /// <param name="result">The answer, as it arrived.</param>
    public WorkersAiRunResult(JsonElement result)
    {
        Raw = result;
        Text = result.ValueKind switch
        {
            JsonValueKind.String => result.GetString(),
            JsonValueKind.Object => FirstString(
                result,
                "response",
                "text",
                "translated_text",
                "summary",
                "description"),
            _ => null,
        };
    }

    /// <summary>The answer exactly as Cloudflare sent it.</summary>
    public JsonElement Raw { get; }

    /// <summary>The JSON kind of the answer: an object, an array, a string, or something rarer.</summary>
    public JsonValueKind Kind => Raw.ValueKind;

    /// <summary>Whether the answer was a single object, as most models answer.</summary>
    public bool IsObject => Kind == JsonValueKind.Object;

    /// <summary>Whether the answer was an array, as classification and detection models answer.</summary>
    public bool IsArray => Kind == JsonValueKind.Array;

    /// <summary>The words the model produced, whichever field of whichever arm carries them.</summary>
    public string? Text { get; }

    /// <summary>Reads the answer as a text-generation result: a response, its tool calls and its token usage.</summary>
    /// <returns>The result, or <c>null</c> when the answer was not an object.</returns>
    public WorkersAiTextGeneration? AsTextGeneration() => AsObject<WorkersAiTextGeneration>();

    /// <summary>Reads the answer as speech synthesis: base64 audio.</summary>
    /// <returns>The result, or <c>null</c> when the answer was not an object.</returns>
    public WorkersAiAudio? AsAudio() => AsObject<WorkersAiAudio>();

    /// <summary>Reads the answer as embeddings: vectors and their shape.</summary>
    /// <returns>The result, or <c>null</c> when the answer was not an object.</returns>
    public WorkersAiEmbeddings? AsEmbeddings() => AsObject<WorkersAiEmbeddings>();

    /// <summary>Reads the answer as speech recognition: a transcript, optionally timed word by word.</summary>
    /// <returns>The result, or <c>null</c> when the answer was not an object.</returns>
    public WorkersAiTranscription? AsTranscription() => AsObject<WorkersAiTranscription>();

    /// <summary>Reads the answer as a translation.</summary>
    /// <returns>The result, or <c>null</c> when the answer was not an object.</returns>
    public WorkersAiTranslation? AsTranslation() => AsObject<WorkersAiTranslation>();

    /// <summary>Reads the answer as a summary.</summary>
    /// <returns>The result, or <c>null</c> when the answer was not an object.</returns>
    public WorkersAiSummary? AsSummarization() => AsObject<WorkersAiSummary>();

    /// <summary>Reads the answer as a description of an image.</summary>
    /// <returns>The result, or <c>null</c> when the answer was not an object.</returns>
    public WorkersAiImageDescription? AsImageDescription() => AsObject<WorkersAiImageDescription>();

    /// <summary>Reads the answer as a generated image: base64 bytes.</summary>
    /// <returns>The result, or <c>null</c> when the answer was not an object.</returns>
    public WorkersAiGeneratedImage? AsImage() => AsObject<WorkersAiGeneratedImage>();

    /// <summary>Reads the answer as classifications, which arrive as an array of label and score.</summary>
    /// <returns>The classifications, or an empty list when the answer was not an array.</returns>
    public IReadOnlyList<WorkersAiClassification> AsClassifications() => AsArray<WorkersAiClassification>();

    /// <summary>Reads the answer as detections, which arrive as an array of label, score and box.</summary>
    /// <returns>The detections, or an empty list when the answer was not an array.</returns>
    public IReadOnlyList<WorkersAiDetection> AsDetections() => AsArray<WorkersAiDetection>();

    /// <summary>
    /// Reads the answer as a type of the caller's choosing, for an arm this library has not named.
    /// </summary>
    /// <typeparam name="T">The type to read the answer into.</typeparam>
    /// <returns>The answer as that type, or <c>null</c> when there was no answer to read.</returns>
    /// <exception cref="JsonException">
    /// Thrown when the answer's shape cannot be that type - an array into a class, for instance. The
    /// <c>As…</c> methods guard against this; this one is the escape hatch and does not.
    /// </exception>
    public T? As<T>()
        where T : class =>
        Kind is JsonValueKind.Null or JsonValueKind.Undefined
            ? null
            : Raw.Deserialize<T>(CloudflareJson.Default);

    /// <summary>The kind of answer and a slice of it, on one line.</summary>
    /// <returns>The result described in one line.</returns>
    public override string ToString() =>
        Kind switch
        {
            JsonValueKind.Object => $"object: {Slice(Text ?? Raw.ToString())}",
            JsonValueKind.Array => $"array of {Raw.GetArrayLength()} item(s)",
            JsonValueKind.String => $"string: {Slice(Text ?? string.Empty)}",
            _ => Kind.ToString(),
        };

    private T? AsObject<T>()
        where T : class =>
        Kind == JsonValueKind.Object ? Raw.Deserialize<T>(CloudflareJson.Default) : null;

    private IReadOnlyList<T> AsArray<T>() =>
        Kind == JsonValueKind.Array
            ? Raw.Deserialize<List<T>>(CloudflareJson.Default) ?? []
            : [];

    private static string? FirstString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static string Slice(string text) =>
        text.Length <= 60 ? text : string.Concat(text.AsSpan(0, 57), "…");
}

/// <summary>What a text-generation model answered: the words, the tools it called and what it cost.</summary>
public sealed class WorkersAiTextGeneration
{
    /// <summary>The generated text.</summary>
    [JsonPropertyName("response")]
    public string? Response { get; set; }

    /// <summary>The tools the model asked to have called, when it was offered any.</summary>
    [JsonPropertyName("tool_calls")]
    public List<WorkersAiToolCall>? ToolCalls { get; set; }

    /// <summary>What the call cost, in tokens.</summary>
    [JsonPropertyName("usage")]
    public WorkersAiUsage? Usage { get; set; }

    /// <summary>Captures any field Cloudflare adds to this arm.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>A slice of the response and the token count, on one line.</summary>
    /// <returns>The answer described in one line.</returns>
    public override string ToString()
    {
        var response = Response ?? "(no response)";
        if (response.Length > 60)
        {
            response = string.Concat(response.AsSpan(0, 57), "…");
        }

        return Usage is null ? response : response + " [" + Usage + "]";
    }
}

/// <summary>A tool the model asked to have called.</summary>
public sealed class WorkersAiToolCall
{
    /// <summary>The tool's name, matching one the request offered.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// The arguments the model chose, as the JSON that arrived. Cloudflare publishes this field as
    /// <c>unknown</c>: a model answers the object its tool declared, and nothing says what that is.
    /// </summary>
    [JsonPropertyName("arguments")]
    public JsonElement? Arguments { get; set; }

    /// <summary>Captures any field Cloudflare adds.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The tool's name and its arguments, on one line.</summary>
    /// <returns>The call described in one line.</returns>
    public override string ToString() => $"{Name}({Arguments?.ToString()})";
}

/// <summary>What an inference call cost, in tokens.</summary>
public sealed class WorkersAiUsage
{
    /// <summary>Tokens in the answer.</summary>
    [JsonPropertyName("completion_tokens")]
    public long? CompletionTokens { get; set; }

    /// <summary>Tokens in the request.</summary>
    [JsonPropertyName("prompt_tokens")]
    public long? PromptTokens { get; set; }

    /// <summary>Tokens in both.</summary>
    [JsonPropertyName("total_tokens")]
    public long? TotalTokens { get; set; }

    /// <summary>
    /// What the call cost in neurons, Workers AI's own unit for billing a model that is not counted in tokens
    /// alone. Observed live as <c>0.18253148533403873</c>, and not published in the answer's shape.
    /// </summary>
    [JsonPropertyName("neurons")]
    public double? Neurons { get; set; }

    /// <summary>How much of the prompt came from a cache, when Cloudflare reports the detail at all.</summary>
    [JsonPropertyName("prompt_tokens_details")]
    public WorkersAiPromptTokensDetails? PromptTokensDetails { get; set; }

    /// <summary>Captures any field Cloudflare adds.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The three counts, on one line.</summary>
    /// <returns>The usage described in one line.</returns>
    public override string ToString() =>
        $"{PromptTokens ?? 0} in + {CompletionTokens ?? 0} out = {TotalTokens ?? 0} token(s)";
}

/// <summary>
/// The detail behind a usage's prompt tokens. Observed live inside a generation's <c>usage</c> as
/// <c>{"cached_tokens":0}</c>; nothing publishes it.
/// </summary>
public sealed class WorkersAiPromptTokensDetails
{
    /// <summary>How many of the prompt's tokens were served from a cache.</summary>
    [JsonPropertyName("cached_tokens")]
    public long? CachedTokens { get; set; }

    /// <summary>Captures any field Cloudflare adds.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The cached count, on one line.</summary>
    /// <returns>The detail described in one line.</returns>
    public override string ToString() => $"{CachedTokens ?? 0} cached token(s)";
}

/// <summary>What a speech-synthesis model answered: audio, base64-encoded.</summary>
public sealed class WorkersAiAudio
{
    /// <summary>The audio, base64-encoded.</summary>
    [JsonPropertyName("audio")]
    public string? Audio { get; set; }

    /// <summary>The audio decoded. Not sent anywhere; a convenience for a caller that wants the bytes.</summary>
    [JsonIgnore]
    public byte[]? Bytes => string.IsNullOrEmpty(Audio) ? null : Convert.FromBase64String(Audio);

    /// <summary>Captures any field Cloudflare adds.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>How much audio arrived, on one line.</summary>
    /// <returns>The answer described in one line.</returns>
    public override string ToString() => $"{Audio?.Length ?? 0} base64 character(s)";
}

/// <summary>What a generated image arrived as: base64 bytes.</summary>
public sealed class WorkersAiGeneratedImage
{
    /// <summary>The image, base64-encoded.</summary>
    [JsonPropertyName("image")]
    public string? Image { get; set; }

    /// <summary>The image decoded. Not sent anywhere; a convenience for a caller that wants the bytes.</summary>
    [JsonIgnore]
    public byte[]? Bytes => string.IsNullOrEmpty(Image) ? null : Convert.FromBase64String(Image);

    /// <summary>Captures any field Cloudflare adds.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>How much image arrived, on one line.</summary>
    /// <returns>The answer described in one line.</returns>
    public override string ToString() => $"{Image?.Length ?? 0} base64 character(s)";
}

/// <summary>What an embedding model answered: the vectors, and the shape they came in.</summary>
public sealed class WorkersAiEmbeddings
{
    /// <summary>
    /// The vectors, one per input text. A batch of three texts answers three lists of numbers.
    /// </summary>
    [JsonPropertyName("data")]
    public List<List<double>>? Data { get; set; }

    /// <summary>
    /// The shape of <see cref="Data"/> as the model reports it, such as <c>[1, 768]</c> for one vector of 768
    /// dimensions.
    /// </summary>
    [JsonPropertyName("shape")]
    public List<long>? Shape { get; set; }

    /// <summary>
    /// How the vectors were pooled, when the model says. Observed live on
    /// <c>@cf/baai/bge-base-en-v1.5</c>, whose published answer is only <c>data</c> and <c>shape</c>.
    /// </summary>
    [JsonPropertyName("pooling")]
    public string? Pooling { get; set; }

    /// <summary>
    /// What the call cost, which an embedding answers as readily as a generation does - and which the published
    /// shape for this arm does not mention.
    /// </summary>
    [JsonPropertyName("usage")]
    public WorkersAiUsage? Usage { get; set; }

    /// <summary>How many vectors arrived.</summary>
    [JsonIgnore]
    public int VectorCount => Data?.Count ?? 0;

    /// <summary>How wide each vector is, as the model reported it or as the first vector shows.</summary>
    [JsonIgnore]
    public int Dimensions =>
        Shape is { Count: > 1 } shape
            ? (int)shape[^1]
            : Data is { Count: > 0 } data && data[0] is { } first
                ? first.Count
                : 0;

    /// <summary>Captures any field Cloudflare adds.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Vector count and width, on one line.</summary>
    /// <returns>The answer described in one line.</returns>
    public override string ToString() => $"{VectorCount} vector(s) of {Dimensions}";
}

/// <summary>What a speech-recognition model answered: a transcript, and the words it was made of.</summary>
public sealed class WorkersAiTranscription
{
    /// <summary>The transcript. The one field of this arm Cloudflare publishes as required.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>The transcript as WebVTT, timed.</summary>
    [JsonPropertyName("vtt")]
    public string? Vtt { get; set; }

    /// <summary>How many words were recognised.</summary>
    [JsonPropertyName("word_count")]
    public long? WordCount { get; set; }

    /// <summary>The words, with the span of audio each covers.</summary>
    [JsonPropertyName("words")]
    public List<WorkersAiTranscribedWord>? Words { get; set; }

    /// <summary>Captures any field Cloudflare adds.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The transcript and the word count, on one line.</summary>
    /// <returns>The answer described in one line.</returns>
    public override string ToString() => $"{Text} [{WordCount ?? Words?.Count ?? 0} word(s)]";
}

/// <summary>One recognised word and where in the audio it was.</summary>
public sealed class WorkersAiTranscribedWord
{
    /// <summary>The word.</summary>
    [JsonPropertyName("word")]
    public string? Word { get; set; }

    /// <summary>When it starts, in seconds. Fractional, so not a whole number.</summary>
    [JsonPropertyName("start")]
    public double? Start { get; set; }

    /// <summary>When it ends, in seconds.</summary>
    [JsonPropertyName("end")]
    public double? End { get; set; }

    /// <summary>Captures any field Cloudflare adds.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The word and its span, on one line.</summary>
    /// <returns>The word described in one line.</returns>
    public override string ToString() => $"{Word} {Start}-{End}";
}

/// <summary>One classification, which arrives as an item of an array rather than as the whole answer.</summary>
public sealed class WorkersAiClassification
{
    /// <summary>The label the model chose.</summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>How sure it was, between 0 and 1.</summary>
    [JsonPropertyName("score")]
    public double? Score { get; set; }

    /// <summary>Captures any field Cloudflare adds.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Label and score, on one line.</summary>
    /// <returns>The classification described in one line.</returns>
    public override string ToString() => $"{Label} {Score}";
}

/// <summary>One detected object, which arrives as an item of an array rather than as the whole answer.</summary>
public sealed class WorkersAiDetection
{
    /// <summary>The label of what was detected.</summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>How sure the model was, between 0 and 1.</summary>
    [JsonPropertyName("score")]
    public double? Score { get; set; }

    /// <summary>Where in the image it was.</summary>
    [JsonPropertyName("box")]
    public WorkersAiBoundingBox? Box { get; set; }

    /// <summary>Captures any field Cloudflare adds.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Label, score and box, on one line.</summary>
    /// <returns>The detection described in one line.</returns>
    public override string ToString() => $"{Label} {Score} {Box}";
}

/// <summary>The box around a detected object.</summary>
public sealed class WorkersAiBoundingBox
{
    /// <summary>The left edge.</summary>
    [JsonPropertyName("xmin")]
    public double? Xmin { get; set; }

    /// <summary>The top edge.</summary>
    [JsonPropertyName("ymin")]
    public double? Ymin { get; set; }

    /// <summary>The right edge.</summary>
    [JsonPropertyName("xmax")]
    public double? Xmax { get; set; }

    /// <summary>The bottom edge.</summary>
    [JsonPropertyName("ymax")]
    public double? Ymax { get; set; }

    /// <summary>Captures any field Cloudflare adds.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The four edges, on one line.</summary>
    /// <returns>The box described in one line.</returns>
    public override string ToString() => $"({Xmin},{Ymin})-({Xmax},{Ymax})";
}

/// <summary>What a translation model answered.</summary>
public sealed class WorkersAiTranslation
{
    /// <summary>The translated text.</summary>
    [JsonPropertyName("translated_text")]
    public string? TranslatedText { get; set; }

    /// <summary>Captures any field Cloudflare adds.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The translation.</summary>
    /// <returns>The answer in one line.</returns>
    public override string ToString() => TranslatedText ?? "(no translation)";
}

/// <summary>What a summarisation model answered.</summary>
public sealed class WorkersAiSummary
{
    /// <summary>The summary.</summary>
    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    /// <summary>Captures any field Cloudflare adds.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The summary.</summary>
    /// <returns>The answer in one line.</returns>
    public override string ToString() => Summary ?? "(no summary)";
}

/// <summary>What an image-to-text model answered. The multimodal chat arm answers with the same field.</summary>
public sealed class WorkersAiImageDescription
{
    /// <summary>The description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Captures any field Cloudflare adds.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The description.</summary>
    /// <returns>The answer in one line.</returns>
    public override string ToString() => Description ?? "(no description)";
}
