using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Models.WorkersAi;

/// <summary>
/// The body of an inference call: <c>POST /accounts/{account_id}/ai/run/{model_name}</c>.
/// </summary>
/// <remarks>
/// <para>
/// Cloudflare documents this body as a union of fifteen shapes, one per task - a classification sends
/// <c>text</c>, a chat model sends <c>messages</c>, a speech model sends <c>audio</c> as an array of bytes -
/// and the inputs go in the body directly, with no model/input/options wrapper. One class carries every
/// field of every arm rather than fifteen classes that differ in three properties, because which arm a call
/// is belongs to the model in the path and not to the body: the same <c>max_tokens</c> and <c>temperature</c>
/// serve a prompt, a chat and an image-to-text call alike. Nothing that is not set is sent, so the body is
/// exactly the arm the model expects.
/// </para>
/// <para>
/// The static <c>For…</c> factories set the fields a given task requires and nothing else; the knobs are then
/// set on the returned object. <see cref="EnsureValid"/> refuses the combinations Cloudflare cannot act on -
/// an empty body, a prompt and a text together, image dimensions outside the published bounds - and refuses
/// <see cref="Stream"/>, because a streamed answer is server-sent events rather than the one JSON envelope
/// this library's transport reads.
/// </para>
/// <para>
/// A model may accept a field this class does not name. <see cref="AdditionalData"/> is written into the body
/// alongside the modelled fields, so such a call needs no change here.
/// </para>
/// </remarks>
public sealed class WorkersAiRunRequest
{
    /// <summary>
    /// The prompt to complete: text-generation with a single prompt, the image to generate, or the speech to
    /// synthesise. Chat models take <see cref="Messages"/> instead.
    /// </summary>
    [JsonPropertyName("prompt")]
    public string? Prompt { get; set; }

    /// <summary>The conversation to continue, for a chat or multimodal chat model.</summary>
    [JsonPropertyName("messages")]
    public List<WorkersAiChatMessage>? Messages { get; set; }

    /// <summary>
    /// The text to classify, translate or embed. Embedding models also take several at once, which is what
    /// <see cref="WorkersAiTextOrBatch"/> is for: one field, either one string or an array of them.
    /// </summary>
    [JsonPropertyName("text")]
    public WorkersAiTextOrBatch? Text { get; set; }

    /// <summary>The text to summarise. Summarisation names its own field rather than sharing <c>text</c>.</summary>
    [JsonPropertyName("input_text")]
    public string? InputText { get; set; }

    /// <summary>The language to translate into, such as <c>fr</c>. Required by translation models.</summary>
    [JsonPropertyName("target_lang")]
    public string? TargetLang { get; set; }

    /// <summary>The language the input is in, when the model should not detect it.</summary>
    [JsonPropertyName("source_lang")]
    public string? SourceLang { get; set; }

    /// <summary>
    /// The image to work on. Image classification, object detection, image-to-text and img2img take it as an
    /// array of 8-bit values; the multimodal models take it as a base64 string. Both spellings are this one
    /// field, and <see cref="WorkersAiImageInput"/> writes whichever it was given.
    /// </summary>
    [JsonPropertyName("image")]
    public WorkersAiImageInput? Image { get; set; }

    /// <summary>A base64 input image for an img2img call. A separate field from <see cref="Image"/>, as published.</summary>
    [JsonPropertyName("image_b64")]
    public string? ImageBase64 { get; set; }

    /// <summary>The inpainting mask, as an array of 8-bit values the size of the image.</summary>
    [JsonPropertyName("mask")]
    public List<byte>? Mask { get; set; }

    /// <summary>
    /// The audio to transcribe, as an array of 8-bit values. Not a base64 string: the published shape is an
    /// array of numbers, which is what a <see cref="byte"/> list serialises to.
    /// </summary>
    [JsonPropertyName("audio")]
    public List<byte>? Audio { get; set; }

    /// <summary>How many tokens the answer may use.</summary>
    [JsonPropertyName("max_tokens")]
    public long? MaxTokens { get; set; }

    /// <summary>Sampling temperature. Higher is more varied.</summary>
    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    /// <summary>Keeps only the this many most likely next tokens.</summary>
    [JsonPropertyName("top_k")]
    public long? TopK { get; set; }

    /// <summary>Keeps the smallest set of tokens whose probabilities add up to this.</summary>
    [JsonPropertyName("top_p")]
    public double? TopP { get; set; }

    /// <summary>Penalises tokens that have already appeared, by how often they did.</summary>
    [JsonPropertyName("frequency_penalty")]
    public double? FrequencyPenalty { get; set; }

    /// <summary>Penalises tokens that have already appeared, at all.</summary>
    [JsonPropertyName("presence_penalty")]
    public double? PresencePenalty { get; set; }

    /// <summary>Penalises repetition, multiplicatively rather than additively.</summary>
    [JsonPropertyName("repetition_penalty")]
    public double? RepetitionPenalty { get; set; }

