using System.Text;
using System.Text.Json;
using CFNet.Exceptions;
using CFNet.Models.WorkersAi;
using CFNet.UnitTests.Testing;
using Xunit;

namespace CFNet.UnitTests.Services;

/// <summary>
/// Inference: <c>POST /accounts/{account_id}/ai/run/{model_name}</c>. Worth pinning because the body is a
/// union of fifteen published shapes and the answer a union of thirteen, so which fields go out and which arm
/// comes back is the whole of the contract - and because three fields (<c>text</c>, <c>image</c> and a
/// message's <c>content</c>) are published as two different JSON kinds at once.
/// </summary>
public class WorkersAiRunTests
{
    private const string Model = "@cf/meta/llama-3.2-1b-instruct";

    [Fact]
    public async Task RunAsync_posts_the_inputs_to_the_model_path_with_no_wrapper()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """{"response":"pong","usage":{"completion_tokens":1,"prompt_tokens":9,"total_tokens":10}}""");

        var result = await harness.Client.WorkersAi.RunAsync(
            Model,
            new WorkersAiRunRequest
            {
                Prompt = "Say the single word pong.",
                MaxTokens = 16,
                Temperature = 0.5,
                TopK = 10,
                TopP = 0.9,
            });

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.Equal(1, harness.RequestCount);

        // The model's name goes into the path exactly as it is written, its slashes and its @ included: the
        // escaped form this library uses for every other path segment was refused live with 400 and error
        // 7000, "No route for that URI", because Cloudflare routes inference on the name as it is spelled.
        Assert.EndsWith(
            "/accounts/acct-1234/ai/run/@cf/meta/llama-3.2-1b-instruct",
            harness.LastRequest.Path,
            StringComparison.Ordinal);

        // The inputs go in the body directly, with no model/input/options wrapper around them.
        Assert.Equal(
            """{"prompt":"Say the single word pong.","max_tokens":16,"temperature":0.5,"top_k":10,"top_p":0.9}""",
            harness.LastRequest.Body);

        // The answer is an object, so the words and the arm agree.
        Assert.True(result.IsObject);
        Assert.Equal("pong", result.Text);
        Assert.Equal("pong", result.AsTextGeneration()!.Response);
        Assert.Null(result.AsTextGeneration()!.ToolCalls);
        Assert.Equal(10, result.AsTextGeneration()!.Usage!.TotalTokens);
        Assert.Equal("9 in + 1 out = 10 token(s)", result.AsTextGeneration()!.Usage!.ToString());
    }

    [Fact]
    public async Task A_chat_call_sends_the_conversation_in_the_order_it_was_given()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"response":"pong"}""");

        await harness.Client.WorkersAi.RunAsync(
            Model,
            WorkersAiRunRequest.ForMessages(
                WorkersAiChatMessage.System("Answer in one word."),
                WorkersAiChatMessage.User("Say pong.")));

        Assert.Equal(
            """{"messages":[{"role":"system","content":"Answer in one word."},{"role":"user","content":"Say pong."}]}""",
            harness.LastRequest.Body);
    }

    [Fact]
    public async Task A_multimodal_message_sends_its_parts_as_an_array_in_the_same_field()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"description":"a cat"}""");

        var result = await harness.Client.WorkersAi.RunAsync(
            "@cf/llava-v1.5-7b",
            WorkersAiRunRequest.ForMessages(
                WorkersAiChatMessage.Of(
                    "user",
                    WorkersAiMessagePart.OfText("What is in this image?"),
                    WorkersAiMessagePart.OfImageUrl("https://example.com/cat.png"))));

        // content is published as "string or array of object", and both spellings go out under the same name.
        Assert.Equal(
            """{"messages":[{"role":"user","content":[{"type":"text","text":"What is in this image?"},{"type":"image_url","image_url":{"url":"https://example.com/cat.png"}}]}]}""",
            harness.LastRequest.Body);

        Assert.Equal("a cat", result.Text);
        Assert.Equal("a cat", result.AsImageDescription()!.Description);
    }

    [Fact]
    public async Task Tools_are_described_the_way_a_model_expects_and_read_back_as_a_call()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """{"response":"","tool_calls":[{"name":"get_weather","arguments":{"city":"Paris"}}]}""");

        var request = WorkersAiRunRequest.ForMessages(WorkersAiChatMessage.User("Weather in Paris?"));
        request.Tools =
        [
            WorkersAiTool.ForFunction(
                "get_weather",
                "The weather in a city.",
                WorkersAiToolParameters.Object(
                    new Dictionary<string, WorkersAiToolArgument>
                    {
                        ["city"] = WorkersAiToolArgument.Of("string", "The city to look up."),
                    },
                    "city")),
        ];

        var result = await harness.Client.WorkersAi.RunAsync(Model, request);

        Assert.Equal(
            """{"messages":[{"role":"user","content":"Weather in Paris?"}],"tools":[{"type":"function","function":{"name":"get_weather","description":"The weather in a city.","parameters":{"type":"object","properties":{"city":{"type":"string","description":"The city to look up."}},"required":["city"]}}}]}""",
            harness.LastRequest.Body);

        var call = Assert.Single(result.AsTextGeneration()!.ToolCalls!);
        Assert.Equal("get_weather", call.Name);

        // The arguments are published as unknown, so they stay the JSON that arrived rather than a guess.
        Assert.Equal("Paris", call.Arguments!.Value.GetProperty("city").GetString());
    }

    [Fact]
    public async Task The_older_function_spelling_is_sent_as_published()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"response":"3"}""");

        var request = WorkersAiRunRequest.ForPrompt("Add one and two.");
        request.Functions = [new WorkersAiLegacyFunction { Name = "add", Code = "async (a, b) => a + b" }];

        await harness.Client.WorkersAi.RunAsync(Model, request);

        Assert.Equal(
            """{"prompt":"Add one and two.","functions":[{"name":"add","code":"async (a, b) => a + b"}]}""",
            harness.LastRequest.Body);
    }

    [Fact]
    public async Task Embeddings_take_one_text_or_a_batch_in_the_same_field()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"data":[[0.25,0.5]],"shape":[1,2]}""");
        harness.RespondWithResult("""{"data":[[0.25],[0.5]],"shape":[2,1]}""");

        var one = await harness.Client.WorkersAi.RunAsync(
            "@cf/baai/multilingual-e5-small",
            WorkersAiRunRequest.ForEmbeddings("hello"));
        Assert.Equal("""{"text":"hello"}""", harness.Requests[0].Body);

        var embeddings = one.AsEmbeddings()!;
        Assert.Equal(1, embeddings.VectorCount);
        Assert.Equal(2, embeddings.Dimensions);
        Assert.Equal(0.25, embeddings.Data![0][0]);
        Assert.Equal("1 vector(s) of 2", embeddings.ToString());

        var batch = await harness.Client.WorkersAi.RunAsync(
            "@cf/baai/multilingual-e5-small",
            WorkersAiRunRequest.ForEmbeddings(new[] { "hello", "world" }));
        Assert.Equal("""{"text":["hello","world"]}""", harness.Requests[1].Body);
        Assert.Equal(2, batch.AsEmbeddings()!.VectorCount);
        Assert.Equal(1, batch.AsEmbeddings()!.Dimensions);
    }

    [Fact]
    public async Task An_image_goes_out_as_bytes_or_as_base64_in_the_same_field()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""[{"label":"cat","score":0.97}]""");
        harness.RespondWithResult("""{"data":[[1]],"shape":[1,1]}""");

        // The vision models take an array of 8-bit values, which is what a byte list serialises to - not the
        // base64 string System.Text.Json writes for a byte array.
        var classified = await harness.Client.WorkersAi.RunAsync(
            "@cf/google/vit-base-patch16-224",
            WorkersAiRunRequest.ForImageClassification([1, 2, 255]));
        Assert.Equal("""{"image":[1,2,255]}""", harness.Requests[0].Body);

        var label = Assert.Single(classified.AsClassifications());
        Assert.Equal("cat", label.Label);
        Assert.Equal(0.97, label.Score);
        Assert.True(classified.IsArray);
        Assert.Null(classified.Text);
        Assert.Null(classified.AsTextGeneration());

        // The multimodal models take the same field as base64.
        await harness.Client.WorkersAi.RunAsync(
            "@cf/openai/clip-vit-base-patch32",
            WorkersAiRunRequest.ForMultimodalEmbeddings(imageBase64: "aGk="));
        Assert.Equal("""{"image":"aGk="}""", harness.Requests[1].Body);
    }

    [Fact]
    public async Task Audio_goes_out_as_an_array_of_numbers_and_a_transcript_reads_back_timed()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """{"text":"hello world","vtt":"00:00.000 --> 00:01.000 hello world","word_count":2,"words":[{"word":"hello","start":0,"end":0.42},{"word":"world","start":0.42,"end":1}]}""");

        var result = await harness.Client.WorkersAi.RunAsync(
            "@cf/openai/whisper-tiny-en",
            WorkersAiRunRequest.ForSpeechRecognition([9, 8, 7], "en"));

        Assert.Equal("""{"source_lang":"en","audio":[9,8,7]}""", harness.LastRequest.Body);

        var transcription = result.AsTranscription()!;
        Assert.Equal("hello world", transcription.Text);
        Assert.Equal("hello world", result.Text);
        Assert.Equal(2, transcription.WordCount);
        Assert.Equal(0.42, transcription.Words![0].End);
        Assert.Equal(1d, transcription.Words[1].End);
        Assert.Equal("hello world [2 word(s)]", transcription.ToString());
    }

    [Fact]
    public async Task A_json_answer_is_asked_for_by_response_format()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"response":"{\"answer\":1}"}""");
        harness.RespondWithResult("""{"response":"{\"answer\":1}"}""");

        var request = WorkersAiRunRequest.ForPrompt("One JSON object.");
        request.ResponseFormat = WorkersAiResponseFormat.JsonObject();
        await harness.Client.WorkersAi.RunAsync(Model, request);
        Assert.Equal(
            """{"prompt":"One JSON object.","response_format":{"type":"json_object"}}""",
            harness.Requests[0].Body);

        // The schema is published as unknown, so it goes out as the JSON it was given.
        request.ResponseFormat = WorkersAiResponseFormat.ForSchema(
            JsonSerializer.SerializeToElement(new { type = "object" }));
        await harness.Client.WorkersAi.RunAsync(Model, request);
        Assert.Equal(
            """{"prompt":"One JSON object.","response_format":{"type":"json_schema","json_schema":{"type":"object"}}}""",
            harness.Requests[1].Body);
        Assert.Equal(2, WorkersAiResponseFormatTypes.All.Count);
    }

    [Fact]
    public async Task A_field_this_library_does_not_name_is_sent_alongside_the_ones_it_does()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"response":"x"}""");

        var request = WorkersAiRunRequest.ForPrompt("x");
        request.AdditionalData = new Dictionary<string, object?> { ["top_logprobs"] = 3 };

        await harness.Client.WorkersAi.RunAsync(Model, request);

        Assert.Equal("""{"prompt":"x","top_logprobs":3}""", harness.LastRequest.Body);
    }

    [Fact]
    public async Task The_other_arms_send_the_fields_they_are_published_with()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"translated_text":"bonjour"}""");
        harness.RespondWithResult("""{"translated_text":"bonjour"}""");
        harness.RespondWithResult("""{"summary":"short"}""");
        harness.RespondWithResult("""[{"label":"POSITIVE","score":0.99}]""");
        harness.RespondWithResult("""{"image":"aGk="}""");
        harness.RespondWithResult("""{"audio":"aGVsbG8="}""");

        await harness.Client.WorkersAi.RunAsync(
            "@cf/meta/m2m100-1.2b",
            WorkersAiRunRequest.ForTranslation("hello", "fr"));
        Assert.Equal("""{"text":"hello","target_lang":"fr"}""", harness.Requests[0].Body);

        await harness.Client.WorkersAi.RunAsync(
            "@cf/meta/m2m100-1.2b",
            WorkersAiRunRequest.ForTranslation("hello", "fr", "en"));
        Assert.Equal(
            """{"text":"hello","target_lang":"fr","source_lang":"en"}""",
            harness.Requests[1].Body);

        await harness.Client.WorkersAi.RunAsync(
            "@cf/facebook/bart-large-cnn",
            WorkersAiRunRequest.ForSummarization("a long text", 64));
        Assert.Equal("""{"input_text":"a long text","max_length":64}""", harness.Requests[2].Body);

        await harness.Client.WorkersAi.RunAsync(
            "@cf/huggingface/distilbert-sst-2-int8",
            WorkersAiRunRequest.ForClassification("great!"));
        Assert.Equal("""{"text":"great!"}""", harness.Requests[3].Body);

        var image = WorkersAiRunRequest.ForTextToImage("a red square");
        image.Height = 512;
        image.Width = 512;
        image.NumSteps = 4;
        image.Guidance = 7.5;
        image.Strength = 0.5;
        image.NegativePrompt = "blur";
        image.Seed = 42;
        var drawn = await harness.Client.WorkersAi.RunAsync("@cf/stable-diffusion-xl-light", image);
        Assert.Equal(
            """{"prompt":"a red square","seed":42,"guidance":7.5,"height":512,"width":512,"num_steps":4,"strength":0.5,"negative_prompt":"blur"}""",
            harness.Requests[4].Body);
        Assert.Equal(new byte[] { 104, 105 }, drawn.AsImage()!.Bytes);
        Assert.Equal("4 base64 character(s)", drawn.AsImage()!.ToString());

        var spoken = await harness.Client.WorkersAi.RunAsync(
            "@cf/myshell-ai/melotts",
            WorkersAiRunRequest.ForTextToSpeech("hello", "en"));
        Assert.Equal("""{"prompt":"hello","lang":"en"}""", harness.Requests[5].Body);
        Assert.Equal("hello", Encoding.UTF8.GetString(spoken.AsAudio()!.Bytes!));
    }

    [Fact]
    public async Task A_streamed_answer_is_refused_before_anything_is_sent()
    {
        using var harness = CloudflareHarness.Create();

        var request = WorkersAiRunRequest.ForPrompt("x");
        request.Stream = true;

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.WorkersAi.RunAsync(Model, request));

        Assert.Equal(0, harness.RequestCount);
        Assert.Contains("server-sent events", exception.Problems[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_request_with_nothing_to_run_on_is_refused()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.WorkersAi.RunAsync(Model, new WorkersAiRunRequest()));

        Assert.Equal(0, harness.RequestCount);
        Assert.Contains("has to carry something", exception.Problems[0], StringComparison.Ordinal);
        Assert.True(new WorkersAiRunRequest().IsEmpty);
        Assert.False(WorkersAiRunRequest.ForPrompt("x").IsEmpty);

        // A field this library does not name is still something to run on.
        var unmodelled = new WorkersAiRunRequest
        {
            AdditionalData = new Dictionary<string, object?> { ["query"] = "x" },
        };
        Assert.False(unmodelled.IsEmpty);
    }

    [Fact]
    public async Task RunAsync_refuses_a_model_name_that_would_change_what_the_request_means()
    {
        using var harness = CloudflareHarness.Create();

        // The name is checked rather than escaped, so these are what checking is for: each would put something
        // into the path that is not part of a model's name.
        var refused = new[]
        {
            "@cf/meta llama",
            "@cf/meta\tllama",
            "@cf/meta?x=1",
            "@cf/meta#fragment",
            "%40cf/meta",
            "/@cf/meta",
            "@cf/meta/",
        };
        foreach (var name in refused)
        {
            var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
                () => harness.Client.WorkersAi.RunAsync(name, WorkersAiRunRequest.ForPrompt("x")));
            Assert.False(string.IsNullOrEmpty(exception.Problems[0]));
        }

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task A_blank_model_name_is_refused_before_anything_is_sent()
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.WorkersAi.RunAsync("   ", WorkersAiRunRequest.ForPrompt("x")));

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task A_request_cannot_carry_two_arms_at_once()
    {
        using var harness = CloudflareHarness.Create();

        var bothTexts = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.WorkersAi.RunAsync(
                Model,
                new WorkersAiRunRequest { Prompt = "x", Text = "y" }));
        Assert.Contains("cannot both be set", bothTexts.Problems[0], StringComparison.Ordinal);

        var bothConversations = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.WorkersAi.RunAsync(
                Model,
                new WorkersAiRunRequest
                {
                    Prompt = "x",
                    Messages = [WorkersAiChatMessage.User("y")],
                }));
        Assert.Contains("cannot both be set", bothConversations.Problems[0], StringComparison.Ordinal);

        var emptyConversation = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.WorkersAi.RunAsync(
                Model,
                new WorkersAiRunRequest { Messages = [] }));
        Assert.Contains("at least one message", emptyConversation.Problems[0], StringComparison.Ordinal);

        var emptyBatch = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.WorkersAi.RunAsync(
                Model,
                WorkersAiRunRequest.ForEmbeddings(Array.Empty<string>())));
        Assert.Contains("at least one text", emptyBatch.Problems[0], StringComparison.Ordinal);

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task The_published_bounds_are_enforced_in_order_and_the_unpublished_ones_are_not()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.WorkersAi.RunAsync(
                Model,
                new WorkersAiRunRequest
                {
                    Prompt = "x",
                    InputText = string.Empty,
                    TargetLang = string.Empty,
                    MaxTokens = -1,
                    Height = 100,
                    Width = 4096,
                    NumSteps = 0,
                    Strength = 1.5,
                }));

        Assert.Equal(0, harness.RequestCount);

        // Seven, in the order EnsureValid looks for them. The two-arms-at-once rules are not among them: this
        // request carries a prompt and no text, which is one arm, and the rules that refuse two are pinned by
        // their own fact.
        Assert.Equal(7, exception.Problems.Count);
        Assert.Equal(
            "input_text has to be at least one character.|" +
            "target_lang has to be at least one character.|" +
            "max_tokens cannot be negative.|" +
            "height has to be between 256 and 2048 pixels, as Cloudflare publishes it.|" +
            "width has to be between 256 and 2048 pixels, as Cloudflare publishes it.|" +
            "num_steps has to be between 1 and 20, as Cloudflare publishes it.|" +
            "strength has to be between 0 and 1, as Cloudflare publishes it.",
            string.Join("|", exception.Problems));

        // The sampling knobs have no published range, so a strange one is Cloudflare's to refuse: this
        // library sends it rather than inventing a bound.
        harness.RespondWithResult("""{"response":"y"}""");
        await harness.Client.WorkersAi.RunAsync(
            Model,
            new WorkersAiRunRequest { Prompt = "x", Temperature = 99, TopP = 7, Seed = -3 });
        Assert.Equal(
            """{"prompt":"x","temperature":99,"top_p":7,"seed":-3}""",
            harness.LastRequest.Body);
        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task An_answer_that_is_a_bare_string_reads_as_the_words()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("\"just the words\"");

        var result = await harness.Client.WorkersAi.RunAsync(Model, WorkersAiRunRequest.ForPrompt("x"));

        Assert.Equal(JsonValueKind.String, result.Kind);
        Assert.Equal("just the words", result.Text);
        Assert.Equal("string: just the words", result.ToString());

        // The projections guard on the shape that arrived, so an arm that does not fit answers nothing rather
        // than throwing - and the escape hatch, which does not guard, says so.
        Assert.Null(result.AsTextGeneration());
        Assert.Null(result.AsEmbeddings());
        Assert.Empty(result.AsClassifications());
        Assert.Throws<JsonException>(() => result.As<WorkersAiTranslation>());
    }

    [Fact]
    public async Task An_array_answer_reads_as_detections_with_their_boxes()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """[{"label":"car","score":0.9,"box":{"xmin":1.5,"ymin":2,"xmax":30.25,"ymax":40}}]""");

        var result = await harness.Client.WorkersAi.RunAsync(
            "@cf/facebook/detr-resnet-50",
            WorkersAiRunRequest.ForObjectDetection([1, 2, 3]));

        Assert.Equal("""{"image":[1,2,3]}""", harness.LastRequest.Body);
        Assert.Equal("array of 1 item(s)", result.ToString());

        var detection = Assert.Single(result.AsDetections());
        Assert.Equal("car", detection.Label);
        Assert.Equal(1.5, detection.Box!.Xmin);
        Assert.Equal(40d, detection.Box.Ymax);
        Assert.Equal("(1.5,2)-(30.25,40)", detection.Box.ToString());
        Assert.Null(result.Text);
        Assert.Null(result.AsTextGeneration());
    }

    [Fact]
    public async Task The_words_are_found_whichever_field_of_whichever_arm_carries_them()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"translated_text":"bonjour"}""");
        harness.RespondWithResult("""{"summary":"short"}""");
        harness.RespondWithResult("""{"description":"a cat"}""");
        harness.RespondWithResult("""{"text":"a transcript"}""");

        var translated = await harness.Client.WorkersAi.RunAsync(
            "@cf/meta/m2m100-1.2b",
            WorkersAiRunRequest.ForTranslation("hello", "fr"));
        Assert.Equal("bonjour", translated.Text);
        Assert.Equal("bonjour", translated.AsTranslation()!.TranslatedText);

        var summarised = await harness.Client.WorkersAi.RunAsync(
            "@cf/facebook/bart-large-cnn",
            WorkersAiRunRequest.ForSummarization("long"));
        Assert.Equal("short", summarised.Text);
        Assert.Equal("short", summarised.AsSummarization()!.Summary);

        var described = await harness.Client.WorkersAi.RunAsync(
            "@cf/llava-v1.5-7b",
            WorkersAiRunRequest.ForImageToText([1, 2]));
        Assert.Equal("a cat", described.Text);
        Assert.Equal("""{"image":[1,2]}""", harness.Requests[2].Body);

        var transcribed = await harness.Client.WorkersAi.RunAsync(
            "@cf/openai/whisper-tiny-en",
            WorkersAiRunRequest.ForSpeechRecognition([1]));
        Assert.Equal("a transcript", transcribed.Text);
    }

    [Fact]
    public async Task A_field_this_library_has_not_named_on_an_arm_is_kept()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"response":"x","finish_reason":"stop"}""");

        var generation = (await harness.Client.WorkersAi.RunAsync(Model, WorkersAiRunRequest.ForPrompt("x")))
            .AsTextGeneration()!;

        Assert.Equal("x", generation.Response);
        Assert.Equal("stop", generation.AdditionalData!["finish_reason"].GetString());
    }

    [Fact]
    public async Task An_answer_with_nothing_in_it_reads_as_nothing_rather_than_throwing()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("null");

        var result = await harness.Client.WorkersAi.RunAsync(Model, WorkersAiRunRequest.ForPrompt("x"));

        Assert.True(result.Kind is JsonValueKind.Null or JsonValueKind.Undefined);
        Assert.Null(result.Text);
        Assert.Null(result.AsTextGeneration());
        Assert.Empty(result.AsClassifications());
        Assert.Null(result.As<WorkersAiEmbeddings>());
    }
}
