using System.Net;
using CFNet.Configuration;
using CFNet.DependencyInjection;
using CFNet.Exceptions;
using CFNet.Http;
using CFNet.Services;
using CFNet.UnitTests.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CFNet.UnitTests.DependencyInjection;

public class UserServiceTests
{
    [Fact]
    public async Task VerifyTokenAsync_calls_the_verify_endpoint()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"t-1\",\"status\":\"active\"}");

        var verification = await harness.Client.User.VerifyTokenAsync();

        Assert.Equal("/client/v4/user/tokens/verify", harness.LastRequest.Path);
        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Equal("t-1", verification.Id);
        Assert.Equal("active", verification.Status);
        Assert.True(verification.IsActive);
    }

    [Fact]
    public async Task GetAsync_reads_the_user_object()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"u-1\",\"email\":\"owner@example.test\",\"first_name\":\"Ada\",\"last_name\":\"Lovelace\",\"country\":\"GB\",\"two_factor_authentication_enabled\":false}");

        var user = await harness.Client.User.GetAsync();

        Assert.Equal("/client/v4/user", harness.LastRequest.Path);
        Assert.Equal("u-1", user.Id);
        Assert.Equal("owner@example.test", user.Email);
        Assert.Equal("Ada", user.FirstName);
        Assert.Equal("Lovelace", user.LastName);
        Assert.Equal("GB", user.Country);

        // Fields without a typed member are still reachable.
        Assert.NotNull(user.AdditionalData);
        Assert.True(user.AdditionalData!.ContainsKey("two_factor_authentication_enabled"));
    }

    [Fact]
    public async Task CheckConnectionAsync_verifies_a_token_without_throwing_on_failure()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"t-1\",\"status\":\"active\"}");

        var check = await harness.Client.CheckConnectionAsync();

        Assert.True(check.IsSuccess);
        Assert.Equal(CloudflareAuthenticationMode.ApiToken, check.Method);
        Assert.Contains("active", check.Message, StringComparison.Ordinal);
        Assert.True(check.Elapsed >= TimeSpan.Zero);
    }

    [Fact]
    public async Task CheckConnectionAsync_reports_an_inactive_token()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"t-1\",\"status\":\"disabled\"}");

        var check = await harness.Client.CheckConnectionAsync();

        Assert.False(check.IsSuccess);
        Assert.Contains("disabled", check.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckConnectionAsync_falls_back_to_the_user_endpoint_for_a_global_key()
    {
        using var harness = CloudflareHarness.CreateWithGlobalKey();
        harness.RespondWithResult("{\"id\":\"u-1\",\"email\":\"owner@example.test\"}");

        var check = await harness.Client.CheckConnectionAsync();

        Assert.Equal("/client/v4/user", harness.LastRequest.Path);
        Assert.True(check.IsSuccess);
        Assert.Equal(CloudflareAuthenticationMode.GlobalApiKey, check.Method);
        Assert.Equal("owner@example.test", check.AccountEmail);
        Assert.Contains("owner@example.test", check.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckConnectionAsync_reports_an_api_failure_with_its_status_and_code()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.Unauthorized, 10000, "Invalid Authentication");
        // A user-scoped rejection is also retried against the account endpoint, which refuses too; the
        // original rejection is the one that gets reported.
        harness.RespondWithError(HttpStatusCode.Forbidden, 1000, "Invalid API Token");

        var check = await harness.Client.CheckConnectionAsync();

        Assert.False(check.IsSuccess);
        Assert.Equal(HttpStatusCode.Unauthorized, check.StatusCode);
        Assert.Equal(10000, check.ErrorCode);
        Assert.Contains("Invalid Authentication", check.Message, StringComparison.Ordinal);
        Assert.Equal(2, harness.RequestCount);
    }

    [Fact]
    public async Task CheckConnectionAsync_reports_missing_credentials_without_sending_anything()
    {
        using var harness = CloudflareHarness.Create(options =>
        {
            options.ApiToken = "placeholder";
        });

        // The transport validates on send, so drop the credentials after construction.
        harness.Options.ApiToken = null;

        var check = await harness.Client.CheckConnectionAsync();

        Assert.False(check.IsSuccess);
        Assert.Equal(CloudflareAuthenticationMode.None, check.Method);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task CheckConnectionAsync_reports_a_timeout()
    {
        using var harness = CloudflareHarness.CreateWithResponder(
            (_, _) => throw new TaskCanceledException("timed out"),
            options => options.MaxRetries = 0);

        var check = await harness.Client.CheckConnectionAsync();

        Assert.False(check.IsSuccess);
        Assert.True(check.IsTimeout);
        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task VerifyAccountTokenAsync_uses_the_account_scoped_verify_endpoint()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"t-2\",\"status\":\"active\"}");

        var verification = await harness.Client.User.VerifyAccountTokenAsync();

        Assert.Equal($"/client/v4/accounts/{CloudflareHarness.TestAccountId}/tokens/verify", harness.LastRequest.Path);
        Assert.True(verification.IsActive);
    }

    [Fact]
    public async Task VerifyAccountTokenAsync_accepts_an_explicit_account_id()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"t-3\",\"status\":\"active\"}");

        await harness.Client.User.VerifyAccountTokenAsync("acct-other");

        Assert.Equal("/client/v4/accounts/acct-other/tokens/verify", harness.LastRequest.Path);
    }

    [Fact]
    public async Task VerifyAccountTokenAsync_needs_an_account_id()
    {
        using var harness = CloudflareHarness.Create(options => options.AccountId = null);
        harness.RespondWithResult("{\"id\":\"t-4\",\"status\":\"active\"}");

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.User.VerifyAccountTokenAsync());

        Assert.NotEmpty(exception.Problems);
    }

    [Fact]
    public async Task CheckConnectionAsync_verifies_an_account_owned_token_without_trying_the_user_endpoint()
    {
        using var harness = CloudflareHarness.Create(options => options.ApiToken = "cfat_0123456789012345678901234567890123456789");
        harness.RespondWithResult("{\"id\":\"t-5\",\"status\":\"active\"}");

        var check = await harness.Client.CheckConnectionAsync();

        Assert.True(check.IsSuccess);
        Assert.Contains("Account-owned", check.Message, StringComparison.Ordinal);
        Assert.Equal($"/client/v4/accounts/{CloudflareHarness.TestAccountId}/tokens/verify", harness.LastRequest.Path);
        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task CheckConnectionAsync_falls_back_to_the_account_endpoint_when_the_user_one_rejects_the_token()
    {
        // Tokens issued before 2026 carry no prefix, so the user-scoped rejection is the only hint that
        // the credential is account-owned. Without the fallback this reports "invalid token" for a
        // secret that works.
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.Forbidden, 1000, "Invalid API Token");
        harness.RespondWithResult("{\"id\":\"t-6\",\"status\":\"active\"}");

        var check = await harness.Client.CheckConnectionAsync();

        Assert.True(check.IsSuccess);
        Assert.Equal(2, harness.RequestCount);
        Assert.Contains("account-owned", check.Message, StringComparison.Ordinal);
        Assert.Equal($"/client/v4/accounts/{CloudflareHarness.TestAccountId}/tokens/verify", harness.LastRequest.Path);
    }

    [Fact]
    public async Task CheckConnectionAsync_still_reports_the_rejection_when_both_endpoints_refuse_the_token()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.Forbidden, 1000, "Invalid API Token");
        harness.RespondWithError(HttpStatusCode.Forbidden, 1000, "Invalid API Token");

        var check = await harness.Client.CheckConnectionAsync();

        Assert.False(check.IsSuccess);
        Assert.Equal(2, harness.RequestCount);
        Assert.Contains("Invalid API Token", check.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckConnectionAsync_does_not_fall_back_without_an_account_id()
    {
        using var harness = CloudflareHarness.Create(options => options.AccountId = null);
        harness.RespondWithError(HttpStatusCode.Forbidden, 1000, "Invalid API Token");

        var check = await harness.Client.CheckConnectionAsync();

        Assert.False(check.IsSuccess);
        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task Accounts_ListAsync_and_GetAsync_use_the_accounts_collection()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage(
            "[{\"id\":\"acct-1\",\"name\":\"Personal\",\"type\":\"standard\"},{\"id\":\"acct-2\",\"name\":\"Work\"}]",
            page: 1, totalPages: 1, count: 2, totalCount: 2);

        var page = await harness.Client.Accounts.ListAsync(new AccountListParameters { Name = "Personal", Page = 1, PerPage = 20 });

        Assert.Equal("/client/v4/accounts", harness.LastRequest.Path);
        Assert.Equal("page=1&per_page=20&name=Personal", harness.LastRequest.Query.TrimStart('?'));
        Assert.Equal(2, page.Count);
        Assert.Equal("standard", page.Items[0].Type);

        harness.RespondWithResult("{\"id\":\"acct-1\",\"name\":\"Personal\"}");
        var account = await harness.Client.Accounts.GetAsync("acct-1");

        Assert.Equal("/client/v4/accounts/acct-1", harness.LastRequest.Path);
        Assert.Equal("Personal", account!.Name);
    }

    [Fact]
    public async Task Accounts_GetAsync_falls_back_to_the_default_account_id()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"acct-1234\"}");

        await harness.Client.Accounts.GetAsync();

        Assert.Equal($"/client/v4/accounts/{CloudflareHarness.TestAccountId}", harness.LastRequest.Path);
    }

    [Fact]
    public async Task Accounts_GetAsync_returns_null_when_the_account_is_not_reachable()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.NotFound, 10000, "not found");

        Assert.Null(await harness.Client.Accounts.GetAsync("acct-x"));
    }
}

