using System.Net;
using CFNet.Exceptions;
using CFNet.Models.Common;
using CFNet.Models.Pages;
using CFNet.Models.Workers;
using CFNet.UnitTests.Testing;
using Xunit;

namespace CFNet.UnitTests.Services;

public class WorkersScriptsServiceTests
{
    [Fact]
    public async Task ListAsync_scopes_the_call_to_the_account()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage(
            "[{\"id\":\"hello\",\"etag\":\"e-1\",\"handlers\":[\"fetch\"]},{\"id\":\"world\"}]",
            page: 1, totalPages: 1, count: 2, totalCount: 2);

        var page = await harness.Client.Workers.ListAsync();

        Assert.Equal($"/client/v4/accounts/{CloudflareHarness.TestAccountId}/workers/scripts", harness.LastRequest.Path);
        Assert.Equal(2, page.Count);
        // The scripts list reports the name as "id"; Name covers whichever field Cloudflare used.
        Assert.Equal("hello", page.Items[0].Name);
        Assert.Equal("hello", page.Items[0].Id);
    }

    [Fact]
    public async Task ListAsync_applies_cursor_pagination_parameters()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");

        await harness.Client.Workers.ListAsync(new WorkerScriptListParameters
        {
            PerPage = 50,
            Cursor = "cursor-1",
            Name = "hello",
            Order = "script_name",
            Direction = SortDirection.Descending,
        });

        Assert.Equal(
            "cursor=cursor-1&per_page=50&name=hello&order=script_name&direction=desc",
            harness.LastRequest.Query.TrimStart('?'));
    }

    [Fact]
    public async Task ListAllAsync_follows_the_cursor_until_it_repeats()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("""
        {"success":true,"errors":[],"messages":[],
         "result":[{"id":"a"}],
         "result_info":{"cursors":{"before":"","after":"c-2"}}}
        """);
        harness.Respond("""
        {"success":true,"errors":[],"messages":[],
         "result":[{"id":"b"}],
         "result_info":{"cursors":{"before":"c-1","after":"c-2"}}}
        """);

        var scripts = await harness.Client.Workers.ListAllAsync().CollectAsync();

        Assert.Equal(new[] { "a", "b" }, scripts.Select(script => script.Name));
        Assert.Equal(2, harness.RequestCount);
        Assert.Equal(string.Empty, harness.Requests[0].Query.TrimStart('?'));
        Assert.Equal("cursor=c-2", harness.Requests[1].Query.TrimStart('?'));
    }

    [Fact]
    public async Task ListAllAsync_stops_when_the_cursor_disappears()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("""
        {"success":true,"errors":[],"messages":[],
         "result":[{"id":"a"}],
         "result_info":{"cursors":{"before":"c-1","after":""}}}
        """);

        var scripts = await harness.Client.Workers.ListAllAsync().CollectAsync();

        Assert.Single(scripts);
        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task GetAsync_returns_null_when_the_script_does_not_exist()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.NotFound, 10007, "Route not found");

        var script = await harness.Client.Workers.GetAsync("missing");

        Assert.Null(script);
        Assert.Equal($"/client/v4/accounts/{CloudflareHarness.TestAccountId}/workers/scripts/missing", harness.LastRequest.Path);
    }

    [Fact]
    public async Task GetAsync_parses_the_script_metadata()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""
        {
          "id": "hello",
          "etag": "etag-1",
          "handlers": ["fetch"],
          "compatibility_date": "2025-01-01",
          "compatibility_flags": ["nodejs_compat"],
          "usage_model": "bundled",
          "logpush": false,
          "created_on": "2024-01-01T00:00:00Z",
          "modified_on": "2024-02-01T00:00:00Z",
          "observability": { "enabled": true, "head_sampling_rate": 0.5 },
          "limits": { "cpu_ms": 50, "subrequests": 50 },
          "placement": { "mode": "off" },
          "tail_consumers": [ { "service": "logger", "environment": "production" } ]
        }
        """);

        var script = await harness.Client.Workers.GetAsync("hello");

        Assert.NotNull(script);
        Assert.Equal("hello", script!.Name);
        Assert.Equal("hello", script.Id);
        Assert.Equal("etag-1", script.Etag);
        Assert.Equal("fetch", Assert.Single(script.Handlers!));
        Assert.Equal("2025-01-01", script.CompatibilityDate);
        Assert.Equal("nodejs_compat", Assert.Single(script.CompatibilityFlags!));
        Assert.Equal("bundled", script.UsageModel);
        Assert.False(script.Logpush);
        Assert.Equal(2024, script.CreatedOn?.Year);
        Assert.True(script.Observability?.Enabled);
        Assert.Equal(0.5, script.Observability!.HeadSamplingRate);
        Assert.Equal(50, script.Limits?.CpuMs);
        Assert.Equal("off", script.Placement?.Mode);
        Assert.Equal("logger", Assert.Single(script.TailConsumers!)!.Service);
    }

    [Fact]
    public async Task GetContentAsync_uses_the_content_sub_resource()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"main_module\":\"worker.mjs\",\"modules\":[{\"name\":\"worker.mjs\",\"content_type\":\"application/javascript+module\"}]}");

        var content = await harness.Client.Workers.GetContentAsync("hello");

        Assert.Equal($"/client/v4/accounts/{CloudflareHarness.TestAccountId}/workers/scripts/hello/content", harness.LastRequest.Path);
        Assert.Equal("worker.mjs", content!.MainModule);
        Assert.Equal("application/javascript+module", Assert.Single(content.Modules!)!.ContentType);
    }

    [Fact]
    public async Task GetSettingsAsync_uses_script_settings_not_settings()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"logpush\":true,\"tags\":[\"t1\"],\"tail_consumers\":[],\"observability\":{\"enabled\":false}}");

        var settings = await harness.Client.Workers.GetSettingsAsync("hello");

        // The endpoint is /script-settings; /settings does not exist and 404s.
        Assert.EndsWith("/workers/scripts/hello/script-settings", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.True(settings!.Logpush);
        Assert.Equal("t1", Assert.Single(settings.Tags!));
    }

    [Fact]
    public async Task UploadAsync_sends_multipart_with_a_metadata_part_and_one_file_part_per_module()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"hello\",\"etag\":\"s-1\"}");

        var script = await harness.Client.Workers.UploadAsync(new UploadWorkerScriptRequest(
            "hello",
            "export default { async fetch(request, env) { return new Response('hi'); } }",
            "2025-01-01"));

        Assert.Equal("hello", script.Name);
        Assert.Equal(HttpMethod.Put, harness.LastRequest.Method);
        Assert.Equal($"/client/v4/accounts/{CloudflareHarness.TestAccountId}/workers/scripts/hello", harness.LastRequest.Path);
        Assert.StartsWith("multipart/form-data", harness.LastRequest.ContentType, StringComparison.Ordinal);

        var body = harness.LastRequest.Body!;

        // The metadata part carries the Cloudflare fields and nothing else.
        Assert.Contains("name=metadata", body, StringComparison.Ordinal);
        Assert.Contains("\"main_module\":\"worker.mjs\"", body, StringComparison.Ordinal);
        Assert.Contains("\"compatibility_date\":\"2025-01-01\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"script_name\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"modules\"", body, StringComparison.Ordinal);

        // The module travels as a file part named after the module, with the ES module media type.
        Assert.Contains("name=worker.mjs", body, StringComparison.Ordinal);
        Assert.Contains("filename", body, StringComparison.Ordinal);
        Assert.Contains("application/javascript+module", body, StringComparison.Ordinal);
        Assert.Contains("return new Response('hi')", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadAsync_includes_bindings_and_keep_bindings_in_the_metadata()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"hello\"}");

        await harness.Client.Workers.UploadAsync(new UploadWorkerScriptRequest("hello", "export default{}", "2025-01-01")
        {
            Bindings = new List<WorkerBinding>
            {
                WorkerBinding.KvNamespace("MY_KV", "ns-id"),
                WorkerBinding.D1Database("DB", "db-id"),
                WorkerBinding.R2Bucket("BUCKET", "bucket-name"),
                WorkerBinding.PlainText("ENV", "production"),
                WorkerBinding.SecretText("SECRET", "s3cret"),
                WorkerBinding.ToService("UPSTREAM", "other-worker"),
            },
            KeepBindings = new List<string> { WorkerBindingTypes.KvNamespace },
            Tags = new List<string> { "team:core" },
        });

        var body = harness.LastRequest.Body!;

        Assert.Contains("\"bindings\":[", body, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"kv_namespace\"", body, StringComparison.Ordinal);
        Assert.Contains("\"namespace_id\":\"ns-id\"", body, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"d1_database_binding\"", body, StringComparison.Ordinal);
        Assert.Contains("\"database_id\":\"db-id\"", body, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"r2_bucket\"", body, StringComparison.Ordinal);
        Assert.Contains("\"bucket_name\":\"bucket-name\"", body, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"plain_text\"", body, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"secret_text\"", body, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"service\"", body, StringComparison.Ordinal);
        Assert.Contains("\"service\":\"other-worker\"", body, StringComparison.Ordinal);
        Assert.Contains("\"keep_bindings\":[\"kv_namespace\"]", body, StringComparison.Ordinal);
        Assert.Contains("\"tags\":[\"team:core\"]", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadAsync_decodes_a_module_supplied_as_base64()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"hello\"}");

        await harness.Client.Workers.UploadAsync(new UploadWorkerScriptRequest
        {
            ScriptName = "hello",
            MainModule = "worker.mjs",
            CompatibilityDate = "2025-01-01",
            Modules = new List<WorkerModule>
            {
                new()
                {
                    Name = "worker.mjs",
                    ContentType = WorkerModuleContentTypes.JavaScriptModule,
                    ContentBase64 = Convert.ToBase64String("export default{}"u8.ToArray()),
                },
            },
        });

        Assert.Contains("export default{}", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadAsync_supports_multiple_modules_including_python()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"py\"}");

        var request = new UploadWorkerScriptRequest
        {
            ScriptName = "py",
            MainModule = "entry.py",
            CompatibilityDate = "2025-01-01",
            CompatibilityFlags = new List<string> { "python_workers" },
            Modules = new List<WorkerModule>
            {
                new("entry.py", "from js import Response\n\ndef on_fetch(request):\n    return Response('hi')\n", WorkerModuleContentTypes.Python),
                new("helper.py", "VALUE = 1\n", WorkerModuleContentTypes.Python),
            },
        };

        request.Validate();
        await harness.Client.Workers.UploadAsync(request);

        var body = harness.LastRequest.Body!;

        Assert.Contains("name=entry.py", body, StringComparison.Ordinal);
        Assert.Contains("name=helper.py", body, StringComparison.Ordinal);
        Assert.Contains("text/x-python", body, StringComparison.Ordinal);
        Assert.Contains("\"python_workers\"", body, StringComparison.Ordinal);
        Assert.Contains("\"main_module\":\"entry.py\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadAsync_falls_back_to_the_known_name_when_cloudflare_returns_no_result()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("null");

        var script = await harness.Client.Workers.UploadAsync("hello", "export default{}", "2025-01-01");

        Assert.Equal("hello", script.Name);
        Assert.Equal("hello", script.Id);
    }

    [Fact]
    public async Task UploadAsync_uses_todays_date_when_no_compatibility_date_is_supplied()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"hello\"}");

        await harness.Client.Workers.UploadAsync("hello", "export default{}");

        Assert.Contains($"\"compatibility_date\":\"{DateTime.UtcNow:yyyy-MM-dd}\"", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Upload_request_validation_catches_the_mistakes_that_produce_error_10021()
    {
        // Main module not present in the bundle.
        var mismatched = new UploadWorkerScriptRequest
        {
            ScriptName = "hello",
            MainModule = "worker.mjs",
            Modules = new List<WorkerModule> { new("other.mjs", "export default{}") },
        };
        var exception = Assert.Throws<CloudflareConfigurationException>(mismatched.Validate);
        Assert.Contains(exception.Problems, problem => problem.Contains("10021", StringComparison.Ordinal));

        // Neither module nor service-worker syntax.
        Assert.Throws<CloudflareConfigurationException>(
            () => new UploadWorkerScriptRequest { ScriptName = "hello", Modules = new List<WorkerModule> { new("w.mjs", "x") } }.Validate());

        // Both at once.
        Assert.Throws<CloudflareConfigurationException>(
            () => new UploadWorkerScriptRequest
            {
                ScriptName = "hello",
                MainModule = "w.mjs",
                BodyPart = "script",
                Modules = new List<WorkerModule> { new("w.mjs", "x") },
            }.Validate());

        // No modules at all.
        Assert.Throws<CloudflareConfigurationException>(
            () => new UploadWorkerScriptRequest { ScriptName = "hello", MainModule = "w.mjs" }.Validate());

        // No script name.
        Assert.Throws<CloudflareConfigurationException>(
            () => new UploadWorkerScriptRequest { MainModule = "w.mjs", Modules = new List<WorkerModule> { new("w.mjs", "x") } }.Validate());

        // Duplicate module names.
        Assert.Throws<CloudflareConfigurationException>(
            () => new UploadWorkerScriptRequest
            {
                ScriptName = "hello",
                MainModule = "w.mjs",
                Modules = new List<WorkerModule> { new("w.mjs", "x"), new("w.mjs", "y") },
            }.Validate());

        // A module with no content.
        Assert.Throws<CloudflareConfigurationException>(
            () => new UploadWorkerScriptRequest
            {
                ScriptName = "hello",
                MainModule = "w.mjs",
                Modules = new List<WorkerModule> { new() { Name = "w.mjs", ContentType = WorkerModuleContentTypes.JavaScriptModule } },
            }.Validate());

        // A module with no content type - the classic "unexpected token" upload failure.
        Assert.Throws<CloudflareConfigurationException>(
            () => new UploadWorkerScriptRequest
            {
                ScriptName = "hello",
                MainModule = "w.mjs",
                Modules = new List<WorkerModule> { new() { Name = "w.mjs", Bytes = "x"u8.ToArray() } },
            }.Validate());

        // A well formed request validates cleanly.
        new UploadWorkerScriptRequest("hello", "export default{}", "2025-01-01").Validate();
    }

    [Fact]
    public void A_module_exposes_its_text_and_describes_itself()
    {
        var module = new WorkerModule("worker.mjs", "export default{}", WorkerModuleContentTypes.JavaScriptModule);

        Assert.Equal("export default{}", module.Text);
        Assert.Equal("worker.mjs (application/javascript+module)", module.ToString());
        Assert.Null(new WorkerModule().Text);
    }

    [Fact]
    public async Task DeleteAsync_removes_the_script()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"hello\"}");

        await harness.Client.Workers.DeleteAsync("hello");

        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.Equal($"/client/v4/accounts/{CloudflareHarness.TestAccountId}/workers/scripts/hello", harness.LastRequest.Path);
    }

    [Fact]
    public async Task ListDeploymentsAsync_uses_the_deployments_sub_resource()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage("[{\"id\":\"d-1\",\"version_id\":\"v-1\"}]", page: 1, totalPages: 1, count: 1, totalCount: 1);

        var page = await harness.Client.Workers.ListDeploymentsAsync("hello");

        Assert.EndsWith("/workers/scripts/hello/deployments", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal("d-1", Assert.Single(page.Items).Id);
    }

    [Fact]
    public async Task CreateDeploymentAsync_defaults_the_strategy_and_validates_the_split()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"d-1\"}");

        var deployment = await harness.Client.Workers.CreateDeploymentAsync("hello", new CreateWorkerDeploymentRequest
        {
            Versions = new List<WorkerDeploymentVersion>
            {
                new() { VersionId = "v-1", Percentage = 75 },
                new() { VersionId = "v-2", Percentage = 25 },
            },
        });

        Assert.Equal("d-1", deployment.Id);
        Assert.Contains("\"strategy\":\"percentage\"", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"version_id\":\"v-1\"", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(50, 40)]
    [InlineData(100, 1)]
    [InlineData(0, 0)]
    public async Task CreateDeploymentAsync_rejects_a_split_that_is_not_exactly_one_hundred(double first, double second)
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Workers.CreateDeploymentAsync("hello", new CreateWorkerDeploymentRequest
            {
                Versions = new List<WorkerDeploymentVersion>
                {
                    new() { VersionId = "v-1", Percentage = first },
                    new() { VersionId = "v-2", Percentage = second },
                },
            }));

        Assert.Contains("100", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task CreateDeploymentAsync_needs_at_least_one_version()
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Workers.CreateDeploymentAsync("hello", new CreateWorkerDeploymentRequest()));
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task Subdomain_endpoints_are_addressed_per_account_and_per_script()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"subdomain\":\"myteam\",\"enabled\":true,\"previews_enabled\":false}");

        var subdomain = await harness.Client.Workers.GetSubdomainAsync();

        Assert.Equal($"/client/v4/accounts/{CloudflareHarness.TestAccountId}/workers/subdomain", harness.LastRequest.Path);
        Assert.Equal("myteam", subdomain!.Subdomain);
        Assert.True(subdomain.Enabled);
        Assert.False(subdomain.PreviewsEnabled);

        harness.RespondWithResult("{\"enabled\":true}");
        await harness.Client.Workers.UpdateSubdomainAsync(new UpdateWorkerSubdomainRequest { Enabled = true });

        Assert.Equal(HttpMethod.Patch, harness.LastRequest.Method);
        Assert.Equal("{\"enabled\":true}", harness.LastRequest.Body);

        harness.RespondWithResult("{\"enabled\":true}");
        await harness.Client.Workers.UpdateScriptSubdomainAsync("hello", new UpdateWorkerScriptSubdomainRequest { Enabled = false });

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.EndsWith("/workers/scripts/hello/subdomain", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal("{\"enabled\":false}", harness.LastRequest.Body);

        harness.RespondWithResult("{\"enabled\":true,\"previews_enabled\":true}");
        var scriptSubdomain = await harness.Client.Workers.GetScriptSubdomainAsync("hello");

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.True(scriptSubdomain!.Enabled);
    }

    [Fact]
    public void Worker_binding_factories_produce_the_documented_shapes()
    {
        Assert.Equal(WorkerBindingTypes.KvNamespace, WorkerBinding.KvNamespace("K", "ns").Type);
        Assert.Equal("ns", WorkerBinding.KvNamespace("K", "ns").NamespaceId);
        Assert.Equal(WorkerBindingTypes.D1DatabaseBinding, WorkerBinding.D1Database("D", "db").Type);
        Assert.Equal("db", WorkerBinding.D1Database("D", "db").DatabaseId);
        Assert.Equal(WorkerBindingTypes.R2Bucket, WorkerBinding.R2Bucket("B", "bk").Type);
        Assert.Equal("bk", WorkerBinding.R2Bucket("B", "bk").BucketName);
        Assert.Equal(WorkerBindingTypes.PlainText, WorkerBinding.PlainText("T", "v").Type);
        Assert.Equal(WorkerBindingTypes.SecretText, WorkerBinding.SecretText("S", "v").Type);
        Assert.Equal(WorkerBindingTypes.Service, WorkerBinding.ToService("U", "svc").Type);
        Assert.Equal("svc", WorkerBinding.ToService("U", "svc").Service);
        Assert.Equal(WorkerBindingTypes.DoNamespace, WorkerBinding.DurableObject("O", "Class").Type);
        Assert.Equal("Class", WorkerBinding.DurableObject("O", "Class").ClassName);
        Assert.Equal(WorkerBindingTypes.Queue, WorkerBinding.Queue("Q", "queue").Type);
        Assert.Equal("queue", WorkerBinding.Queue("Q", "queue").QueueName);
        Assert.Equal(WorkerBindingTypes.AnalyticsEngine, WorkerBinding.AnalyticsEngine("AE", "ds").Type);
        Assert.Equal("ASSETS", WorkerBinding.Assets().Name);
        Assert.Equal("AI", WorkerBinding.Ai().Name);
    }

    [Fact]
    public void Worker_binding_type_constants_match_the_api_literals()
    {
        Assert.Equal("kv_namespace", WorkerBindingTypes.KvNamespace);
        Assert.Equal("r2_bucket", WorkerBindingTypes.R2Bucket);
        Assert.Equal("plain_text", WorkerBindingTypes.PlainText);
        Assert.Equal("secret_text", WorkerBindingTypes.SecretText);
        Assert.Equal("service", WorkerBindingTypes.Service);
        Assert.Equal("do_namespace", WorkerBindingTypes.DoNamespace);
        Assert.Equal("d1_database_binding", WorkerBindingTypes.D1DatabaseBinding);
        Assert.Equal("queue", WorkerBindingTypes.Queue);
        Assert.Equal("analytics_engine", WorkerBindingTypes.AnalyticsEngine);
        Assert.Equal("assets", WorkerBindingTypes.Assets);
    }

    [Fact]
    public void Module_content_type_constants_match_the_api_literals()
    {
        Assert.Equal("application/javascript+module", WorkerModuleContentTypes.JavaScriptModule);
        Assert.Equal("text/x-python", WorkerModuleContentTypes.Python);
        Assert.Equal("application/wasm", WorkerModuleContentTypes.Wasm);
        Assert.Equal("buffer", WorkerModuleContentTypes.Buffer);
        Assert.Equal("base64", WorkerModuleContentTypes.Base64);
        Assert.Equal("text", WorkerModuleContentTypes.Text);
    }

    [Fact]
    public async Task Worker_calls_need_an_account_id()
    {
        using var harness = CloudflareHarness.Create(options => options.AccountId = null);

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(() => harness.Client.Workers.GetAsync("hello"));

        Assert.Contains("CLOUDFLARE_ACCOUNT_ID", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }
}

public class PagesProjectsServiceTests
{
    [Fact]
    public async Task ListAsync_scopes_the_call_to_the_account()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage("[{\"id\":\"p-1\",\"name\":\"site\"}]", page: 1, totalPages: 1, count: 1, totalCount: 1);

        var page = await harness.Client.Pages.ListAsync(new PagesProjectListParameters { Page = 1, PerPage = 10, Search = "site" });

        Assert.Equal($"/client/v4/accounts/{CloudflareHarness.TestAccountId}/pages/projects", harness.LastRequest.Path);
        Assert.Equal("page=1&per_page=10&search=site", harness.LastRequest.Query.TrimStart('?'));
        Assert.Equal("site", Assert.Single(page.Items).Name);
    }

    [Fact]
    public async Task ListAsync_pairs_a_page_size_with_a_page_number()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage("[]", page: 1, totalPages: 1, count: 0, totalCount: 0);

        await harness.Client.Pages.ListAsync(new PagesProjectListParameters { PerPage = 20 });

        // Pages answers a page size sent on its own with 8000024 "Invalid list options provided. Review
        // the page or per_page parameter", so the two always travel together.
        Assert.Equal("page=1&per_page=20", harness.LastRequest.Query.TrimStart('?'));
    }

    [Fact]
    public async Task ListAsync_keeps_an_explicit_page_number()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage("[]", page: 3, totalPages: 5, count: 0, totalCount: 0);

        await harness.Client.Pages.ListAsync(new PagesProjectListParameters { Page = 3, PerPage = 20 });

        Assert.Equal("page=3&per_page=20", harness.LastRequest.Query.TrimStart('?'));
    }

    [Fact]
    public async Task ListAsync_retries_without_paging_when_pages_rejects_the_list_options()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(
            HttpStatusCode.BadRequest,
            8000024,
            "Invalid list options provided. Review the `page` or `per_page` parameter.");
        harness.RespondWithPage("[{\"id\":\"p-1\",\"name\":\"site\"}]", page: 1, totalPages: 1, count: 1, totalCount: 1);

        var page = await harness.Client.Pages.ListAsync(new PagesProjectListParameters { PerPage = 20 });

        Assert.Equal(2, harness.RequestCount);
        Assert.Equal("?page=1&per_page=20", harness.Requests[0].Query);
        Assert.Equal(string.Empty, harness.Requests[1].Query);
        Assert.Equal("site", Assert.Single(page.Items).Name);
    }

    [Fact]
    public async Task ListAsync_does_not_retry_a_refusal_that_is_not_about_list_options()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.Forbidden, 10000, "Authentication error");

        await Assert.ThrowsAsync<CloudflareApiException>(
            () => harness.Client.Pages.ListAsync(new PagesProjectListParameters { PerPage = 20 }));

        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task ListAsync_sends_no_query_at_all_when_nothing_was_asked_for()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage("[]", page: 1, totalPages: 1, count: 0, totalCount: 0);

        await harness.Client.Pages.ListAsync();

        Assert.Equal(string.Empty, harness.LastRequest.Query);
    }

    [Fact]
    public async Task GetAsync_returns_null_for_an_unknown_project()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.NotFound, 10007, "Route not found");

        var project = await harness.Client.Pages.GetAsync("missing");

        Assert.Null(project);
        Assert.Equal($"/client/v4/accounts/{CloudflareHarness.TestAccountId}/pages/projects/missing", harness.LastRequest.Path);
    }

    [Fact]
    public async Task GetAsync_parses_a_project_with_its_bindings()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""
        {
          "id": "p-1",
          "name": "site",
          "account_id": "acct",
          "subdomain": "site.pages.dev",
          "domains": [ "site.pages.dev", "www.a.test" ],
          "production_branch": "main",
          "source": {
            "type": "github",
            "url": "https://github.com/example/site",
            "config": {
              "owner": "example",
              "repo_name": "site",
              "production_branch": "main",
              "pr_comments_enabled": true,
              "deployments_enabled": true,
              "preview_branch_excludes": [ "main" ],
              "preview_branch_includes": [ "*" ],
              "preview_deployment_setting": "all",
              "production_deployment_setting": "all"
            }
          },
          "build_config": {
            "build_command": "npm run build",
            "destination_dir": "dist",
            "root_dir": "/",
            "build_caching": true
          },
          "deployment_configs": {
            "preview": {
              "env_vars": { "NODE_VERSION": { "type": "plain_text", "value": "20" } },
              "compatibility_date": "2025-01-01",
              "kv_namespaces": { "CACHE": { "namespace_id": "ns-1" } },
              "d1_databases": { "DB": { "database_id": "db-1", "migrations_dir": "migrations" } },
              "r2_buckets": { "MEDIA": { "bucket_name": "media", "jurisdiction": "default" } },
              "services": { "API": { "service": "api-worker", "environment": "production" } },
              "durable_object_namespaces": { "SESSIONS": { "class_name": "Session", "script_name": "site" } },
              "queue_producers": { "JOBS": { "queue_name": "jobs" } },
              "queue_consumers": { "EVENTS": { "queue_name": "events", "max_batch_size": 10, "max_retries": 3, "dead_letter_queue": "dlq" } }
            },
            "production": { "env_vars": { "NODE_VERSION": { "type": "plain_text", "value": "20" } } }
          },
          "created_on": "2024-01-01T00:00:00Z",
          "modified_on": "2024-02-01T00:00:00Z",
          "latest_deployment": { "id": "d-1", "environment": "preview", "latest_stage": { "name": "deploy", "status": "success" } }
        }
        """);

        var project = await harness.Client.Pages.GetAsync("site");

        Assert.NotNull(project);
        Assert.Equal("p-1", project!.Id);
        Assert.Equal("site", project.Name);
        Assert.Equal("site.pages.dev", project.Subdomain);
        Assert.Equal(2, project.Domains!.Count);
        Assert.Equal("main", project.ProductionBranch);
        Assert.Equal("github", project.Source!.Type);
        Assert.Equal("example", project.Source.Config!.Owner);
        Assert.Equal("npm run build", project.BuildConfig!.BuildCommand);
        Assert.Equal("dist", project.BuildConfig.DestinationDir);
        Assert.True(project.BuildConfig.BuildCaching);

        var preview = project.DeploymentConfigs!.Preview!;
        Assert.Equal("2025-01-01", preview.CompatibilityDate);
        Assert.Equal("plain_text", preview.EnvVars!["NODE_VERSION"].Type);
        Assert.Equal("20", preview.EnvVars["NODE_VERSION"].ValueAsString);

        // Pages bindings are dictionaries keyed by binding name, unlike Workers' array.
        Assert.Equal("ns-1", preview.KvNamespaces!["CACHE"].NamespaceId);
        Assert.Equal("db-1", preview.D1Databases!["DB"].DatabaseId);
        Assert.Equal("migrations", preview.D1Databases["DB"].MigrationsDir);
        Assert.Equal("media", preview.R2Buckets!["MEDIA"].BucketName);
        Assert.Equal("api-worker", preview.Services!["API"].Service);
        Assert.Equal("Session", preview.DurableObjectNamespaces!["SESSIONS"].ClassName);
        Assert.Equal("jobs", preview.QueueProducers!["JOBS"].QueueName);
        Assert.Equal(10, preview.QueueConsumers!["EVENTS"].MaxBatchSize);
        Assert.Equal("dlq", preview.QueueConsumers["EVENTS"].DeadLetterQueue);

        Assert.NotNull(project.LatestDeployment);
        Assert.Equal(PagesDeploymentStatusValues.Success, project.LatestDeployment!.Status);
    }

    [Fact]
    public async Task CreateAsync_validates_the_project_name_locally()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Pages.CreateAsync(new CreatePagesProjectRequest("Not Valid")));

        Assert.Contains(exception.Problems, problem => problem.Contains("not a valid Pages project name", StringComparison.Ordinal));
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task CreateAsync_requires_a_production_branch()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Pages.CreateAsync(new CreatePagesProjectRequest { Name = "site" }));

        Assert.Contains(exception.Problems, problem => problem.Contains("ProductionBranch", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateAsync_sends_the_name_branch_and_build_config()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"p-1\",\"name\":\"site\"}");

        var project = await harness.Client.Pages.CreateAsync(new CreatePagesProjectRequest("site", "main")
        {
            BuildConfig = new PagesBuildConfig { BuildCommand = "npm run build", DestinationDir = "dist" },
        });

        Assert.Equal("p-1", project.Id);
        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.Contains("\"name\":\"site\"", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"production_branch\":\"main\"", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"build_command\":\"npm run build\"", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"destination_dir\":\"dist\"", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_defaults_the_production_branch_to_main()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"p-1\"}");

        await harness.Client.Pages.CreateAsync("site");

        Assert.Contains("\"production_branch\":\"main\"", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("site", true)]
    [InlineData("my-site-2", true)]
    [InlineData("9lives", true)]
    [InlineData("Site", false)]
    [InlineData("-site", false)]
    [InlineData("my_site", false)]
    [InlineData("my.site", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void Project_names_follow_the_documented_rules(string name, bool valid)
    {
        Assert.Equal(valid, CreatePagesProjectRequest.IsValidProjectName(name));
    }

    [Fact]
    public void A_project_name_longer_than_58_characters_is_rejected()
    {
        Assert.True(CreatePagesProjectRequest.IsValidProjectName(new string('a', 58)));
        Assert.False(CreatePagesProjectRequest.IsValidProjectName(new string('a', 59)));
    }

    [Fact]
    public async Task UpdateAsync_patches_the_project()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"p-1\",\"name\":\"site\",\"production_branch\":\"develop\"}");

        var project = await harness.Client.Pages.UpdateAsync("site", new UpdatePagesProjectRequest { ProductionBranch = "develop" });

        Assert.Equal("develop", project.ProductionBranch);
        Assert.Equal(HttpMethod.Patch, harness.LastRequest.Method);
        Assert.EndsWith("/pages/projects/site", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal("{\"production_branch\":\"develop\"}", harness.LastRequest.Body);
    }

    [Fact]
    public async Task DeleteAsync_removes_the_project()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("null");

        await harness.Client.Pages.DeleteAsync("site");

        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.EndsWith("/pages/projects/site", harness.LastRequest.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListDeploymentsAsync_filters_by_branch()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage("[{\"id\":\"d-1\"}]", page: 1, totalPages: 1, count: 1, totalCount: 1);

        await harness.Client.Pages.ListDeploymentsAsync("site", new PagesDeploymentListParameters
        {
            Branch = "main",
            Order = "created_on",
            Direction = SortDirection.Descending,
            Page = 2,
            PerPage = 25,
        });

        Assert.EndsWith("/pages/projects/site/deployments", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal("page=2&per_page=25&branch=main&order=created_on&direction=desc", harness.LastRequest.Query.TrimStart('?'));
    }

    [Fact]
    public async Task GetDeploymentAsync_returns_null_when_it_is_gone()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.NotFound, 10007, "Route not found");

        var deployment = await harness.Client.Pages.GetDeploymentAsync("site", "d-missing");

        Assert.Null(deployment);
        Assert.EndsWith("/pages/projects/site/deployments/d-missing", harness.LastRequest.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateDeploymentAsync_posts_a_request_even_without_a_body()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"d-1\",\"environment\":\"preview\"}");

        var deployment = await harness.Client.Pages.CreateDeploymentAsync("site");

        Assert.Equal("d-1", deployment.Id);
        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.Equal("{}", harness.LastRequest.Body);
    }

    [Fact]
    public async Task CreateDeploymentAsync_sends_the_branch_when_supplied()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"d-1\"}");

        await harness.Client.Pages.CreateDeploymentAsync("site", new CreatePagesDeploymentRequest { Branch = "feature" });

        Assert.Contains("\"branch\":\"feature\"", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteDeploymentAsync_targets_the_deployment()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("null");

        await harness.Client.Pages.DeleteDeploymentAsync("site", "d-1");

        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.EndsWith("/pages/projects/site/deployments/d-1", harness.LastRequest.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Domains_are_managed_under_the_project()
    {
        using var harness = CloudflareHarness.Create();

        harness.RespondWithPage("[{\"name\":\"www.a.test\",\"status\":\"active\"}]", page: 1, totalPages: 1, count: 1, totalCount: 1);
        var domains = await harness.Client.Pages.ListDomainsAsync("site");
        Assert.EndsWith("/pages/projects/site/domains", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal("active", Assert.Single(domains.Items).Status);

        harness.RespondWithResult("{\"name\":\"www.a.test\",\"status\":\"verification_required\"}");
        var added = await harness.Client.Pages.AddDomainAsync("site", "www.a.test");
        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.Equal("{\"name\":\"www.a.test\"}", harness.LastRequest.Body);
        Assert.Equal("verification_required", added.Status);

        harness.RespondWithResult("{\"name\":\"www.a.test\",\"status\":\"active\"}");
        await harness.Client.Pages.UpdateDomainAsync("site", "www.a.test", new UpdatePagesDomainRequest { Verified = true });
        Assert.Equal(HttpMethod.Patch, harness.LastRequest.Method);
        Assert.EndsWith("/pages/projects/site/domains/www.a.test", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal("{\"verified\":true}", harness.LastRequest.Body);

        harness.RespondWithResult("null");
        await harness.Client.Pages.DeleteDomainAsync("site", "www.a.test");
        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
    }

    [Fact]
    public async Task AddDomainAsync_rejects_a_blank_domain()
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<CloudflareConfigurationException>(() => harness.Client.Pages.AddDomainAsync("site", "  "));
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public void Pages_environment_variable_factories_set_the_right_type()
    {
        Assert.Equal("plain_text", PagesEnvVar.PlainText("v").Type);
        Assert.Equal("v", PagesEnvVar.PlainText("v").Value);
        Assert.Equal("secret_text", PagesEnvVar.SecretText("v").Type);
    }

    [Fact]
    public void A_deployment_status_is_derived_from_its_latest_stage()
    {
        Assert.Equal(
            PagesDeploymentStatusValues.InProgress,
            new PagesDeployment { LatestStage = new PagesDeploymentStage { Status = "in_progress" } }.Status);

        Assert.Equal(
            PagesDeploymentStatusValues.Unknown,
            new PagesDeployment().Status);

        Assert.Equal(
            "d-1 [preview] success",
            new PagesDeployment
            {
                Id = "d-1",
                Environment = "preview",
                LatestStage = new PagesDeploymentStage { Status = PagesDeploymentStatusValues.Success },
            }.ToString());
    }

    [Fact]
    public void Pages_deployment_status_literals_match_the_api()
    {
        Assert.Equal("queued", PagesDeploymentStatusValues.Queued);
        Assert.Equal("in_progress", PagesDeploymentStatusValues.InProgress);
        Assert.Equal("success", PagesDeploymentStatusValues.Success);
        Assert.Equal("failure", PagesDeploymentStatusValues.Failure);
        Assert.Equal("canceled", PagesDeploymentStatusValues.Canceled);
        Assert.Equal("skipped", PagesDeploymentStatusValues.Skipped);
        Assert.Equal("pending", PagesDeploymentStatusValues.Pending);
        Assert.Equal("none", PagesDeploymentStatusValues.None);
        Assert.Equal("unknown", PagesDeploymentStatusValues.Unknown);
    }
}


public class WorkerDomainsTests
{
    [Fact]
    public async Task ListDomainsAsync_reads_the_account_level_collection()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""
        [
          {
            "id": "d-1",
            "cert_id": "c-1",
            "enabled": true,
            "previews_enabled": false,
            "hostname": "app.example.com",
            "service": "my-worker",
            "zone_id": "zone-1",
            "zone_name": "example.com",
            "environment": "production"
          }
        ]
        """);

        var domains = await harness.Client.Workers.ListDomainsAsync();

        Assert.Equal($"/client/v4/accounts/{CloudflareHarness.TestAccountId}/workers/domains", harness.LastRequest.Path);
        // The published schema documents no query parameters for this endpoint, so none are sent.
        Assert.Equal(string.Empty, harness.LastRequest.Query);

        var domain = Assert.Single(domains);
        Assert.Equal("d-1", domain.Id);
        Assert.Equal("app.example.com", domain.Hostname);
        Assert.Equal("my-worker", domain.Service);
        Assert.Equal("zone-1", domain.ZoneId);
        Assert.Equal("example.com", domain.ZoneName);
        Assert.Equal("c-1", domain.CertId);
        Assert.True(domain.Enabled);
        Assert.False(domain.PreviewsEnabled);
        Assert.Equal("production", domain.Environment);
    }

    [Fact]
    public async Task GetDomainAsync_scopes_the_call_to_one_domain_and_tolerates_404()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"id":"d-1","hostname":"app.example.com","service":"my-worker"}""");

        var domain = await harness.Client.Workers.GetDomainAsync("d-1");

        Assert.Equal($"/client/v4/accounts/{CloudflareHarness.TestAccountId}/workers/domains/d-1", harness.LastRequest.Path);
        Assert.Equal("app.example.com", domain!.Hostname);

        harness.RespondWithError(HttpStatusCode.NotFound, 10007, "Domain not found");
        Assert.Null(await harness.Client.Workers.GetDomainAsync("d-missing"));
    }

    [Fact]
    public async Task AttachDomainAsync_puts_the_hostname_service_and_zone_to_the_collection()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"id":"d-2","hostname":"app.example.com","service":"my-worker","enabled":true}""");

        var domain = await harness.Client.Workers.AttachDomainAsync(
            AttachWorkerDomainRequest.For("app.example.com", "my-worker", "zone-1"));

        Assert.Equal(HttpMethod.Put, harness.LastRequest.Method);
        Assert.Equal($"/client/v4/accounts/{CloudflareHarness.TestAccountId}/workers/domains", harness.LastRequest.Path);
        Assert.Equal("{\"hostname\":\"app.example.com\",\"service\":\"my-worker\",\"zone_id\":\"zone-1\"}", harness.LastRequest.Body);
        Assert.Equal("d-2", domain.Id);
    }

    [Fact]
    public async Task AttachDomainAsync_falls_back_to_the_configured_zone_id()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"id":"d-3","hostname":"app.example.com"}""");

        await harness.Client.Workers.AttachDomainAsync("app.example.com", "my-worker");

        Assert.Contains($"\"zone_id\":\"{CloudflareHarness.TestZoneId}\"", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AttachDomainAsync_can_send_the_preview_routing_flags()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"id":"d-4","hostname":"app.example.com"}""");

        await harness.Client.Workers.AttachDomainAsync(new AttachWorkerDomainRequest
        {
            Hostname = "app.example.com",
            Service = "my-worker",
            Enabled = false,
            PreviewsEnabled = true,
        });

        Assert.Equal(
            "{\"hostname\":\"app.example.com\",\"service\":\"my-worker\",\"enabled\":false,\"previews_enabled\":true}",
            harness.LastRequest.Body);
    }

    [Theory]
    [InlineData(null, "my-worker", null, null, "Hostname")]
    [InlineData("app.example.com", null, null, null, "Service")]
    [InlineData("app.example.com", "my-worker", false, null, "preview routing")]
    [InlineData("app.example.com", "my-worker", null, false, "hostname routing")]
    public void AttachWorkerDomainRequest_rejects_what_cloudflare_would_refuse(
        string? hostname,
        string? service,
        bool? enabled,
        bool? previewsEnabled,
        string expectedInMessage)
    {
        var request = new AttachWorkerDomainRequest
        {
            Hostname = hostname,
            Service = service,
            Enabled = enabled,
            PreviewsEnabled = previewsEnabled,
        };

        var exception = Assert.Throws<CloudflareConfigurationException>(request.EnsureValid);

        Assert.Contains(expectedInMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteDomainAsync_detaches_by_id()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("null");

        await harness.Client.Workers.DeleteDomainAsync("d-1");

        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.Equal($"/client/v4/accounts/{CloudflareHarness.TestAccountId}/workers/domains/d-1", harness.LastRequest.Path);
    }

    [Fact]
    public async Task Domain_calls_accept_an_explicit_account()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");

        await harness.Client.Workers.ListDomainsAsync("acct-other");

        Assert.Equal("/client/v4/accounts/acct-other/workers/domains", harness.LastRequest.Path);
    }
}

public class PagesDeploymentRollbackTests
{
    [Fact]
    public async Task RollbackDeploymentAsync_posts_to_the_deployments_rollback_path()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"id":"d-1","latest_stage":{"status":"success"},"environment":"production"}""");

        var deployment = await harness.Client.Pages.RollbackDeploymentAsync("site", "d-1");

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.Equal(
            $"/client/v4/accounts/{CloudflareHarness.TestAccountId}/pages/projects/site/deployments/d-1/rollback",
            harness.LastRequest.Path);
        Assert.Equal("d-1", deployment.Id);
    }

    [Fact]
    public async Task RollbackDeploymentAsync_escapes_both_path_segments()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"id":"d/2"}""");

        await harness.Client.Pages.RollbackDeploymentAsync("a/b", "d/2");

        Assert.Contains("/pages/projects/a%2Fb/deployments/d%2F2/rollback", harness.LastRequest.Uri!.OriginalString, StringComparison.Ordinal);
    }
}


public class WorkerDomainFilterTests
{
    private static string DomainsOfTwoWorkers() =>
        """
        [
          {"id":"d-1","hostname":"app.example.com","service":"my-worker","enabled":true,"zone_name":"example.com"},
          {"id":"d-2","hostname":"beta.example.com","service":"other-worker","enabled":true,"zone_name":"example.com"},
          {"id":"d-3","hostname":"api.example.com","service":"My-Worker","enabled":false,"zone_name":"example.com"}
        ]
        """;

    [Fact]
    public async Task ListDomainsForWorkerAsync_keeps_only_the_attachments_of_that_worker()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(DomainsOfTwoWorkers());

        var domains = await harness.Client.Workers.ListDomainsForWorkerAsync("my-worker");

        // The v4 API only exposes the account-level collection, so the filter runs on one list call.
        Assert.Equal(1, harness.RequestCount);
        Assert.Equal(
            $"/client/v4/accounts/{CloudflareHarness.TestAccountId}/workers/domains",
            harness.LastRequest.Path);

        Assert.Equal(2, domains.Count);
        Assert.Equal(new[] { "d-1", "d-3" }, domains.Select(domain => domain.Id).ToArray());
        Assert.Equal("api.example.com", domains[1].Hostname);
    }

    [Fact]
    public async Task ListDomainsForWorkerAsync_returns_nothing_for_a_worker_without_domains()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(DomainsOfTwoWorkers());

        var domains = await harness.Client.Workers.ListDomainsForWorkerAsync("missing-worker");

        Assert.Empty(domains);
        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task ListDomainsForWorkerAsync_passes_the_account_id_through_to_the_list_call()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(DomainsOfTwoWorkers());

        var domains = await harness.Client.Workers.ListDomainsForWorkerAsync("other-worker", "acct-9999");

        Assert.Contains("/accounts/acct-9999/workers/domains", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal("d-2", Assert.Single(domains).Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ListDomainsForWorkerAsync_needs_a_worker_name(string? workerName)
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Workers.ListDomainsForWorkerAsync(workerName!));

        Assert.Equal(0, harness.RequestCount);
    }
}

public class PagesDirectUploadAndLogsTests
{
    private static string ProjectPath(string suffix) =>
        $"/client/v4/accounts/{CloudflareHarness.TestAccountId}/pages/projects/site{suffix}";

    [Fact]
    public async Task PurgeBuildCacheAsync_posts_with_no_body()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("null");

        await harness.Client.Pages.PurgeBuildCacheAsync("site");

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.Equal(ProjectPath("/purge_build_cache"), harness.LastRequest.Path);
        Assert.True(string.IsNullOrEmpty(harness.LastRequest.Body));
    }

    [Fact]
    public async Task PurgeBuildCacheAsync_surfaces_a_rejection()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.Forbidden, 10000, "Not allowed to edit this resource");

        var exception = await Assert.ThrowsAsync<CloudflareApiException>(
            () => harness.Client.Pages.PurgeBuildCacheAsync("site"));

        Assert.Equal(10000, exception.ErrorCode);
    }

    [Fact]
    public async Task GetUploadTokenAsync_reads_the_jwt_without_ever_printing_it()
    {
        using var harness = CloudflareHarness.Create();
        const string jwt = "eyJhbGciOiJIUzI1NiJ9.pages-upload-token";
        harness.RespondWithResult($"{{\"jwt\":\"{jwt}\"}}");

        var token = await harness.Client.Pages.GetUploadTokenAsync("site");

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Equal(ProjectPath("/upload-token"), harness.LastRequest.Path);
        Assert.Equal(jwt, token.Jwt);
        Assert.True(token.HasToken);
        // A credential must not turn up in a log line, a watch window or an exception message.
        Assert.Equal($"jwt, {jwt.Length} characters", token.ToString());
        Assert.DoesNotContain(jwt, token.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUploadTokenAsync_without_a_result_payload_raises()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("null");

        var exception = await Assert.ThrowsAsync<CloudflareApiException>(
            () => harness.Client.Pages.GetUploadTokenAsync("site"));

        Assert.Contains("without a result payload", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetryDeploymentAsync_posts_to_the_retry_path_and_returns_the_deployment()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"id":"d-1","latest_stage":{"name":"build","status":"active"}}""");

        var deployment = await harness.Client.Pages.RetryDeploymentAsync("site", "d-1");

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.Equal(ProjectPath("/deployments/d-1/retry"), harness.LastRequest.Path);
        Assert.True(string.IsNullOrEmpty(harness.LastRequest.Body));
        Assert.Equal("d-1", deployment.Id);
        Assert.Equal("build", deployment.LatestStage!.Name);
        Assert.Equal(PagesDeploymentStatusValues.Active, deployment.LatestStage.Status);
    }

    [Fact]
    public async Task RetryDeploymentAsync_escapes_the_path_segments_and_requires_an_id()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"id":"d/2"}""");

        await harness.Client.Pages.RetryDeploymentAsync("a/b", "d/2");
        Assert.Contains("/pages/projects/a%2Fb/deployments/d%2F2/retry", harness.LastRequest.Uri!.OriginalString, StringComparison.Ordinal);

        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Pages.RetryDeploymentAsync("site", "  "));
    }

    [Fact]
    public async Task GetDeploymentLogsAsync_reads_the_lines_the_container_flag_and_the_total()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """
            {
              "data": [
                { "line": "Installing dependencies", "ts": "2026-05-05T13:00:15Z" },
                { "line": "Build failed: missing script", "ts": "2026-05-05T13:00:41Z" }
              ],
              "includes_container_logs": true,
              "total": 42
            }
            """);

        var logs = await harness.Client.Pages.GetDeploymentLogsAsync("site", "d-1");

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Equal(ProjectPath("/deployments/d-1/history/logs"), harness.LastRequest.Path);
        Assert.Equal(2, logs.Data!.Count);
        Assert.Equal("Installing dependencies", logs.Data[0].Line);
        // Cloudflare types "ts" as a string, so it stays one rather than being parsed on a guess.
        Assert.Equal("2026-05-05T13:00:15Z", logs.Data[0].Timestamp);
        Assert.True(logs.IncludesContainerLogs);
        Assert.Equal(42, logs.Total);
        Assert.Equal("2 of 42 log lines", logs.ToString());

        // A truncated log is normal: fewer lines returned than Cloudflare holds in total.
        Assert.Contains("Installing dependencies", logs.Text, StringComparison.Ordinal);
        Assert.Contains("Build failed: missing script", logs.Text, StringComparison.Ordinal);
        Assert.EndsWith("\n", logs.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetDeploymentLogsAsync_reads_a_deployment_with_no_logs()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"data":[],"includes_container_logs":false,"total":0}""");

        var logs = await harness.Client.Pages.GetDeploymentLogsAsync("site", "d-1");

        Assert.Empty(logs.Data!);
        Assert.Equal(string.Empty, logs.Text);
        Assert.False(logs.IncludesContainerLogs);
        Assert.Equal("0 of 0 log lines", logs.ToString());
    }

    [Fact]
    public async Task Deployment_logs_keep_fields_this_release_does_not_name()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """
            {
              "data": [{ "line": "hi", "ts": "now", "level": "info" }],
              "total": 1,
              "truncated": true
            }
            """);

        var logs = await harness.Client.Pages.GetDeploymentLogsAsync("site", "d-1");

        Assert.True(logs.AdditionalData!["truncated"].GetBoolean());
        Assert.Equal("info", logs.Data![0].AdditionalData!["level"].GetString());
        Assert.Equal("hi", logs.Data[0].ToString());
    }

    [Fact]
    public async Task GetDeploymentLogsAsync_without_a_result_payload_raises()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("null");

        await Assert.ThrowsAsync<CloudflareApiException>(
            () => harness.Client.Pages.GetDeploymentLogsAsync("site", "d-1"));
    }

    [Fact]
    public async Task GetDomainAsync_reads_one_domain_and_returns_null_when_it_is_not_attached()
    {
        using var harness = CloudflareHarness.Create();
        harness
            .RespondWithResult("""{"name":"shop.example.com","project_name":"site","status":"active"}""")
            .Respond(HttpStatusCode.NotFound, "{\"success\":false,\"errors\":[],\"result\":null}");

        var domain = await harness.Client.Pages.GetDomainAsync("site", "shop.example.com");
        Assert.Equal(ProjectPath("/domains/shop.example.com"), harness.LastRequest.Path);
        Assert.Equal("shop.example.com", domain!.Name);
        Assert.Equal("active", domain.Status);

        Assert.Null(await harness.Client.Pages.GetDomainAsync("site", "missing.example.com"));
    }

    [Fact]
    public async Task ListDeploymentsAsync_sends_the_documented_env_filter()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage("[]", page: 1, totalPages: 1, count: 0, totalCount: 0);

        await harness.Client.Pages.ListDeploymentsAsync(
            "site",
            new PagesDeploymentListParameters { Env = PagesDeploymentEnvironments.Preview, Page = 2, PerPage = 20 });

        Assert.Equal(ProjectPath("/deployments"), harness.LastRequest.Path);
        Assert.Equal("page=2&per_page=20&env=preview", harness.LastRequest.Query.TrimStart('?'));
    }

    [Fact]
    public async Task ListDeploymentsAsync_sends_no_query_at_all_when_nothing_is_set()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage("[]", page: 1, totalPages: 1, count: 0, totalCount: 0);

        await harness.Client.Pages.ListDeploymentsAsync("site");

        Assert.Equal(string.Empty, harness.LastRequest.Query);
        Assert.Equal(new[] { "production", "preview" }, PagesDeploymentEnvironments.All);
    }
}
