using System.Net;
using System.Text.Json;
using CFNet.Exceptions;
using CFNet.Models.WorkersAi;
using CFNet.UnitTests.Testing;
using Xunit;

namespace CFNet.UnitTests.Services;

/// <summary>
/// The Workers AI catalogue: what an account can run inference against. Worth pinning because the paths are
/// not under <c>workers/</c> at all, because two of the three searches answer a bare array with no filters,
/// and because the published schema types all three answers as <c>unknown</c> - so what the models preserve
/// rather than name matters as much as what they name.
/// </summary>
public class WorkersAiCatalogueTests
{
    private const string ModelJson =
        """{"id":"c323a2b0-6bee-4a1c-9c8f-0a1b2c3d4e5f","name":"@cf/meta/llama-3.2-3b-instruct","description":"Llama 3.2 3B Instruct, quantised to int8.","task":{"id":"0f5d8e2d-1a2b-4c3d-9e8f-0a1b2c3d4e5f","name":"text-generation","description":"Family of generative models."},"tags":["text-generation","openrouter","llama"],"properties":[{"property_id":"beta","value":false},{"property_id":"recommended","value":"true"},{"property_id":"deprecation_date","value":"2026-12-31"},{"property_id":"context_window","value":131072}],"limits":{"max_input_tokens":131072,"max_output_tokens":8192},"batching":{"max_batch_size":16,"min_delay_seconds":2.5},"pricing":{"unit_cost":0.011}}""";

    [Fact]
    public async Task ListModelsAsync_gets_the_model_search_with_no_query_when_nothing_is_asked_for()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage($"[{ModelJson}]", page: 1, totalPages: 1, count: 1, totalCount: 1);

