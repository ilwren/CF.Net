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
            "[{\"id\":\"s-1\",\"script\":\"hello\"},{\"id\":\"s-2\",\"script\":\"world\"}]",
            page: 1, totalPages: 1, count: 2, totalCount: 2);

        var page = await harness.Client.Workers.ListAsync();

        Assert.Equal($"/client/v4/accounts/{CloudflareHarness.TestAccountId}/workers/scripts", harness.LastRequest.Path);
        Assert.Equal(2, page.Count);
        Assert.Equal("hello", page.Items[0].ScriptName);
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
         "result":[{"id":"s-1","script":"a"}],
         "result_info":{"cursors":{"before":"","after":"c-2"}}}
        """);
        harness.Respond("""
        {"success":true,"errors":[],"messages":[],
         "result":[{"id":"s-2","script":"b"}],
         "result_info":{"cursors":{"before":"c-1","after":"c-2"}}}
        """);

        var scripts = await harness.Client.Workers.ListAllAsync().CollectAsync();

        Assert.Equal(new[] { "a", "b" }, scripts.Select(script => script.ScriptName));
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
         "result":[{"id":"s-1","script":"a"}],
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
          "id": "5c8a1e20",
          "script": "hello",
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
        Assert.Equal("hello", script!.ScriptName);
        Assert.Equal("5c8a1e20", script.Id);
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
        harness.RespondWithResult("{\"id\":\"s-1\",\"script\":\"hello\"}");

        var script = await harness.Client.Workers.UploadAsync(new UploadWorkerScriptRequest(
            "hello",
            "export default { async fetch(request, env) { return new Response('hi'); } }",
            "2025-01-01"));

        Assert.Equal("hello", script.ScriptName);
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
        harness.RespondWithResult("{\"script\":\"hello\"}");

        await harness.Client.Workers.UploadAsync(new UploadWorkerScriptRequest("hello", "export default{}", "2025-01-01")
        {
            Bindings = new List<WorkerBinding>
            {
                WorkerBinding.KvNamespace("MY_KV", "ns-id"),
                WorkerBinding.D1Database("DB", "db-id"),
                WorkerBinding.R2Bucket("BUCKET", "bucket-name"),
                WorkerBinding.PlainText("ENV", "production"),
                WorkerBinding.SecretText("SECRET", "s3cret"),
                WorkerBinding.Service("UPSTREAM", "other-worker"),
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
        harness.RespondWithResult("{\"script\":\"hello\"}");

        await harness.Client.Workers.UploadAsync(new UploadWorkerScriptRequest
        {
            ScriptName = "hello",
            MainModule = "worker.mjs",
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
        harness.RespondWithResult("{\"script\":\"py\"}");

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

        Assert.Equal("hello", script.ScriptName);
        Assert.Equal("hello", script.Id);
    }

    [Fact]
    public async Task UploadAsync_uses_todays_date_when_no_compatibility_date_is_supplied()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"script\":\"hello\"}");

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
        Assert.Equal(WorkerBindingTypes.Service, WorkerBinding.Service("U", "svc").Type);
        Assert.Equal("svc", WorkerBinding.Service("U", "svc").Service);
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
        Assert.Equal("20", preview.EnvVars["NODE_VERSION"].Value);

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