    /// <summary>A seed, so the same request gives the same answer.</summary>
    [JsonPropertyName("seed")]
    public long? Seed { get; set; }

    /// <summary>Asks for the model's own output rather than the wrapper this library and Cloudflare add.</summary>
    [JsonPropertyName("raw")]
    public bool? Raw { get; set; }

    /// <summary>The name of a fine-tuned adapter to run on top of the model.</summary>
    [JsonPropertyName("lora")]
    public string? Lora { get; set; }

    /// <summary>
    /// Asks for the answer as server-sent events instead of one JSON body. This library refuses to send it:
    /// its transport reads a response as a single Cloudflare envelope, and a stream is neither.
    /// </summary>
    [JsonPropertyName("stream")]
    public bool? Stream { get; set; }

    /// <summary>Lets the model run past an end-of-sequence token.</summary>
    [JsonPropertyName("ignore_eos")]
    public bool? IgnoreEndOfSequence { get; set; }

    /// <summary>Asks for a JSON answer, either any JSON object or one fitting a schema.</summary>
    [JsonPropertyName("response_format")]
    public WorkersAiResponseFormat? ResponseFormat { get; set; }

    /// <summary>The tools the model may call, described the way OpenAI describes them.</summary>
    [JsonPropertyName("tools")]
    public List<WorkersAiTool>? Tools { get; set; }

    /// <summary>
    /// The older tool spelling: a name and Cloudflare Workers code to run. A model takes this or
    /// <see cref="Tools"/>, and the two are published as separate fields of the same arm.
    /// </summary>
    [JsonPropertyName("functions")]
    public List<WorkersAiLegacyFunction>? Functions { get; set; }

    /// <summary>How closely an image should follow the prompt. Higher is closer.</summary>
    [JsonPropertyName("guidance")]
    public double? Guidance { get; set; }

    /// <summary>The image's height in pixels, between 256 and 2048 as published.</summary>
    [JsonPropertyName("height")]
    public long? Height { get; set; }

    /// <summary>The image's width in pixels, between 256 and 2048 as published.</summary>
    [JsonPropertyName("width")]
    public long? Width { get; set; }

    /// <summary>Diffusion steps, at most 20 as published. More is slower and usually better.</summary>
    [JsonPropertyName("num_steps")]
    public long? NumSteps { get; set; }

    /// <summary>How strongly an img2img call should transform its input, between 0 and 1 as published.</summary>
    [JsonPropertyName("strength")]
    public double? Strength { get; set; }

    /// <summary>What the image should not contain.</summary>
    [JsonPropertyName("negative_prompt")]
    public string? NegativePrompt { get; set; }

    /// <summary>The language to speak, such as <c>en</c>. Defaults to English when not sent.</summary>
    [JsonPropertyName("lang")]
    public string? Lang { get; set; }

    /// <summary>How long a summary may be.</summary>
    [JsonPropertyName("max_length")]
    public long? MaxLength { get; set; }

    /// <summary>
    /// Fields this class does not name, written into the body as they are. A model's own knob does not have to
    /// wait for a release of this library.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, object?>? AdditionalData { get; set; }

    /// <summary>Builds a text-generation call from a single prompt.</summary>
    /// <param name="prompt">The prompt to complete. Has to be at least one character.</param>
    /// <returns>A request carrying only the prompt.</returns>
    public static WorkersAiRunRequest ForPrompt(string prompt) => new() { Prompt = prompt };

    /// <summary>Builds a chat call from a conversation.</summary>
    /// <param name="messages">The conversation, oldest first.</param>
    /// <returns>A request carrying only the messages.</returns>
    public static WorkersAiRunRequest ForMessages(params WorkersAiChatMessage[] messages) =>
        new() { Messages = [.. messages] };

    /// <summary>Builds an embedding call for one text.</summary>
    /// <param name="text">The text to embed.</param>
    /// <returns>A request carrying only the text.</returns>
    public static WorkersAiRunRequest ForEmbeddings(string text) => new() { Text = text };

    /// <summary>Builds an embedding call for a batch of texts.</summary>
    /// <param name="texts">The texts to embed, one vector each.</param>
    /// <returns>A request carrying only the batch.</returns>
    public static WorkersAiRunRequest ForEmbeddings(IEnumerable<string> texts) =>
        new() { Text = texts.ToList() };

    /// <summary>Builds a text-classification call.</summary>
    /// <param name="text">The text to classify.</param>
    /// <returns>A request carrying only the text.</returns>
    public static WorkersAiRunRequest ForClassification(string text) => new() { Text = text };

    /// <summary>Builds a translation call.</summary>
    /// <param name="text">The text to translate.</param>
    /// <param name="targetLang">The language to translate into, such as <c>fr</c>.</param>
    /// <param name="sourceLang">The language the text is in, or null to let the model detect it.</param>
    /// <returns>A request carrying the text and the languages.</returns>
    public static WorkersAiRunRequest ForTranslation(string text, string targetLang, string? sourceLang = null) =>
        new() { Text = text, TargetLang = targetLang, SourceLang = sourceLang };

