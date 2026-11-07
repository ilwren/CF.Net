using CFNet.Configuration;
using CFNet.Exceptions;
using Xunit;

namespace CFNet.UnitTests.Configuration;

public class CloudflareClientOptionsTests
{
    [Fact]
    public void Defaults_match_documented_values()
    {
        var options = new CloudflareClientOptions();

        Assert.Equal("https://api.cloudflare.com/client/v4/", options.BaseAddress.OriginalString);
        Assert.Equal(TimeSpan.FromSeconds(100), options.Timeout);
        Assert.Equal(2, options.MaxRetries);
        Assert.Equal(TimeSpan.FromMilliseconds(500), options.RetryBaseDelay);
        Assert.Equal(TimeSpan.FromSeconds(20), options.MaxRetryDelay);
        Assert.True(options.UseRetryJitter);
        Assert.True(options.ThrowOnApiError);
        Assert.Contains("CF.Net", options.UserAgent);
        Assert.Same(CFNet.Serialization.CloudflareJson.Default, options.JsonSerializerOptions);
    }

    [Theory]
    [InlineData(null, null, null, CloudflareAuthenticationMode.None)]
    [InlineData("token", null, null, CloudflareAuthenticationMode.ApiToken)]
    [InlineData(null, "key", "a@b.c", CloudflareAuthenticationMode.GlobalApiKey)]
    [InlineData("token", "key", "a@b.c", CloudflareAuthenticationMode.ApiToken)]
    [InlineData(null, "key", null, CloudflareAuthenticationMode.None)]
    [InlineData(null, null, "a@b.c", CloudflareAuthenticationMode.None)]
    [InlineData("   ", "  ", "  ", CloudflareAuthenticationMode.None)]
    public void AuthenticationMode_prefers_token_and_rejects_incomplete_legacy_key(
        string? token,
        string? key,
        string? email,
        CloudflareAuthenticationMode expected)
    {
        var options = new CloudflareClientOptions
        {
            ApiToken = token,
            GlobalApiKey = key,
            ApiEmail = email,
        };

        Assert.Equal(expected, options.AuthenticationMode);
        Assert.Equal(expected != CloudflareAuthenticationMode.None, options.HasCredentials);
    }

    [Theory]
    [InlineData("cfat_0123456789012345678901234567890123456789", true)]
    [InlineData("cfut_0123456789012345678901234567890123456789", false)]
    [InlineData("cfk_0123456789012345678901234567890123456789", false)]
    [InlineData("0123456789abcdef0123456789abcdef01234567", false)]
    [InlineData(" CFat_0123456789012345678901234567890123456789", false)]
    [InlineData(null, false)]
    public void HasAccountOwnedToken_only_matches_the_account_owned_prefix(
        string? token,
        bool expected)
    {
        // A pre-2026 token carries no prefix, so an unprefixed one cannot be recognised here - that is
        // why CheckConnectionAsync also falls back when the user-scoped endpoint rejects a credential.
        Assert.Equal(expected, new CloudflareClientOptions { ApiToken = token }.HasAccountOwnedToken);
    }