        var page = await harness.Client.WorkersAi.ListModelsAsync();

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/ai/models/search",
            harness.LastRequest.Path,
            StringComparison.Ordinal);

        // No filters is no query string at all: the catalogue is small enough to come back whole.
        Assert.Equal(string.Empty, harness.LastRequest.Query);
        Assert.Equal(1, page.Count);
        Assert.Equal(1, page.TotalCount);
        Assert.False(page.HasMore);
        Assert.Null(page.NextPage);
    }

    [Fact]
    public async Task The_model_search_filters_go_out_in_snake_case()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");

        await harness.Client.WorkersAi.ListModelsAsync(
            new WorkersAiModelListParameters
            {
                Page = 2,
                PerPage = 50,
                Author = "google",
                Format = WorkersAiModelFormats.OpenRouter,
                HideExperimental = true,
                IncludeDeprecated = false,
                Search = "llama",
                Source = 1,
                Task = "text-generation",
            });

        // A false that was set goes out as false: only a null filter is left out.
        Assert.Equal(
            "page=2&per_page=50&author=google&format=openrouter&hide_experimental=true" +
            "&include_deprecated=false&search=llama&source=1&task=text-generation",
            harness.LastRequest.Query.TrimStart('?'));
    }

    [Fact]
    public async Task A_model_reads_its_task_tags_limits_and_batching()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage($"[{ModelJson}]", page: 1, totalPages: 1, count: 1, totalCount: 1);

        var page = await harness.Client.WorkersAi.ListModelsAsync();
        var model = Assert.Single(page.Items);

        Assert.Equal("c323a2b0-6bee-4a1c-9c8f-0a1b2c3d4e5f", model.Id);
        Assert.Equal("@cf/meta/llama-3.2-3b-instruct", model.Name);
        Assert.Equal("text-generation", model.Task!.Name);
        Assert.Equal("@cf/meta/llama-3.2-3b-instruct (text-generation)", model.ToString());

        Assert.Equal(3, model.Tags!.Count);
        Assert.True(model.HasTag("llama"));
        Assert.True(model.HasTag("Text-Generation"), "a tag is matched whatever case it is asked for");
        Assert.False(model.HasTag("embeddings"));

        Assert.Equal(131072, model.Limits!.MaxInputTokens);
        Assert.Equal(8192, model.Limits.MaxOutputTokens);
        Assert.Equal("131072 in, 8192 out", model.Limits.ToString());
        Assert.Equal(16, model.Batching!.MaxBatchSize);
        Assert.Equal(2.5, model.Batching.MinDelaySeconds);
        Assert.Equal("16 per batch", model.Batching.ToString());

        // The published schema types these items as "unknown", so a field this library does not name is kept
        // rather than dropped - and reported by the integration run.
        Assert.True(model.AdditionalData!.ContainsKey("pricing"));
    }

    [Fact]
    public async Task A_catalogue_property_keeps_the_kind_of_value_it_arrived_as()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage($"[{ModelJson}]", page: 1, totalPages: 1, count: 1, totalCount: 1);

        var model = Assert.Single((await harness.Client.WorkersAi.ListModelsAsync()).Items);

        Assert.Equal(4, model.Properties!.Count);

        // The same property carries a boolean here, a string there and a number elsewhere, so the value is
        // the JSON it arrived as and ValueText prints whichever it was.
        Assert.Equal("false", model.Property("beta")!.ValueText);
        Assert.Equal("true", model.Property("recommended")!.ValueText);
        Assert.Equal("2026-12-31", model.Property("deprecation_date")!.ValueText);
        Assert.Equal(System.Text.Json.JsonValueKind.Number, model.Property("context_window")!.Value!.Value.ValueKind);
        Assert.Equal("131072", model.Property("context_window")!.ValueText);
        Assert.Equal("beta=false", model.Property("beta")!.ToString());
        Assert.Null(model.Property("nonexistent"));
    }

    [Fact]
    public async Task ListAllModelsAsync_walks_every_page_and_keeps_the_filters_it_was_given()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage($"[{ModelJson}]", page: 1, totalPages: 2, count: 1, totalCount: 2);
        harness.RespondWithPage(
            """[{"id":"another","name":"@cf/openai/whisper","task":{"name":"automatic-speech-recognition"}}]""",
            page: 2,
            totalPages: 2,
            count: 1,
            totalCount: 2);

        var models = new List<WorkersAiModel>();
        await foreach (var model in harness.Client.WorkersAi.ListAllModelsAsync(
                           new WorkersAiModelListParameters { Task = "text-generation" }))
        {
            models.Add(model);
        }

        Assert.Equal(2, models.Count);
        Assert.Equal("@cf/meta/llama-3.2-3b-instruct", models[0].Name);
        Assert.Equal("automatic-speech-recognition", models[1].Task!.Name);

        Assert.Equal(2, harness.RequestCount);
        Assert.Equal("page=1&task=text-generation", harness.Requests[0].Query.TrimStart('?'));
        Assert.Equal("page=2&task=text-generation", harness.Requests[1].Query.TrimStart('?'));
    }

    [Fact]
    public async Task A_model_search_that_answers_nothing_reads_as_an_empty_page()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");

        var page = await harness.Client.WorkersAi.ListModelsAsync(
            new WorkersAiModelListParameters { Search = "nothing-by-this-name" });

        Assert.Empty(page.Items);
        Assert.False(page.HasMore);
        Assert.Null(page.TotalCount);
    }

    [Fact]
    public async Task GetModelSchemaAsync_asks_for_one_model_by_its_full_name()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """{"input":{"additionalProperties":false,"description":"Input for a text-generation model.","type":"object","properties":{"prompt":{"type":"string","minLength":1}},"required":["prompt"]},"output":{"additionalProperties":true,"description":"Output of a text-generation model.","type":"object","oneOf":[{"properties":{"response":{"type":"string"}}}]}}""");

        var schema = await harness.Client.WorkersAi.GetModelSchemaAsync("@cf/meta/llama-3.2-3b-instruct");

        Assert.EndsWith(
            "/accounts/acct-1234/ai/models/schema",
            harness.LastRequest.Path,
            StringComparison.Ordinal);

        // The model name carries a namespace and slashes, so it goes out escaped in the query.
        Assert.Equal(
            "model=%40cf%2Fmeta%2Fllama-3.2-3b-instruct",
            harness.LastRequest.Query.TrimStart('?'));

        Assert.Equal("object", schema.Input!.Type);
        Assert.False(schema.Input.AdditionalProperties);
        Assert.Equal("Input for a text-generation model.", schema.Input.Description);
        Assert.Equal("object", schema.Output!.Type);
        Assert.True(schema.Output.AdditionalProperties);
        Assert.Equal("object in, object out", schema.ToString());

        // The three published fields are not all this answer carried: the input names the field a call has to
        // send and the output offers one alternative shape, and both are read as a schema rather than kept
        // raw. The one-line description says how much of the shape arrived.
        Assert.Equal("object 1 field(s)", schema.Input.ToString());
        Assert.Equal("object (open) 1 alternative(s)", schema.Output.ToString());
        Assert.Equal("string", schema.Input.Properties!["prompt"].Type);
        Assert.Equal("prompt", string.Join(",", schema.Input.Required!));
        Assert.Equal("string", schema.Output.OneOf![0].Properties!["response"].Type);

        // A keyword this library does not model still costs nothing, nested as deep as it arrives.
        Assert.Single(schema.Input.Properties["prompt"].AdditionalData!);
        Assert.Equal(1, schema.Input.Properties["prompt"].AdditionalData["minLength"].GetInt32());
    }

    [Fact]
    public async Task A_model_schema_reads_the_model_specific_shape_cloudflare_really_answers()
    {
        using var harness = CloudflareHarness.Create();

        // The answer the live suite recorded for @cf/pipecat-ai/smart-turn-v2 - not the three-field example
        // Cloudflare publishes. The input offers two alternative shapes and the output names its media type
        // and its two fields, so the published example understates this endpoint by a good deal.
        harness.RespondWithResult(
            """{"input":{"type":"object","oneOf":[{"properties":{"audio":{"type":"object","description":"readable stream with audio data and content-type specified for that data","properties":{"body":{"type":"object"},"contentType":{"type":"string"}},"required":["body","contentType"]},"dtype":{"type":"string","description":"type of data PCM data that's sent to the inference server as raw array","enum":["uint8","float32","float64"]}},"required":["audio"]},{"properties":{"audio":{"type":"string","description":"base64 encoded audio data"},"dtype":{"type":"string","description":"type of data PCM data that's sent to the inference server as raw array","enum":["uint8","float32","float64"]}},"required":["audio"]}]},"output":{"type":"object","contentType":"application/json","properties":{"is_complete":{"type":"boolean","description":"if true, end-of-turn was detected"},"probability":{"type":"number","description":"probability of the end-of-turn detection"}}}}""");

        var schema = await harness.Client.WorkersAi.GetModelSchemaAsync("@cf/pipecat-ai/smart-turn-v2");

        // Two of the three published fields were absent from this answer, and the type has to say so rather
        // than invent them. Nothing was left over, either: every field of it is modelled.
        Assert.Equal("object", schema.Input!.Type);
        Assert.Null(schema.Input.Description);
        Assert.Null(schema.Input.AdditionalProperties);
        Assert.Null(schema.Input.AdditionalData);

        // The input is a choice between a readable stream and base64 audio, and reading that choice is the
        // whole reason to ask for a schema before building an inference call.
        Assert.Equal(2, schema.Input.OneOf!.Count);
        var streamed = schema.Input.OneOf[0];
        Assert.Equal("audio", string.Join(",", streamed.Required!));
        Assert.Equal("object", streamed.Properties!["audio"].Type);
        Assert.Equal(
            "readable stream with audio data and content-type specified for that data",
            streamed.Properties["audio"].Description);
        Assert.Equal("body,contentType", string.Join(",", streamed.Properties["audio"].Required!));
        Assert.Equal("object", streamed.Properties["audio"].Properties!["body"].Type);
        Assert.Equal("string", streamed.Properties["audio"].Properties!["contentType"].Type);

        var encoded = schema.Input.OneOf[1];
        Assert.Equal("string", encoded.Properties!["audio"].Type);
        Assert.Equal("base64 encoded audio data", encoded.Properties["audio"].Description);
        Assert.Equal("audio", string.Join(",", encoded.Required!));

        // The enumeration of a PCM data type arrives as strings here, but the type keeps the JSON it was
        // given, because a schema may enumerate numbers or booleans as readily.
        var dtype = streamed.Properties!["dtype"];
        Assert.Equal(3, dtype.EnumValues!.Count);
        Assert.Equal(
            "uint8,float32,float64",
            string.Join(",", dtype.EnumValues.Select(value => value.GetString())));

        // The output names its media type and describes the two fields the model answers with, and the
        // property names stay exactly as the model's wire names them.
        Assert.Equal("application/json", schema.Output!.ContentType);
        Assert.Equal("boolean", schema.Output.Properties!["is_complete"].Type);
        Assert.Equal("if true, end-of-turn was detected", schema.Output.Properties["is_complete"].Description);
        Assert.Equal("number", schema.Output.Properties["probability"].Type);
        Assert.Null(schema.Output.AdditionalData);
        Assert.Equal("object 2 field(s)", schema.Output.ToString());
        Assert.Equal("object 2 alternative(s)", schema.Input.ToString());
    }

    [Fact]
    public async Task A_model_schema_keeps_the_keywords_it_does_not_model()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """{"input":{"additionalProperties":false,"description":"d","type":"object","$schema":"https://json-schema.org/draft/2020-12/schema","title":"Input","anyOf":[{"type":"string"}],"items":{"type":"number"}},"output":{"additionalProperties":true,"description":"d","type":"object"}}""");

        var schema = await harness.Client.WorkersAi.GetModelSchemaAsync("@cf/meta/llama-3.2-3b-instruct");

        // anyOf, allOf, items and $schema have not been seen in a live answer, so they are not modelled - and
        // not modelling them has to cost nothing, which is what the extension data is for.
        Assert.False(schema.Input!.AdditionalProperties);
        Assert.Equal("d", schema.Input.Description);
        Assert.Equal(4, schema.Input.AdditionalData!.Count);
        Assert.True(schema.Input.AdditionalData.ContainsKey("$schema"));
        Assert.True(schema.Input.AdditionalData.ContainsKey("title"));
        Assert.True(schema.Input.AdditionalData.ContainsKey("anyOf"));
        Assert.True(schema.Input.AdditionalData.ContainsKey("items"));
        Assert.Equal(
            "string",
            schema.Input.AdditionalData["anyOf"].EnumerateArray().Single().GetProperty("type").GetString());
        Assert.Equal("number", schema.Input.AdditionalData["items"].GetProperty("type").GetString());

        // The side with nothing extra reports nothing extra, rather than an empty bag.
        Assert.True(schema.Output!.AdditionalProperties);
        Assert.Null(schema.Output.AdditionalData);
    }

    [Fact]
    public async Task GetModelSchemaAsync_refuses_a_model_it_has_not_been_given()
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.WorkersAi.GetModelSchemaAsync("  "));

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task ListTasksAsync_reads_the_bare_array_the_task_search_answers()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """[{"id":"0f5d8e2d-1a2b-4c3d-9e8f-0a1b2c3d4e5f","name":"text-generation","description":"Family of generative models."},{"id":"another-task","name":"text-embeddings","description":"Turns text into vectors."}]""");

        var tasks = await harness.Client.WorkersAi.ListTasksAsync();

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/ai/tasks/search",
            harness.LastRequest.Path,
            StringComparison.Ordinal);

        // No filters are published for this one, so the account id in the path is the whole request.
        Assert.Equal(string.Empty, harness.LastRequest.Query);
        Assert.Equal(2, tasks.Count);
        Assert.Equal("text-generation", tasks[0].Name);
        Assert.Equal("text-generation", tasks[0].ToString());
        Assert.Equal("text-embeddings", tasks[1].Name);
        Assert.Equal("Turns text into vectors.", tasks[1].Description);
    }

    [Fact]
    public async Task ListAuthorsAsync_reads_the_bare_array_the_author_search_answers()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """[{"id":"google","name":"google","description":"Google DeepMind models."},{"id":"meta","name":"meta"}]""");

        var authors = await harness.Client.WorkersAi.ListAuthorsAsync();

        Assert.EndsWith(
            "/accounts/acct-1234/ai/authors/search",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Equal(2, authors.Count);
        Assert.Equal("google", authors[0].Name);
        Assert.Equal("Google DeepMind models.", authors[0].Description);
        Assert.Null(authors[1].Description);
    }

    [Fact]
    public async Task An_empty_catalogue_answer_reads_as_an_empty_list_rather_than_an_error()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");
        harness.RespondWithResult("[]");

        Assert.Empty(await harness.Client.WorkersAi.ListTasksAsync());
        Assert.Empty(await harness.Client.WorkersAi.ListAuthorsAsync());
        Assert.Equal(2, harness.RequestCount);
    }

    [Fact]
    public async Task The_marketplace_format_is_passed_through_and_not_read()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");

        await harness.Client.WorkersAi.ListModelsAsync(
            new WorkersAiModelListParameters { Format = WorkersAiModelFormats.OpenRouter });

        Assert.Equal("format=openrouter", harness.LastRequest.Query.TrimStart('?'));
        Assert.Equal("openrouter", Assert.Single(WorkersAiModelFormats.All));
    }

    /// <summary>
    /// A model item exactly as a live <c>GET …/ai/models/search</c> answered it, punctuation and all: a task
    /// named for a person rather than a slug, two properties whose values are two different JSON kinds, and
    /// two fields no documentation mentions.
    /// </summary>
    private const string LiveModelJson =
        """{"id":"eed32bc1-8775-4985-89ce-dd1405508ad8","name":"@cf/baai/bge-m3","description":"Multi-Functionality, Multi-Linguality, and Multi-Granularity embeddings model.","task":{"id":"0137cdcf-162a-4108-94f2-1ca59e8c65ee","name":"Text Embeddings","description":"Feature extraction models transform raw data into numerical features that can be processed while preserving the information in the original dataset."},"tags":[],"properties":[{"property_id":"context_window","value":"60000"},{"property_id":"price","value":[{"unit":"per M input tokens","price":0.0118,"currency":"USD"}]}],"source":1,"created_at":"2024-05-22 19:27:09.781"}""";

    [Fact]
    public async Task A_model_item_keeps_the_two_fields_a_live_catalogue_answers_that_no_documentation_does()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage($"[{LiveModelJson}]", page: 1, totalPages: 1, count: 1, totalCount: 1);

        var page = await harness.Client.WorkersAi.ListModelsAsync();
        var model = Assert.Single(page.Items);

        Assert.Equal("@cf/baai/bge-m3", model.Name);

        // The catalogue writes task names for a person - "Text Embeddings", not "text_embeddings" - so a caller
        // matching one against a slug has to normalise both.
        Assert.Equal("Text Embeddings", model.Task!.Name);

        Assert.Equal(1, model.Source);

        // Not ISO 8601: a space where the standard puts a T, and no offset. System.Text.Json reads a DateTime
        // by the standard alone, so this stays the string that arrived.
        Assert.Equal("2024-05-22 19:27:09.781", model.CreatedAt);

        // A property's value is the JSON it arrived as, whichever kind that is: a number written as a string
        // here, and a list of prices there.
        Assert.Equal("60000", model.Property("context_window")!.ValueText);
        Assert.Equal(JsonValueKind.Array, model.Property("price")!.Value!.Value.ValueKind);
        Assert.Empty(model.Tags!);
        Assert.Equal("@cf/baai/bge-m3 (Text Embeddings)", model.ToString());

        // Nothing is left over: source and created_at are the whole of what the documentation omits.
        Assert.True(model.AdditionalData is not { Count: > 0 });
    }

    [Fact]
    public void A_task_name_and_a_slug_are_one_task()
    {
        // The catalogue writes a task's name for a person and the documentation writes a slug, and both turn
        // up in one answer - so matching a model against a task has to bridge them.
        Assert.True(WorkersAiTaskNames.Same("Text Generation", "text-generation"));
        Assert.True(WorkersAiTaskNames.Same("text_generation", "Text Generation"));
        Assert.True(WorkersAiTaskNames.Same("  Text-to-Image ", "text to image"));
        Assert.False(WorkersAiTaskNames.Same("Text Generation", "Text Embeddings"));

        // Nothing said is not a task, so two empty names are not one task said twice.
        Assert.False(WorkersAiTaskNames.Same(null, null));
        Assert.False(WorkersAiTaskNames.Same("   ", string.Empty));
        Assert.False(WorkersAiTaskNames.Same(null, "text-generation"));
        Assert.Equal("text generation", WorkersAiTaskNames.Normalise(" Text-Generation "));
        Assert.Equal(string.Empty, WorkersAiTaskNames.Normalise(null));
    }

    [Fact]
    public async Task A_model_answers_whether_it_performs_a_task_in_either_spelling()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage($"[{LiveModelJson}]", page: 1, totalPages: 1, count: 1, totalCount: 1);

        var model = Assert.Single((await harness.Client.WorkersAi.ListModelsAsync()).Items);

        Assert.True(model.PerformsTask("Text Embeddings"));
        Assert.True(model.PerformsTask("text-embeddings"));
        Assert.False(model.PerformsTask("text-generation"));
        Assert.False(model.PerformsTask("  "));
    }

    [Fact]
    public async Task A_task_from_the_task_list_filters_the_model_search_by_its_name()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """[{"id":"c329a1f9-323d-4e91-b2aa-582dd4188d34","name":"Text Generation"}]""");
        harness.RespondWithResult("[]");

        var tasks = await harness.Client.WorkersAi.ListTasksAsync();
        await harness.Client.WorkersAi.ListModelsAsync(
            new WorkersAiModelListParameters { Task = Assert.Single(tasks).Name });

        // The name a task list gives is what the filter takes: a live search for it answered 39 models, where
        // the slug and the task's id each answered an empty page.
        Assert.Equal("task=Text%20Generation", harness.LastRequest.Query.TrimStart('?'));
    }
}
