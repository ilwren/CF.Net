using CFNet.Configuration;
using CFNet.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace CFNet.IntegrationTests;

/// <summary>
/// Everything an integration test needs: the credentials from the environment, a ready made client,
/// and a way to opt out of a test without failing it when the credentials are absent.
/// </summary>
/// <remarks>
/// Credentials are read from exactly the same variables the GitHub Actions workflow injects, so a
/// local run and a CI run behave identically:
/// <list type="bullet">
///   <item><description><c>CLOUDFLARE_API_TOKEN</c> - preferred</description></item>
///   <item><description><c>CLOUDFLARE_GLOBAL_API_KEY</c> + <c>CLOUDFLARE_API_EMAIL</c> - legacy alternative</description></item>
///   <item><description><c>CLOUDFLARE_ACCOUNT_ID</c> - required for Workers, Pages, D1, KV and R2</description></item>
///   <item><description><c>CLOUDFLARE_ZONE_ID</c> - required for the DNS record lifecycle test</description></item>
///   <item><description><c>CLOUDFLARE_IT_ENABLE_WRITES</c> - set to <c>true</c> to allow create/delete tests</description></item>
/// </list>
/// </remarks>
public abstract class CloudflareIntegrationTest : IDisposable
{
    private CloudflareClient? _client;
    private ServiceProvider? _provider;

    protected CloudflareIntegrationTest(ITestOutputHelper output)
    {
        Output = output;
        ApiToken = Read("CLOUDFLARE_API_TOKEN");
        GlobalApiKey = Read("CLOUDFLARE_GLOBAL_API_KEY");
        ApiEmail = Read("CLOUDFLARE_API_EMAIL");
        AccountId = Read("CLOUDFLARE_ACCOUNT_ID");
        ZoneId = Read("CLOUDFLARE_ZONE_ID");
        WritesEnabled = string.Equals(Read("CLOUDFLARE_IT_ENABLE_WRITES"), "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>xunit output sink.</summary>
    protected ITestOutputHelper Output { get; }

    /// <summary>Value of <c>CLOUDFLARE_API_TOKEN</c>.</summary>
    protected string? ApiToken { get; }

    /// <summary>Value of <c>CLOUDFLARE_GLOBAL_API_KEY</c>.</summary>
    protected string? GlobalApiKey { get; }

    /// <summary>Value of <c>CLOUDFLARE_API_EMAIL</c>.</summary>
    protected string? ApiEmail { get; }

    /// <summary>Value of <c>CLOUDFLARE_ACCOUNT_ID</c>.</summary>
    protected string? AccountId { get; }

    /// <summary>Value of <c>CLOUDFLARE_ZONE_ID</c>.</summary>
    protected string? ZoneId { get; }

    /// <summary>Whether destructive tests may run.</summary>
    protected bool WritesEnabled { get; }

    /// <summary>True when some usable credential scheme is present.</summary>
    protected bool HasCredentials =>
        !string.IsNullOrWhiteSpace(ApiToken) ||
        (!string.IsNullOrWhiteSpace(GlobalApiKey) && !string.IsNullOrWhiteSpace(ApiEmail));

    /// <summary>
    /// A client built the same way an application would build it through dependency injection.
    /// </summary>
    protected CloudflareClient Client => _client ??= BuildClient();

    /// <summary>
    /// Ends the test early when the credentials needed for it are absent. xunit v2 has no dynamic
    /// skip, so an unconfigured run reports these as passing no-ops and prints why.
    /// </summary>
    /// <param name="condition">Whether the test can run.</param>
    /// <param name="reason">Shown in the test output when it cannot.</param>
    /// <returns>True when the test should continue.</returns>
    protected bool SkipUnless(bool condition, string reason)
    {
        if (condition)
        {
            return true;
        }

        Output.WriteLine($"SKIPPED: {reason}");
        return false;
    }

    /// <summary>Ends the test early unless credentials are configured.</summary>
    protected bool SkipWithoutCredentials() =>
        SkipUnless(HasCredentials, "no Cloudflare credentials found (set CLOUDFLARE_API_TOKEN, or CLOUDFLARE_GLOBAL_API_KEY and CLOUDFLARE_API_EMAIL).");

    /// <summary>Ends the test early unless an account id is configured.</summary>
    protected bool SkipWithoutAccount() =>
        SkipWithoutCredentials() && SkipUnless(!string.IsNullOrWhiteSpace(AccountId), "CLOUDFLARE_ACCOUNT_ID is not set.");

    /// <summary>Ends the test early unless a zone id is configured.</summary>
    protected bool SkipWithoutZone() =>
        SkipWithoutCredentials() && SkipUnless(!string.IsNullOrWhiteSpace(ZoneId), "CLOUDFLARE_ZONE_ID is not set.");

    /// <summary>Ends the test early unless destructive operations were explicitly allowed.</summary>
    protected bool SkipWithoutWritePermission() =>
        SkipUnless(WritesEnabled, "CLOUDFLARE_IT_ENABLE_WRITES is not 'true'.");

    /// <summary>A unique suffix so parallel or repeated runs never collide.</summary>
    protected static string UniqueSuffix(string prefix) =>
        $"{prefix}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}";

    /// <summary>Logs a Cloudflare call and its outcome, so a failing run shows the CF-RAY to quote.</summary>
    protected void Log(string message) => Output.WriteLine(message);

    private CloudflareClient BuildClient()
    {
        // Going through the container proves the DI registration works against the real API too.
        _provider = new ServiceCollection()
            .AddLogging()
            .AddCloudflareFromEnvironment(options =>
            {
                // Keep retries modest: the integration run should fail fast, not wait out an outage.
                options.MaxRetries = 1;
                options.RetryBaseDelay = TimeSpan.FromMilliseconds(250);
                options.Timeout = TimeSpan.FromSeconds(30);
            })
            .BuildServiceProvider();

        return _provider.GetRequiredService<CloudflareClient>();
    }

    private static string? Read(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Disposes the client.</summary>
    /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _client?.Dispose();
            _client = null;
            _provider?.Dispose();
            _provider = null;
        }
    }
}