    /// <summary>Builds a summarisation call.</summary>
    /// <param name="inputText">The text to summarise.</param>
    /// <param name="maxLength">How long the summary may be, or null for the model's default.</param>
    /// <returns>A request carrying the text and the length.</returns>
    public static WorkersAiRunRequest ForSummarization(string inputText, long? maxLength = null) =>
        new() { InputText = inputText, MaxLength = maxLength };

    /// <summary>Builds an image-generation call.</summary>
    /// <param name="prompt">What the image should show.</param>
    /// <returns>A request carrying only the prompt; dimensions and steps are set on the result.</returns>
    public static WorkersAiRunRequest ForTextToImage(string prompt) => new() { Prompt = prompt };

    /// <summary>Builds a speech-synthesis call.</summary>
    /// <param name="prompt">The words to speak.</param>
    /// <param name="lang">The language to speak them in, or null for English.</param>
    /// <returns>A request carrying the words and the language.</returns>
    public static WorkersAiRunRequest ForTextToSpeech(string prompt, string? lang = null) =>
        new() { Prompt = prompt, Lang = lang };

    /// <summary>Builds a speech-recognition call.</summary>
    /// <param name="audio">The audio samples, as 8-bit values.</param>
    /// <param name="sourceLang">The language spoken, or null to let the model detect it.</param>
    /// <returns>A request carrying the audio and the language.</returns>
    public static WorkersAiRunRequest ForSpeechRecognition(IEnumerable<byte> audio, string? sourceLang = null) =>
        new() { Audio = [.. audio], SourceLang = sourceLang };

    /// <summary>Builds an image-classification call.</summary>
    /// <param name="image">The image, as 8-bit values.</param>
    /// <returns>A request carrying only the image.</returns>
    public static WorkersAiRunRequest ForImageClassification(IEnumerable<byte> image) =>
        new() { Image = image.ToList() };

    /// <summary>Builds an object-detection call.</summary>
    /// <param name="image">The image, as 8-bit values.</param>
    /// <returns>A request carrying only the image.</returns>
    public static WorkersAiRunRequest ForObjectDetection(IEnumerable<byte> image) =>
        new() { Image = image.ToList() };

    /// <summary>Builds an image-to-text call.</summary>
    /// <param name="image">The image, as 8-bit values.</param>
    /// <param name="prompt">A question about the image, or null for a description.</param>
    /// <returns>A request carrying the image and the prompt.</returns>
    public static WorkersAiRunRequest ForImageToText(IEnumerable<byte> image, string? prompt = null) =>
        new() { Image = image.ToList(), Prompt = prompt };

    /// <summary>Builds a multimodal-embedding call.</summary>
    /// <param name="texts">The texts to embed, or null when embedding an image alone.</param>
    /// <param name="imageBase64">The image to embed, base64-encoded, or null when embedding texts alone.</param>
    /// <returns>A request carrying whichever of the two was given.</returns>
    public static WorkersAiRunRequest ForMultimodalEmbeddings(
        IEnumerable<string>? texts = null,
        string? imageBase64 = null)
    {
        var request = new WorkersAiRunRequest();
        if (texts is not null)
        {
            request.Text = texts.ToList();
        }

        if (imageBase64 is not null)
        {
            request.Image = WorkersAiImageInput.FromBase64(imageBase64);
        }

        return request;
    }