public class CloudflareClientTests
{
    [Fact]
    public void Create_builds_a_client_that_exposes_every_service()
    {
        using var client = CloudflareClient.Create(options =>
        {
            options.ApiToken = "tok";
            options.AccountId = "acct";
            options.ZoneId = "zone";
        });

        Assert.NotNull(client.User);
        Assert.NotNull(client.Accounts);
        Assert.NotNull(client.Zones);
        Assert.NotNull(client.DnsRecords);
        Assert.NotNull(client.Workers);
        Assert.NotNull(client.Pages);
        Assert.NotNull(client.D1);
        Assert.NotNull(client.Kv);
        Assert.NotNull(client.R2);
        Assert.NotNull(client.Transport);
        Assert.Equal("acct", client.Options.AccountId);
    }

    [Fact]
    public void The_services_all_share_one_transport()
    {
        using var client = CloudflareClient.Create(options => options.ApiToken = "tok");

        var transport = client.Transport;

        // Every service holds the same transport, so one ConfigureRequest hook applies everywhere.
        Assert.IsType<CloudflareClient>(client);
        Assert.Same(transport, ((ICloudflareClient)client).Transport);
        Assert.IsAssignableFrom<IZonesService>(client.Zones);
        Assert.IsAssignableFrom<IDnsRecordsService>(client.DnsRecords);
        Assert.IsAssignableFrom<IWorkersScriptsService>(client.Workers);
        Assert.IsAssignableFrom<IPagesProjectsService>(client.Pages);
        Assert.IsAssignableFrom<ID1DatabaseService>(client.D1);
        Assert.IsAssignableFrom<IKvService>(client.Kv);
        Assert.IsAssignableFrom<IR2Service>(client.R2);
        Assert.IsAssignableFrom<ISslService>(client.Ssl);
        Assert.IsAssignableFrom<IUserService>(client.User);
        Assert.IsAssignableFrom<IAccountsService>(client.Accounts);
    }