    [Fact]
    public void Validate_reports_every_problem_at_once()
    {
        var options = new CloudflareClientOptions
        {
            GlobalApiKey = "key",
            ApiEmail = null,
            BaseAddress = new Uri("client/v4", UriKind.Relative),
            MaxRetries = -1,
            RetryBaseDelay = TimeSpan.FromSeconds(-1),
            Timeout = TimeSpan.FromSeconds(-5),
        };

        var exception = Assert.Throws<CloudflareConfigurationException>(() => options.Validate());

        Assert.Contains(exception.Problems, problem => problem.Contains("ApiEmail", StringComparison.Ordinal));
        Assert.Contains(exception.Problems, problem => problem.Contains("BaseAddress", StringComparison.Ordinal));
        Assert.Contains(exception.Problems, problem => problem.Contains("MaxRetries", StringComparison.Ordinal));
        Assert.Contains(exception.Problems, problem => problem.Contains("Timeout", StringComparison.Ordinal));
        Assert.Contains(exception.Problems, problem => problem.Contains("RetryBaseDelay", StringComparison.Ordinal));

        // A global key without an e-mail leaves no usable scheme at all, so that is reported twice:
        // once as the incomplete pair, once as missing credentials.
        Assert.Contains(exception.Problems, problem => problem.Contains("credentials", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(6, exception.Problems.Count);
    }

    [Fact]
    public void Validate_accepts_a_minimal_token_configuration()
    {
        var options = new CloudflareClientOptions { ApiToken = "tok", BaseAddress = new Uri("https://api.cloudflare.com/client/v4") };

        // No trailing slash is allowed: the transport appends one before joining relative paths.
        options.Validate();
    }

    [Fact]
    public void RequireAccountId_and_RequireZoneId_use_explicit_then_default_then_throw()
    {
        var options = new CloudflareClientOptions { ApiToken = "tok", AccountId = "acct-default", ZoneId = "zone-default" };

        Assert.Equal("explicit", options.RequireAccountId("explicit"));
        Assert.Equal("acct-default", options.RequireAccountId(null));

        var withoutDefaults = new CloudflareClientOptions { ApiToken = "tok" };
        var accountProblem = Assert.Throws<CloudflareConfigurationException>(() => withoutDefaults.RequireAccountId(null)).Problems.Single();
        Assert.Contains(CloudflareDefaults.EnvironmentVariables.AccountId, accountProblem, StringComparison.Ordinal);

        var zoneProblem = Assert.Throws<CloudflareConfigurationException>(() => withoutDefaults.RequireZoneId("   ")).Problems.Single();
        Assert.Contains(CloudflareDefaults.EnvironmentVariables.ZoneId, zoneProblem, StringComparison.Ordinal);
    }

    [Fact]
    public void Clone_copies_every_setting()
    {
        var original = new CloudflareClientOptions
        {
            BaseAddress = new Uri("https://proxy.example.test/v4"),
            ApiToken = "tok",
            GlobalApiKey = "key",
            ApiEmail = "a@b.c",
            AccountId = "acct",
            ZoneId = "zone",
            Timeout = TimeSpan.FromSeconds(7),
            UserAgent = "agent/1",
            MaxRetries = 9,
            RetryBaseDelay = TimeSpan.FromMilliseconds(11),
            MaxRetryDelay = TimeSpan.FromSeconds(13),
            UseRetryJitter = false,
            ThrowOnApiError = false,
        };

        var clone = original.Clone();

        Assert.NotSame(original, clone);
        Assert.Equal(original.BaseAddress, clone.BaseAddress);
        Assert.Equal(original.ApiToken, clone.ApiToken);
        Assert.Equal(original.GlobalApiKey, clone.GlobalApiKey);
        Assert.Equal(original.ApiEmail, clone.ApiEmail);
        Assert.Equal(original.AccountId, clone.AccountId);
        Assert.Equal(original.ZoneId, clone.ZoneId);
        Assert.Equal(original.Timeout, clone.Timeout);
        Assert.Equal(original.UserAgent, clone.UserAgent);
        Assert.Equal(original.MaxRetries, clone.MaxRetries);
        Assert.Equal(original.RetryBaseDelay, clone.RetryBaseDelay);
        Assert.Equal(original.MaxRetryDelay, clone.MaxRetryDelay);
        Assert.Equal(original.UseRetryJitter, clone.UseRetryJitter);
        Assert.Equal(original.ThrowOnApiError, clone.ThrowOnApiError);
        Assert.Same(original.JsonSerializerOptions, clone.JsonSerializerOptions);
    }

    [Fact]
    public void Clone_copies_the_request_and_response_hooks()
    {
        var requestCalls = 0;
        var responseCalls = 0;
        var original = new CloudflareClientOptions
        {
            ApiToken = "tok",
            ConfigureRequest = _ => requestCalls++,
            InspectResponse = _ => responseCalls++,
        };

        var clone = original.Clone();
        clone.ConfigureRequest?.Invoke(new System.Net.Http.HttpRequestMessage());
        clone.InspectResponse?.Invoke(new System.Net.Http.HttpResponseMessage());

        Assert.Equal(1, requestCalls);
        Assert.Equal(1, responseCalls);
    }

    [Fact]
    public void FromEnvironment_leaves_credentials_alone_when_nothing_is_set()
    {
        var options = CloudflareClientOptions.FromEnvironment(_ => null);

        Assert.Null(options.ApiToken);
        Assert.Null(options.AccountId);
        Assert.Equal("https://api.cloudflare.com/client/v4/", options.BaseAddress.OriginalString);
    }

    [Fact]
    public void FromEnvironment_reads_the_documented_variable_names()
    {
        var variables = new Dictionary<string, string?>
        {
            [CloudflareDefaults.EnvironmentVariables.ApiToken] = "tok-from-env",
            [CloudflareDefaults.EnvironmentVariables.AccountId] = "acct-from-env",
            [CloudflareDefaults.EnvironmentVariables.ZoneId] = "zone-from-env",
            [CloudflareDefaults.EnvironmentVariables.BaseAddress] = "https://proxy.example.test/client/v4/",
        };

        var options = CloudflareClientOptions.FromEnvironment(Lookup(variables));

        Assert.Equal("tok-from-env", options.ApiToken);
        Assert.Equal("acct-from-env", options.AccountId);
        Assert.Equal("zone-from-env", options.ZoneId);
        Assert.Equal("https://proxy.example.test/client/v4/", options.BaseAddress.OriginalString);
        Assert.Equal(CloudflareAuthenticationMode.ApiToken, options.AuthenticationMode);
    }

    [Fact]
    public void FromEnvironment_reads_the_legacy_key_pair()
    {
        var variables = new Dictionary<string, string?>
        {
            [CloudflareDefaults.EnvironmentVariables.GlobalApiKey] = "key-from-env",
            [CloudflareDefaults.EnvironmentVariables.ApiEmail] = "env@example.test",
        };

        var options = CloudflareClientOptions.FromEnvironment(Lookup(variables));

        Assert.Equal(CloudflareAuthenticationMode.GlobalApiKey, options.AuthenticationMode);
        Assert.Equal("key-from-env", options.GlobalApiKey);
        Assert.Equal("env@example.test", options.ApiEmail);
    }

    [Fact]
    public void FromEnvironment_trims_and_treats_blank_values_as_absent()
    {
        var options = CloudflareClientOptions.FromEnvironment(Lookup(new Dictionary<string, string?>
        {
            [CloudflareDefaults.EnvironmentVariables.ApiToken] = "  padded-token  ",
            [CloudflareDefaults.EnvironmentVariables.AccountId] = "   ",
        }));

        Assert.Equal("padded-token", options.ApiToken);
        Assert.Null(options.AccountId);
    }

    /// <summary>Turns a dictionary into the lookup function <see cref="CloudflareClientOptions.FromEnvironment"/> expects.</summary>
    private static Func<string, string?> Lookup(Dictionary<string, string?> variables) =>
        name => variables.TryGetValue(name, out var value) ? value : null;
}

[Collection("Environment")]
public class CloudflareClientOptionsProcessEnvironmentTests
{
    [Fact]
    public void FromEnvironment_ignores_a_malformed_base_address()
    {
        var previous = Environment.GetEnvironmentVariable(CloudflareDefaults.EnvironmentVariables.BaseAddress);
        Environment.SetEnvironmentVariable(CloudflareDefaults.EnvironmentVariables.BaseAddress, "not a uri");

        try
        {
            var options = CloudflareClientOptions.FromEnvironment();

            Assert.Equal("https://api.cloudflare.com/client/v4/", options.BaseAddress.OriginalString);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CloudflareDefaults.EnvironmentVariables.BaseAddress, previous);
        }
    }

    [Fact]
    public void Defaults_environment_variable_names_are_the_documented_ones()
    {
        Assert.Equal("CLOUDFLARE_API_TOKEN", CloudflareDefaults.EnvironmentVariables.ApiToken);
        Assert.Equal("CLOUDFLARE_GLOBAL_API_KEY", CloudflareDefaults.EnvironmentVariables.GlobalApiKey);
        Assert.Equal("CLOUDFLARE_API_EMAIL", CloudflareDefaults.EnvironmentVariables.ApiEmail);
        Assert.Equal("CLOUDFLARE_ACCOUNT_ID", CloudflareDefaults.EnvironmentVariables.AccountId);
        Assert.Equal("CLOUDFLARE_ZONE_ID", CloudflareDefaults.EnvironmentVariables.ZoneId);
        Assert.Equal("Cloudflare", CloudflareDefaults.ConfigurationSectionName);
        Assert.Equal("CLOUDFLARE_BASE_ADDRESS", CloudflareDefaults.EnvironmentVariables.BaseAddress);
        Assert.Equal("Authorization", CloudflareDefaults.Headers.Authorization);
        Assert.Equal("X-Auth-Key", CloudflareDefaults.Headers.AuthKey);
        Assert.Equal("X-Auth-Email", CloudflareDefaults.Headers.AuthEmail);
        Assert.Equal("cf-r2-jurisdiction", CloudflareDefaults.Headers.R2Jurisdiction);
        Assert.Equal("CF-KV-Metadata", CloudflareDefaults.Headers.KvMetadata);
        Assert.Equal("CF-RAY", CloudflareDefaults.Headers.CfRay);
    }
}