    /// <summary>Checks the request against what Cloudflare can act on, before anything is sent.</summary>
    /// <exception cref="CFNet.Exceptions.CloudflareConfigurationException">
    /// Thrown with every problem found, in the order below, so one call reports all of them.
    /// </exception>
    /// <remarks>
    /// Only the bounds Cloudflare publishes are enforced - the image dimensions and step count of a
    /// text-to-image call, and the minimum lengths of the text fields. The sampling knobs have no published
    /// range, so a strange temperature is Cloudflare's to refuse rather than this library's to guess at.
    /// </remarks>
    public void EnsureValid()
    {
        var problems = new List<string>();

        if (Stream == true)
        {
            problems.Add(
                "stream has to stay unset: a streamed inference answer is server-sent events, and this " +
                "library reads a response as one JSON envelope. Read the whole answer instead, or call the " +
                "endpoint with an HttpClient of your own.");
        }

        if (Prompt is not null && Text is not null)
        {
            problems.Add(
                "prompt and text cannot both be set: a generation model takes prompt and a classification, " +
                "translation or embedding model takes text, and one call goes to one model.");
        }

        if (Prompt is not null && Messages is { Count: > 0 })
        {
            problems.Add(
                "prompt and messages cannot both be set: a chat model takes the conversation and a completion " +
                "model takes the prompt.");
        }

        if (Prompt is { Length: 0 })
        {
            problems.Add("prompt has to be at least one character.");
        }

        if (Messages is { Count: 0 })
        {
            problems.Add("messages cannot be an empty list: a chat call needs at least one message.");
        }

        if (Text is { } text)
        {
            if (text.Batch is { Count: 0 })
            {
                problems.Add("text cannot be an empty list: an embedding call needs at least one text.");
            }
            else if (text.Single is { Length: 0 })
            {
                problems.Add("text has to be at least one character.");
            }
            else if (text.Batch is { } batch && batch.Any(string.IsNullOrEmpty))
            {
                problems.Add("every text in a batch has to be at least one character.");
            }
        }

        if (InputText is { Length: 0 })
        {
            problems.Add("input_text has to be at least one character.");
        }

        if (TargetLang is { Length: 0 })
        {
            problems.Add("target_lang has to be at least one character.");
        }

        if (MaxTokens is < 0)
        {
            problems.Add("max_tokens cannot be negative.");
        }

        if (Height is < 256 or > 2048)
        {
            problems.Add("height has to be between 256 and 2048 pixels, as Cloudflare publishes it.");
        }

        if (Width is < 256 or > 2048)
        {
            problems.Add("width has to be between 256 and 2048 pixels, as Cloudflare publishes it.");
        }

        if (NumSteps is < 1 or > 20)
        {
            problems.Add("num_steps has to be between 1 and 20, as Cloudflare publishes it.");
        }

        if (Strength is < 0 or > 1)
        {
            problems.Add("strength has to be between 0 and 1, as Cloudflare publishes it.");
        }

        if (IsEmpty && problems.Count == 0)
        {
            problems.Add(
                "an inference request has to carry something to run on: a prompt, messages, text, an image or " +
                "audio. Every field of this request is null.");
        }

        if (problems.Count > 0)
        {
            throw new CFNet.Exceptions.CloudflareConfigurationException(problems);
        }
    }

    /// <summary>Whether nothing at all was set, so the body would be empty.</summary>
    [JsonIgnore]
    public bool IsEmpty =>
        Prompt is null && Messages is null && Text is null && InputText is null && TargetLang is null &&
        SourceLang is null && Image is null && ImageBase64 is null && Mask is null && Audio is null &&
        MaxTokens is null && Temperature is null && TopK is null && TopP is null && FrequencyPenalty is null &&
        PresencePenalty is null && RepetitionPenalty is null && Seed is null && Raw is null && Lora is null &&
        Stream is null && IgnoreEndOfSequence is null && ResponseFormat is null && Tools is null &&
        Functions is null && Guidance is null && Height is null && Width is null && NumSteps is null &&
        Strength is null && NegativePrompt is null && Lang is null && MaxLength is null &&
        AdditionalData is not { Count: > 0 };

    /// <summary>The fields that are set, on one line.</summary>
    /// <returns>The request described in one line.</returns>
    public override string ToString()
    {
        if (IsEmpty)
        {
            return "an empty inference request";
        }

        var parts = new List<string>();
        if (Prompt is not null)
        {
            parts.Add("prompt");
        }

        if (Messages is { Count: > 0 } messages)
        {
            parts.Add("messages[" + messages.Count + "]");
        }

        if (Text is { } text)
        {
            parts.Add(text.Batch is { Count: > 0 } batch ? "text[" + batch.Count + "]" : "text");
        }

        if (Image is not null)
        {
            parts.Add("image");
        }

        if (Audio is not null)
        {
            parts.Add("audio");
        }

        if (InputText is not null)
        {
            parts.Add("input_text");
        }

        if (MaxTokens is { } tokens)
        {
            parts.Add("max_tokens=" + tokens);
        }

        return string.Join(" ", parts);
    }
}

/// <summary>The kinds of answer an inference call asks for, as the <c>response_format</c> field words them.</summary>
public static class WorkersAiResponseFormatTypes
{
    /// <summary>Any JSON object.</summary>
    public const string JsonObject = "json_object";

    /// <summary>A JSON object fitting <see cref="WorkersAiResponseFormat.JsonSchema"/>.</summary>
    public const string JsonSchema = "json_schema";

    /// <summary>Every kind this library knows of.</summary>
    public static IReadOnlyList<string> All { get; } = [JsonObject, JsonSchema];
}

/// <summary>Asks a model for a JSON answer rather than prose.</summary>
public sealed class WorkersAiResponseFormat
{
    /// <summary>One of <see cref="WorkersAiResponseFormatTypes"/>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// The schema the answer has to fit, as the JSON it arrived in. Cloudflare publishes this field as
    /// <c>unknown</c>, so it is kept raw rather than guessed at - and <see cref="WorkersAiJsonSchema"/>, the
    /// type a model's own schema reads into, is a reasonable thing to put here.
    /// </summary>
    [JsonPropertyName("json_schema")]
    public JsonElement? JsonSchema { get; set; }

    /// <summary>Asks for any JSON object.</summary>
    /// <returns>A response format of type <c>json_object</c>.</returns>
    public static WorkersAiResponseFormat JsonObject() =>
        new() { Type = WorkersAiResponseFormatTypes.JsonObject };