    [Fact]
    public void Create_validates_the_options_before_anything_is_built()
    {
        var exception = Assert.Throws<CloudflareConfigurationException>(() => CloudflareClient.Create());

        Assert.Contains("CLOUDFLARE_API_TOKEN", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateFromEnvironment_reads_the_credentials()
    {
        var previousToken = Environment.GetEnvironmentVariable(CloudflareDefaults.EnvironmentVariables.ApiToken);
        Environment.SetEnvironmentVariable(CloudflareDefaults.EnvironmentVariables.ApiToken, "env-token");

        try
        {
            using var client = CloudflareClient.CreateFromEnvironment();

            Assert.Equal("env-token", client.Options.ApiToken);
            Assert.Equal(CloudflareAuthenticationMode.ApiToken, client.Options.AuthenticationMode);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CloudflareDefaults.EnvironmentVariables.ApiToken, previousToken);
        }
    }

    [Fact]
    public void The_transport_constructor_rejects_a_null_transport()
    {
        Assert.Throws<ArgumentNullException>(() => new CloudflareClient((ICloudflareTransport)null!));
    }

    [Fact]
    public void A_client_built_around_a_caller_supplied_transport_does_not_own_it()
    {
        using var handler = new StubHttpMessageHandler();
        using var httpClient = new HttpClient(handler);
        var transport = new CloudflareTransport(httpClient, new CloudflareClientOptions { ApiToken = "tok" });
        using var client = new CloudflareClient(transport);

        client.Dispose();
        client.Dispose();

        // The caller still owns the transport and the HttpClient, so disposing the client is a no-op
        // for both and the options stay readable.
        Assert.Equal("tok", transport.Options.ApiToken);
        Assert.Equal("tok", client.Options.ApiToken);
        Assert.Same(transport, client.Transport);
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var client = CloudflareClient.Create(options => options.ApiToken = "tok");

        client.Dispose();
        client.Dispose();
    }

    [Fact]
    public void The_factory_creates_independent_clients_from_shared_defaults()
    {
        var factory = new CloudflareClientFactory(new CloudflareClientOptions
        {
            ApiToken = "default-token",
            AccountId = "default-account",
            UserAgent = "CF.Net.Tests/1.0",
        });

        using var first = factory.Create();
        using var second = factory.Create(options =>
        {
            options.ApiToken = "other-token";
            options.AccountId = "other-account";
        });

        Assert.NotSame(first, second);
        Assert.Equal("default-token", first.Options.ApiToken);
        Assert.Equal("default-account", first.Options.AccountId);
        Assert.Equal("other-token", second.Options.ApiToken);
        Assert.Equal("other-account", second.Options.AccountId);

        // Transport tuning is inherited from the defaults.
        Assert.Equal("CF.Net.Tests/1.0", second.Options.UserAgent);
    }

    [Fact]
    public void The_factory_copies_its_defaults_so_later_mutation_does_not_leak()
    {
        var defaults = new CloudflareClientOptions { ApiToken = "tok", AccountId = "acct" };
        var factory = new CloudflareClientFactory(defaults);

        defaults.ApiToken = "changed";

        using var client = factory.Create();

        Assert.Equal("tok", client.Options.ApiToken);
    }

    [Fact]
    public void The_factory_creates_from_environment_on_top_of_its_defaults()
    {
        var previous = Environment.GetEnvironmentVariable(CloudflareDefaults.EnvironmentVariables.ApiToken);
        Environment.SetEnvironmentVariable(CloudflareDefaults.EnvironmentVariables.ApiToken, "env-token");

        try
        {
            var factory = new CloudflareClientFactory(new CloudflareClientOptions
            {
                ApiToken = "default-token",
                UserAgent = "CF.Net.Tests/1.0",
                MaxRetries = 7,
            });

            using var client = factory.CreateFromEnvironment();

            // The environment wins on credentials, the defaults win on transport tuning.
            Assert.Equal("env-token", client.Options.ApiToken);
            Assert.Equal("CF.Net.Tests/1.0", client.Options.UserAgent);
            Assert.Equal(7, client.Options.MaxRetries);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CloudflareDefaults.EnvironmentVariables.ApiToken, previous);
        }
    }

