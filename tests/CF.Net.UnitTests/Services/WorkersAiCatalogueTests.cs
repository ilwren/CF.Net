using System.Net;
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
        Assert.Equal("object", schema.Input.ToString());
        Assert.Equal("object (open)", schema.Output.ToString());
    }

    [Fact]
    public async Task A_model_schema_keeps_the_part_of_the_schema_cloudflare_does_not_publish()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """{"input":{"additionalProperties":false,"description":"d","type":"object","properties":{"prompt":{"type":"string"}},"required":["prompt"],"oneOf":[{"a":1}]},"output":{"additionalProperties":true,"description":"d","type":"object"}}""");

        var schema = await harness.Client.WorkersAi.GetModelSchemaAsync("@cf/meta/llama-3.2-3b-instruct");

        // The published shape is three fields, which is not a usable JSON Schema on its own: the properties
        // that say what a model takes are the useful half, and they are preserved rather than lost.
        Assert.Equal(3, schema.Input!.AdditionalData!.Count);
        Assert.True(schema.Input.AdditionalData.ContainsKey("properties"));
        Assert.True(schema.Input.AdditionalData.ContainsKey("required"));
        Assert.True(schema.Input.AdditionalData.ContainsKey("oneOf"));
        Assert.Equal(
            "prompt",
            schema.Input.AdditionalData["required"].EnumerateArray().Single().GetString());
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
}