    /// <summary>Asks for a JSON object fitting a schema. Not called <c>JsonSchema</c>: that name is the field.</summary>
    /// <param name="schema">The schema, as JSON.</param>
    /// <returns>A response format of type <c>json_schema</c>.</returns>
    public static WorkersAiResponseFormat ForSchema(JsonElement schema) =>
        new() { Type = WorkersAiResponseFormatTypes.JsonSchema, JsonSchema = schema };

    /// <summary>Captures any field Cloudflare adds to this object.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The type, on one line.</summary>
    /// <returns>The response format described in one line.</returns>
    public override string ToString() => Type ?? "(no type)";
}

/// <summary>One turn of a conversation with a chat model.</summary>
public sealed class WorkersAiChatMessage
{
    /// <summary>Who is speaking, such as <c>system</c>, <c>user</c>, <c>assistant</c> or <c>tool</c>.</summary>
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    /// <summary>
    /// What was said. Text models take a string; a multimodal model takes an array of parts, which is what
    /// <see cref="WorkersAiMessageContent"/> writes whichever it was given.
    /// </summary>
    [JsonPropertyName("content")]
    public WorkersAiMessageContent? Content { get; set; }

    /// <summary>A message spoken as plain text.</summary>
    /// <param name="role">Who is speaking.</param>
    /// <param name="content">What was said.</param>
    /// <returns>The message.</returns>
    public static WorkersAiChatMessage Of(string role, string content) =>
        new() { Role = role, Content = content };

    /// <summary>A message carrying text and images.</summary>
    /// <param name="role">Who is speaking.</param>
    /// <param name="parts">The parts of the message, in order.</param>
    /// <returns>The message.</returns>
    public static WorkersAiChatMessage Of(string role, params WorkersAiMessagePart[] parts) =>
        new() { Role = role, Content = parts.ToList() };

    /// <summary>A system message: the instructions the conversation starts with.</summary>
    /// <param name="content">The instructions.</param>
    /// <returns>The message.</returns>
    public static WorkersAiChatMessage System(string content) => Of(WorkersAiChatRoles.System, content);

    /// <summary>A user message.</summary>
    /// <param name="content">What the user said.</param>
    /// <returns>The message.</returns>
    public static WorkersAiChatMessage User(string content) => Of(WorkersAiChatRoles.User, content);

    /// <summary>An assistant message, such as a turn this library read back from the model.</summary>
    /// <param name="content">What the model said.</param>
    /// <returns>The message.</returns>
    public static WorkersAiChatMessage Assistant(string content) => Of(WorkersAiChatRoles.Assistant, content);

    /// <summary>The answer to a tool call, sent back to the model as the next turn.</summary>
    /// <param name="content">What the tool returned.</param>
    /// <returns>The message.</returns>
    public static WorkersAiChatMessage Tool(string content) => Of(WorkersAiChatRoles.Tool, content);

    /// <summary>Captures any field Cloudflare adds to a message.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Role and a short slice of the content, on one line.</summary>
    /// <returns>The message described in one line.</returns>
    public override string ToString()
    {
        var content = Content?.ToString() ?? string.Empty;
        return $"{Role}: {(content.Length <= 40 ? content : string.Concat(content.AsSpan(0, 37), "…"))}";
    }
}

/// <summary>The roles a chat model's messages are published with.</summary>
public static class WorkersAiChatRoles
{
    /// <summary>Instructions the conversation starts with.</summary>
    public const string System = "system";

    /// <summary>The person calling the model.</summary>
    public const string User = "user";

    /// <summary>The model itself, in an earlier turn.</summary>
    public const string Assistant = "assistant";

    /// <summary>The answer to a tool call.</summary>
    public const string Tool = "tool";
}

/// <summary>One part of a multimodal message: some text, or an image by URL.</summary>
public sealed class WorkersAiMessagePart
{
    /// <summary>What kind of part this is, such as <c>text</c> or <c>image_url</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>The text, for a text part.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>The image, for an image part.</summary>
    [JsonPropertyName("image_url")]
    public WorkersAiImageUrl? ImageUrl { get; set; }

    /// <summary>A part carrying text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>A part of type <c>text</c>.</returns>
    public static WorkersAiMessagePart OfText(string text) =>
        new() { Type = WorkersAiMessagePartTypes.Text, Text = text };

    /// <summary>A part carrying an image, by URL.</summary>
    /// <param name="url">Where the image is.</param>
    /// <returns>A part of type <c>image_url</c>.</returns>
    public static WorkersAiMessagePart OfImageUrl(string url) =>
        new()
        {
            Type = WorkersAiMessagePartTypes.ImageUrl,
            ImageUrl = new WorkersAiImageUrl { Url = url },
        };

    /// <summary>Captures any field Cloudflare adds to a part.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The part's kind, on one line.</summary>
    /// <returns>The part described in one line.</returns>
    public override string ToString() => Type ?? "(no type)";
}

/// <summary>The part kinds a multimodal message is published with.</summary>
public static class WorkersAiMessagePartTypes
{
    /// <summary>A part carrying text.</summary>
    public const string Text = "text";