    [Fact]
    public void The_factory_validates_before_creating_a_client()
    {
        var factory = new CloudflareClientFactory();

        Assert.Throws<CloudflareConfigurationException>(() => factory.Create());
    }

    [Fact]
    public void The_factory_can_source_http_clients_from_a_host()
    {
        using var handler = new StubHttpMessageHandler();
        using var httpClient = new HttpClient(handler);
        var created = 0;

        var factory = new CloudflareClientFactory(
            new CloudflareClientOptions { ApiToken = "tok" },
            httpClientFactory: () =>
            {
                created++;
                return httpClient;
            });

        using var client = factory.Create();

        Assert.Equal(1, created);
        Assert.NotNull(client.Transport);
    }
}

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCloudflare_registers_the_client_the_factory_and_every_service()
    {
        using var provider = new ServiceCollection()
            .AddCloudflare(options =>
            {
                options.ApiToken = "tok";
                options.AccountId = "acct";
                options.ZoneId = "zone";
            })
            .BuildServiceProvider();

        var client = provider.GetRequiredService<ICloudflareClient>();

        Assert.NotNull(client);
        Assert.Equal("acct", client.Options.AccountId);

        // Each service resolves to the same singleton the client exposes.
        Assert.Same(client.Zones, provider.GetRequiredService<IZonesService>());
        Assert.Same(client.DnsRecords, provider.GetRequiredService<IDnsRecordsService>());
        Assert.Same(client.Workers, provider.GetRequiredService<IWorkersScriptsService>());
        Assert.Same(client.Pages, provider.GetRequiredService<IPagesProjectsService>());
        Assert.Same(client.D1, provider.GetRequiredService<ID1DatabaseService>());
        Assert.Same(client.Kv, provider.GetRequiredService<IKvService>());
        Assert.Same(client.R2, provider.GetRequiredService<IR2Service>());
        Assert.Same(client.Ssl, provider.GetRequiredService<ISslService>());
        Assert.Same(client.User, provider.GetRequiredService<IUserService>());
        Assert.Same(client.Accounts, provider.GetRequiredService<IAccountsService>());
        Assert.Same(client.Transport, provider.GetRequiredService<ICloudflareTransport>());
        Assert.Same(client, provider.GetRequiredService<CloudflareClient>());
        Assert.NotNull(provider.GetRequiredService<ICloudflareClientFactory>());
    }

    [Fact]
    public void AddCloudflare_is_idempotent()
    {
        using var provider = new ServiceCollection()
            .AddCloudflare(options => options.ApiToken = "tok")
            .AddCloudflare(options => options.ApiToken = "other")
            .BuildServiceProvider();

        // Both Configure callbacks run, the last one wins.
        Assert.Equal("other", provider.GetRequiredService<ICloudflareClient>().Options.ApiToken);
    }

    [Fact]
    public void AddCloudflare_accepts_a_ready_made_options_instance()
    {
        using var provider = new ServiceCollection()
            .AddCloudflare(new CloudflareClientOptions { ApiToken = "tok", AccountId = "acct", MaxRetries = 5 })
            .BuildServiceProvider();

        var options = provider.GetRequiredService<ICloudflareClient>().Options;

        Assert.Equal("tok", options.ApiToken);
        Assert.Equal("acct", options.AccountId);
        Assert.Equal(5, options.MaxRetries);
    }

    [Fact]
    public void AddCloudflareFromConfiguration_binds_the_Cloudflare_section()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cloudflare:ApiToken"] = "config-token",
                ["Cloudflare:AccountId"] = "config-account",
                ["Cloudflare:ZoneId"] = "config-zone",
                ["Cloudflare:MaxRetries"] = "4",
                ["Cloudflare:ThrowOnApiError"] = "false",
                ["Cloudflare:Timeout"] = "00:00:30",
                ["Cloudflare:BaseAddress"] = "https://proxy.example.test/client/v4/",
                ["Other:ApiToken"] = "ignored",
            })
            .Build();

        using var provider = new ServiceCollection()
            .AddCloudflareFromConfiguration(configuration)
            .BuildServiceProvider();

        var options = provider.GetRequiredService<ICloudflareClient>().Options;

        Assert.Equal("config-token", options.ApiToken);
        Assert.Equal("config-account", options.AccountId);
        Assert.Equal("config-zone", options.ZoneId);
        Assert.Equal(4, options.MaxRetries);
        Assert.False(options.ThrowOnApiError);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Timeout);
        Assert.Equal("https://proxy.example.test/client/v4/", options.BaseAddress.OriginalString);
    }

    [Fact]
    public void AddCloudflareFromConfiguration_honours_a_custom_section_and_a_callback()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cloudflare:Other:ApiToken"] = "nested-token",
            })
            .Build();

        using var provider = new ServiceCollection()
            .AddCloudflareFromConfiguration(
                configuration,
                sectionName: "Cloudflare:Other",
                configure: options => options.AccountId = "from-callback")
            .BuildServiceProvider();

        var options = provider.GetRequiredService<ICloudflareClient>().Options;

        Assert.Equal("nested-token", options.ApiToken);
        Assert.Equal("from-callback", options.AccountId);
    }

    [Fact]
    public void AddCloudflareFromConfiguration_falls_back_to_the_default_section_name()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Cloudflare:ApiToken"] = "tok" })
            .Build();

        using var provider = new ServiceCollection()
            .AddCloudflareFromConfiguration(configuration, sectionName: "   ")
            .BuildServiceProvider();

        Assert.Equal("tok", provider.GetRequiredService<ICloudflareClient>().Options.ApiToken);
    }

    [Fact]
    public void AddCloudflareFromEnvironment_reads_the_process_environment()
    {
        var previous = Environment.GetEnvironmentVariable(CloudflareDefaults.EnvironmentVariables.ApiToken);
        var previousAccount = Environment.GetEnvironmentVariable(CloudflareDefaults.EnvironmentVariables.AccountId);
        Environment.SetEnvironmentVariable(CloudflareDefaults.EnvironmentVariables.ApiToken, "di-token");
        Environment.SetEnvironmentVariable(CloudflareDefaults.EnvironmentVariables.AccountId, "di-account");

        try
        {
            using var provider = new ServiceCollection()
                .AddCloudflareFromEnvironment()
                .BuildServiceProvider();

            var options = provider.GetRequiredService<ICloudflareClient>().Options;

            Assert.Equal("di-token", options.ApiToken);
            Assert.Equal("di-account", options.AccountId);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CloudflareDefaults.EnvironmentVariables.ApiToken, previous);
            Environment.SetEnvironmentVariable(CloudflareDefaults.EnvironmentVariables.AccountId, previousAccount);
        }
    }

    [Fact]
    public void AddCloudflareFromEnvironment_lets_the_callback_override()
    {
        var previous = Environment.GetEnvironmentVariable(CloudflareDefaults.EnvironmentVariables.ApiToken);
        Environment.SetEnvironmentVariable(CloudflareDefaults.EnvironmentVariables.ApiToken, "env-token");

        try
        {
            using var provider = new ServiceCollection()
                .AddCloudflareFromEnvironment(options => options.ApiToken = "callback-token")
                .BuildServiceProvider();

            Assert.Equal("callback-token", provider.GetRequiredService<ICloudflareClient>().Options.ApiToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CloudflareDefaults.EnvironmentVariables.ApiToken, previous);
        }
    }

    [Fact]
    public void AddCloudflareHttpClient_registers_a_named_client_with_the_shared_timeout()
    {
        var collection = new ServiceCollection();
        var builder = collection.AddCloudflareHttpClient(
            options =>
            {
                options.ApiToken = "tok";
                options.Timeout = TimeSpan.FromSeconds(13);
            });

        Assert.NotNull(builder);

        using var provider = collection.BuildServiceProvider();

        var namedClient = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ServiceCollectionCloudflareExtensions.HttpClientName);

        Assert.Equal("https://api.cloudflare.com/client/v4/", namedClient.BaseAddress!.OriginalString);
        Assert.Equal(TimeSpan.FromSeconds(13), namedClient.Timeout);

        var transport = provider.GetRequiredService<ICloudflareTransport>();

        Assert.NotNull(transport);
        Assert.Equal("tok", transport.Options.ApiToken);
    }

    [Fact]
    public void AddCloudflareHttpClient_accepts_further_client_configuration()
    {
        using var stub = new StubHttpMessageHandler();
        var collection = new ServiceCollection();
        collection.AddCloudflareHttpClient(
            options => options.ApiToken = "tok",
            httpClientName: "cf",
            configureClient: client => client.ConfigurePrimaryHttpMessageHandler(() => stub));

        using var provider = collection.BuildServiceProvider();

        var namedClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient("cf");

        Assert.NotNull(namedClient);
        Assert.Equal(TimeSpan.FromSeconds(100), namedClient.Timeout);
    }

    [Fact]
    public void AddCloudflareHttpClient_falls_back_to_the_default_name()
    {
        var collection = new ServiceCollection();
        collection.AddCloudflareHttpClient(options => options.ApiToken = "tok", httpClientName: "  ");

        using var provider = collection.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ServiceCollectionCloudflareExtensions.HttpClientName));
    }

    [Fact]
    public async Task AddCloudflareHttpClient_sends_through_the_named_client()
    {
        // Registering the stub in the container first keeps the handler builder callback pure:
        // mutating an IServiceCollection after BuildServiceProvider is not supported.
        var stub = new StubHttpMessageHandler();
        stub.RespondWithResult("[{\"id\":\"z-1\",\"name\":\"a.test\"}]");

        var collection = new ServiceCollection();
        collection.AddSingleton(stub);
        collection.AddCloudflareHttpClient(
            options =>
            {
                options.ApiToken = "tok";
                options.MaxRetries = 0;
            },
            configureClient: client => client.ConfigurePrimaryHttpMessageHandler(
                provider => provider.GetRequiredService<StubHttpMessageHandler>()));

        using var provider = collection.BuildServiceProvider();

        var page = await provider.GetRequiredService<IZonesService>().ListAsync();

        Assert.Single(page.Items);
        Assert.Equal("a.test", page.Items[0].Name);
        Assert.Equal("/client/v4/zones", stub.LastRequest.Path);
        Assert.Equal("Bearer tok", stub.LastRequest.Header("Authorization"));
    }

    [Fact]
    public void AddCloudflareClientFactory_registers_only_the_factory()
    {
        using var provider = new ServiceCollection()
            .AddCloudflareClientFactory(options =>
            {
                options.ApiToken = "tok";
                options.UserAgent = "CF.Net.Tests/1.0";
            })
            .BuildServiceProvider();

        var factory = provider.GetRequiredService<ICloudflareClientFactory>();
        using var client = factory.Create(options => options.AccountId = "acct");

        Assert.Equal("tok", client.Options.ApiToken);
        Assert.Equal("acct", client.Options.AccountId);
        Assert.Equal("CF.Net.Tests/1.0", client.Options.UserAgent);
        Assert.Null(provider.GetService<ICloudflareClient>());
    }

    [Fact]
    public void The_factory_registered_by_AddCloudflare_reuses_the_named_http_client_when_one_exists()
    {
        var collection = new ServiceCollection();
        collection.AddCloudflareHttpClient(options => options.ApiToken = "tok");

        using var provider = collection.BuildServiceProvider();

        using var first = provider.GetRequiredService<ICloudflareClientFactory>().Create();
        using var second = provider.GetRequiredService<ICloudflareClientFactory>().Create();

        Assert.NotSame(first, second);
        Assert.Equal("tok", first.Options.ApiToken);
    }

    [Fact]
    public void The_extensions_reject_a_null_service_collection()
    {
        Assert.Throws<ArgumentNullException>(
            () => ServiceCollectionCloudflareExtensions.AddCloudflare(null!, (Action<CloudflareClientOptions>?)null));
        Assert.Throws<ArgumentNullException>(
            () => ServiceCollectionCloudflareExtensions.AddCloudflare(null!, new CloudflareClientOptions()));
        Assert.Throws<ArgumentNullException>(
            () => ServiceCollectionCloudflareExtensions.AddCloudflareFromConfiguration(null!, new ConfigurationBuilder().Build()));
        Assert.Throws<ArgumentNullException>(
            () => new ServiceCollection().AddCloudflareFromConfiguration(null!));
        Assert.Throws<ArgumentNullException>(
            () => ServiceCollectionCloudflareExtensions.AddCloudflareHttpClient(null!));
        Assert.Throws<ArgumentNullException>(
            () => ServiceCollectionCloudflareExtensions.AddCloudflareClientFactory(null!));
    }

    [Fact]
    public async Task A_custom_transport_registered_beforehand_wins_over_the_default_one()
    {
        // TryAddSingleton only registers when nothing is there yet, which is exactly the extension
        // point an application needs to inject a logging or mocking transport.
        var stub = new StubHttpMessageHandler();
        stub.RespondWithResult("[{\"uuid\":\"db-1\",\"name\":\"app\"}]");

        var options = new CloudflareClientOptions
        {
            ApiToken = "tok",
            AccountId = "acct",
            MaxRetries = 0,
        };

        var collection = new ServiceCollection();
        collection.AddSingleton<ICloudflareTransport>(new CloudflareTransport(new HttpClient(stub), options));
        collection.AddCloudflare(configure =>
        {
            configure.ApiToken = options.ApiToken;
            configure.AccountId = options.AccountId;
        });

        using var provider = collection.BuildServiceProvider();

        var databases = await provider.GetRequiredService<ID1DatabaseService>().ListAsync();

        Assert.Equal("app", Assert.Single(databases.Items).Name);
        Assert.Equal("/client/v4/accounts/acct/d1/database", stub.LastRequest.Path);
        Assert.Same(
            provider.GetRequiredService<ICloudflareTransport>(),
            provider.GetRequiredService<ICloudflareClient>().Transport);
    }
}