    /// <summary>A part carrying an image by URL.</summary>
    public const string ImageUrl = "image_url";
}

/// <summary>Where a multimodal message's image is.</summary>
public sealed class WorkersAiImageUrl
{
    /// <summary>The image's URL.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>Captures any field Cloudflare adds.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The URL.</summary>
    /// <returns>The image reference in one line.</returns>
    public override string ToString() => Url ?? "(no url)";
}

/// <summary>A tool a chat model may call, described the way OpenAI describes one.</summary>
public sealed class WorkersAiTool
{
    /// <summary>What kind of tool this is. Only <c>function</c> is published.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>The tool itself.</summary>
    [JsonPropertyName("function")]
    public WorkersAiToolFunction? Function { get; set; }

    /// <summary>A tool that calls a function.</summary>
    /// <param name="name">What the model calls it.</param>
    /// <param name="description">What it is for, in words the model can reason about.</param>
    /// <param name="parameters">The arguments it takes.</param>
    /// <returns>The tool.</returns>
    public static WorkersAiTool ForFunction(
        string name,
        string description,
        WorkersAiToolParameters parameters) =>
        new()
        {
            Type = WorkersAiToolTypes.Function,
            Function = new WorkersAiToolFunction
            {
                Name = name,
                Description = description,
                Parameters = parameters,
            },
        };

    /// <summary>Captures any field Cloudflare adds to a tool.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The tool's kind and name, on one line.</summary>
    /// <returns>The tool described in one line.</returns>
    public override string ToString() => $"{Type}: {Function?.Name}";
}

/// <summary>The tool kinds this library knows of.</summary>
public static class WorkersAiToolTypes
{
    /// <summary>A tool that calls a function.</summary>
    public const string Function = "function";
}

/// <summary>The function inside a tool.</summary>
public sealed class WorkersAiToolFunction
{
    /// <summary>What the model calls it, and what it gets back in a tool call.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>What it is for.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>The arguments it takes.</summary>
    [JsonPropertyName("parameters")]
    public WorkersAiToolParameters? Parameters { get; set; }

    /// <summary>Captures any field Cloudflare adds.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The function's name.</summary>
    /// <returns>The function in one line.</returns>
    public override string ToString() => Name ?? "(no name)";
}

/// <summary>The arguments a tool takes, as a small JSON Schema.</summary>
public sealed class WorkersAiToolParameters
{
    /// <summary>Always <c>object</c>, as published.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>The arguments, by name.</summary>
    [JsonPropertyName("properties")]
    public Dictionary<string, WorkersAiToolArgument>? Properties { get; set; }

    /// <summary>Which of them a call has to carry.</summary>
    [JsonPropertyName("required")]
    public List<string>? Required { get; set; }

    /// <summary>Builds an argument list.</summary>
    /// <param name="properties">The arguments, by name.</param>
    /// <param name="required">Which of them are required, or null for none.</param>
    /// <returns>An object schema for the arguments.</returns>
    public static WorkersAiToolParameters Object(
        Dictionary<string, WorkersAiToolArgument> properties,
        params string[] required)
    {
        var parameters = new WorkersAiToolParameters { Type = "object", Properties = properties };
        if (required.Length > 0)
        {
            parameters.Required = [.. required];
        }

        return parameters;
    }

    /// <summary>Captures any field Cloudflare adds.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Type and argument count, on one line.</summary>
    /// <returns>The arguments described in one line.</returns>
    public override string ToString() => $"{Type} with {Properties?.Count ?? 0} argument(s)";
}

/// <summary>One argument of a tool.</summary>
public sealed class WorkersAiToolArgument
{
    /// <summary>The JSON type, such as <c>string</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>What it is, in words the model can reason about.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>An argument.</summary>
    /// <param name="type">The JSON type, such as <c>string</c>.</param>
    /// <param name="description">What it is.</param>
    /// <returns>The argument.</returns>
    public static WorkersAiToolArgument Of(string type, string description) =>
        new() { Type = type, Description = description };

    /// <summary>Captures any field Cloudflare adds.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Type and description, on one line.</summary>
    /// <returns>The argument described in one line.</returns>
    public override string ToString() => $"{Type}: {Description}";
}

/// <summary>The older tool spelling: a name and Cloudflare Workers code to run.</summary>
public sealed class WorkersAiLegacyFunction
{
    /// <summary>What the model calls it.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>The Workers code that runs when the model calls it.</summary>
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    /// <summary>Captures any field Cloudflare adds.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The function's name.</summary>
    /// <returns>The function in one line.</returns>
    public override string ToString() => Name ?? "(no name)";
}

/// <summary>
/// The <c>text</c> field of an inference call, which is one string for a classification or translation and an
/// array of strings for a batch of embeddings.
/// </summary>
/// <remarks>
/// Cloudflare publishes this field as <c>string or array of string</c>. One JSON field cannot be two CLR
/// properties, so this carries either and writes whichever it holds. Both convert to it implicitly, so
/// <c>Text = "hello"</c> and <c>Text = new[] { "a", "b" }</c> are both the assignment a caller would write.
/// </remarks>
[JsonConverter(typeof(WorkersAiTextOrBatchConverter))]
public readonly struct WorkersAiTextOrBatch
{
    /// <summary>Builds one holding a single text.</summary>
    /// <param name="text">The text.</param>
    public WorkersAiTextOrBatch(string text)
    {
        Single = text;
        Batch = null;
    }

    /// <summary>Builds one holding a batch.</summary>
    /// <param name="texts">The texts, one vector each.</param>
    public WorkersAiTextOrBatch(IReadOnlyList<string> texts)
    {
        Single = null;
        Batch = texts;
    }

    /// <summary>The single text, when this holds one.</summary>
    public string? Single { get; }

    /// <summary>The batch, when this holds one.</summary>
    public IReadOnlyList<string>? Batch { get; }

    /// <summary>Whether this holds a batch rather than a single text.</summary>
    public bool IsBatch => Batch is not null;

    /// <summary>How many texts this holds, one way or the other.</summary>
    public int Count => Batch?.Count ?? (Single is null ? 0 : 1);

    /// <summary>Takes a single text.</summary>
    /// <param name="text">The text.</param>
    public static implicit operator WorkersAiTextOrBatch(string text) => new(text);

    /// <summary>Takes a batch of texts.</summary>
    /// <param name="texts">The texts.</param>
    public static implicit operator WorkersAiTextOrBatch(string[] texts) => new(texts);

    /// <summary>Takes a batch of texts.</summary>
    /// <param name="texts">The texts.</param>
    public static implicit operator WorkersAiTextOrBatch(List<string> texts) => new(texts);

    /// <summary>Either the text or the count of them, on one line.</summary>
    /// <returns>The field described in one line.</returns>
    public override string ToString() => Batch is { } batch ? $"{batch.Count} text(s)" : Single ?? string.Empty;
}

/// <summary>Writes a <see cref="WorkersAiTextOrBatch"/> as the string or the array it holds.</summary>
public sealed class WorkersAiTextOrBatchConverter : JsonConverter<WorkersAiTextOrBatch>
{
    /// <summary>Reads whichever shape arrived.</summary>
    /// <param name="reader">The reader positioned at the value.</param>
    /// <param name="typeToConvert">The type being read.</param>
    /// <param name="options">The serializer's options.</param>
    /// <returns>The field as it arrived.</returns>
    public override WorkersAiTextOrBatch Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? new WorkersAiTextOrBatch(reader.GetString()!)
            : new WorkersAiTextOrBatch(JsonSerializer.Deserialize<List<string>>(ref reader) ?? []);

    /// <summary>Writes the string or the array this holds.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The field.</param>
    /// <param name="options">The serializer's options.</param>
    public override void Write(
        Utf8JsonWriter writer,
        WorkersAiTextOrBatch value,
        JsonSerializerOptions options)
    {
        if (value.Batch is { } batch)
        {
            writer.WriteStartArray();
            foreach (var text in batch)
            {
                writer.WriteStringValue(text);
            }

            writer.WriteEndArray();
            return;
        }

        writer.WriteStringValue(value.Single);
    }
}

/// <summary>
/// The <c>image</c> field of an inference call, which is an array of 8-bit values for the vision models and a
/// base64 string for the multimodal ones.
/// </summary>
/// <remarks>
/// Cloudflare publishes both spellings for the same field name in different arms of the same union. This
/// carries either and writes whichever it holds, so a caller passes bytes or a base64 string and does not
/// have to know which arm the model in the path is - though <see cref="WorkersAiModelSchema"/> will tell it.
/// </remarks>
[JsonConverter(typeof(WorkersAiImageInputConverter))]
public readonly struct WorkersAiImageInput
{
    /// <summary>Builds one holding raw samples.</summary>
    /// <param name="bytes">The image, as 8-bit values.</param>
    public WorkersAiImageInput(IReadOnlyList<byte> bytes)
    {
        Bytes = bytes;
        Base64 = null;
    }

    /// <summary>Builds one holding base64.</summary>
    /// <param name="base64">The image, base64-encoded.</param>
    public WorkersAiImageInput(string base64)
    {
        Bytes = null;
        Base64 = base64;
    }

    /// <summary>The image as 8-bit values, when this holds them.</summary>
    public IReadOnlyList<byte>? Bytes { get; }

    /// <summary>The image as base64, when this holds that.</summary>
    public string? Base64 { get; }

    /// <summary>Whether this holds base64 rather than raw samples.</summary>
    public bool IsBase64 => Base64 is not null;

    /// <summary>Takes raw samples.</summary>
    /// <param name="bytes">The image, as 8-bit values.</param>
    public static implicit operator WorkersAiImageInput(byte[] bytes) => new(bytes);

    /// <summary>Takes raw samples.</summary>
    /// <param name="bytes">The image, as 8-bit values.</param>
    public static implicit operator WorkersAiImageInput(List<byte> bytes) => new(bytes);

    /// <summary>Takes a base64 image.</summary>
    /// <param name="base64">The image, base64-encoded.</param>
    /// <returns>The field holding that base64.</returns>
    public static WorkersAiImageInput FromBase64(string base64) => new(base64);

    /// <summary>Which spelling this holds and how much of it, on one line.</summary>
    /// <returns>The field described in one line.</returns>
    public override string ToString() =>
        Base64 is { } base64 ? $"base64, {base64.Length} character(s)" : $"{Bytes?.Count ?? 0} byte(s)";
}

/// <summary>Writes a <see cref="WorkersAiImageInput"/> as the array or the string it holds.</summary>
public sealed class WorkersAiImageInputConverter : JsonConverter<WorkersAiImageInput>
{
    /// <summary>Reads whichever shape arrived.</summary>
    /// <param name="reader">The reader positioned at the value.</param>
    /// <param name="typeToConvert">The type being read.</param>
    /// <param name="options">The serializer's options.</param>
    /// <returns>The field as it arrived.</returns>
    public override WorkersAiImageInput Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? new WorkersAiImageInput(reader.GetString()!)
            : new WorkersAiImageInput(JsonSerializer.Deserialize<List<byte>>(ref reader) ?? []);

    /// <summary>Writes the array of numbers or the base64 string this holds.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The field.</param>
    /// <param name="options">The serializer's options.</param>
    public override void Write(
        Utf8JsonWriter writer,
        WorkersAiImageInput value,
        JsonSerializerOptions options)
    {
        if (value.Base64 is { } base64)
        {
            writer.WriteStringValue(base64);
            return;
        }

        writer.WriteStartArray();
        foreach (var sample in value.Bytes ?? [])
        {
            writer.WriteNumberValue(sample);
        }

        writer.WriteEndArray();
    }
}

/// <summary>
/// The <c>content</c> of a chat message, which is a string for a text model and an array of parts for a
/// multimodal one.
/// </summary>
[JsonConverter(typeof(WorkersAiMessageContentConverter))]
public readonly struct WorkersAiMessageContent
{
    /// <summary>Builds one holding text.</summary>
    /// <param name="text">What was said.</param>
    public WorkersAiMessageContent(string text)
    {
        Text = text;
        Parts = null;
    }

    /// <summary>Builds one holding parts.</summary>
    /// <param name="parts">The parts of the message, in order.</param>
    public WorkersAiMessageContent(IReadOnlyList<WorkersAiMessagePart> parts)
    {
        Text = null;
        Parts = parts;
    }

    /// <summary>The text, when this holds text.</summary>
    public string? Text { get; }

    /// <summary>The parts, when this holds a multimodal message.</summary>
    public IReadOnlyList<WorkersAiMessagePart>? Parts { get; }

    /// <summary>Whether this holds parts rather than plain text.</summary>
    public bool IsMultimodal => Parts is not null;

    /// <summary>Takes plain text.</summary>
    /// <param name="text">What was said.</param>
    public static implicit operator WorkersAiMessageContent(string text) => new(text);

    /// <summary>Takes parts.</summary>
    /// <param name="parts">The parts of the message.</param>
    public static implicit operator WorkersAiMessageContent(WorkersAiMessagePart[] parts) => new(parts);

    /// <summary>Takes parts.</summary>
    /// <param name="parts">The parts of the message.</param>
    public static implicit operator WorkersAiMessageContent(List<WorkersAiMessagePart> parts) => new(parts);

    /// <summary>The text, or the kinds of the parts, on one line.</summary>
    /// <returns>The content described in one line.</returns>
    public override string ToString() =>
        Parts is { } parts
            ? string.Join("+", parts.Select(part => part.ToString()))
            : Text ?? string.Empty;
}

/// <summary>Writes a <see cref="WorkersAiMessageContent"/> as the string or the array it holds.</summary>
public sealed class WorkersAiMessageContentConverter : JsonConverter<WorkersAiMessageContent>
{
    /// <summary>Reads whichever shape arrived.</summary>
    /// <param name="reader">The reader positioned at the value.</param>
    /// <param name="typeToConvert">The type being read.</param>
    /// <param name="options">The serializer's options.</param>
    /// <returns>The content as it arrived.</returns>
    public override WorkersAiMessageContent Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? new WorkersAiMessageContent(reader.GetString()!)
            : new WorkersAiMessageContent(
                JsonSerializer.Deserialize<List<WorkersAiMessagePart>>(ref reader, options) ?? []);

    /// <summary>Writes the string or the array of parts this holds.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The content.</param>
    /// <param name="options">The serializer's options.</param>
    public override void Write(
        Utf8JsonWriter writer,
        WorkersAiMessageContent value,
        JsonSerializerOptions options)
    {
        if (value.Parts is { } parts)
        {
            JsonSerializer.Serialize(writer, parts, options);
            return;
        }

        writer.WriteStringValue(value.Text);
    }
}
