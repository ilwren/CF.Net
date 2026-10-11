using System.Text.Json;
using CFNet.Configuration;
using CFNet.DependencyInjection;
using CFNet.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace CFNet.IntegrationTests;

/// <summary>
/// Everything an integration test needs: the credentials from the environment or a local file, a
/// ready made client, and a way to opt out of a test without failing it when the credentials are
/// absent.
/// </summary>
/// <remarks>
/// <para><b>Where credentials come from</b>, in precedence order:</para>
/// <list type="number">
///   <item><description>
///     Environment variables - exactly the ones the GitHub Actions workflow injects, so a CI run and
///     a local run are configured the same way:
///     <c>CLOUDFLARE_API_TOKEN</c>, or the legacy pair <c>CLOUDFLARE_GLOBAL_API_KEY</c> +
///     <c>CLOUDFLARE_API_EMAIL</c>, plus <c>CLOUDFLARE_ACCOUNT_ID</c> and <c>CLOUDFLARE_ZONE_ID</c>.
///   </description></item>
///   <item><description>
///     <c>tests/CF.Net.IntegrationTests/credentials.local.json</c>, which is git-ignored, for
///     developers who would rather not export variables in every shell. Environment variables still
///     win, so a CI secret can never be shadowed by a stray local file.
///   </description></item>
/// </list>
/// <para><b>Behaviour switches</b>:</para>
/// <list type="bullet">
///   <item><description><c>CLOUDFLARE_IT_ENABLE_WRITES=true</c> - allow create/delete tests.</description></item>
///   <item><description>
///     <c>CLOUDFLARE_IT_CASSETTE=&lt;name&gt;</c> with <c>CLOUDFLARE_IT_RECORD=true</c> - record every
///     call into <c>tests/cassettes/&lt;name&gt;.json</c>, so the offline unit tests can later replay
///     genuine Cloudflare payloads. Credentials are redacted on the way in.
///   </description></item>
/// </list>
/// </remarks>
public abstract class CloudflareIntegrationTest : IDisposable
{
    private CloudflareClient? _client;
    private ServiceProvider? _provider;
    private readonly Dictionary<string, string?> _localFile;
    private readonly Func<string, string?> _environment;

    protected CloudflareIntegrationTest(ITestOutputHelper output)
    {
        Output = output;
        _localFile = LocalCredentials.Load();
        _environment = Lookup;

        ApiToken = Lookup(CloudflareDefaults.EnvironmentVariables.ApiToken);
        GlobalApiKey = Lookup(CloudflareDefaults.EnvironmentVariables.GlobalApiKey);
        ApiEmail = Lookup(CloudflareDefaults.EnvironmentVariables.ApiEmail);
        AccountId = Lookup(CloudflareDefaults.EnvironmentVariables.AccountId);
        ZoneId = Lookup(CloudflareDefaults.EnvironmentVariables.ZoneId);
        WritesEnabled = IsTrue(Lookup("CLOUDFLARE_IT_ENABLE_WRITES"));
        CredentialSource = _localFile.Count > 0 && Environment.GetEnvironmentVariable(CloudflareDefaults.EnvironmentVariables.ApiToken) is null
            ? "credentials.local.json"
            : "environment";
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

    /// <summary>Where the credentials in this run came from; printed by the tests that use them.</summary>
    protected string CredentialSource { get; }

    /// <summary>True when some usable credential scheme is present.</summary>
    protected bool HasCredentials =>
        !string.IsNullOrWhiteSpace(ApiToken) ||
        (!string.IsNullOrWhiteSpace(GlobalApiKey) && !string.IsNullOrWhiteSpace(ApiEmail));

    /// <summary>
    /// A client built the same way an application would build it through dependency injection.
    /// </summary>
    protected CloudflareClient Client => _client ??= BuildClient();

    /// <summary>
    /// The cassette recorder, when this run is recording; <see langword="null"/> otherwise. Tests set
    /// <see cref="CassetteHandler.Label"/> so the recording reads like a transcript.
    /// </summary>
    protected CassetteHandler? Recorder => IntegrationCassette.Handler;

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
        SkipUnless(HasCredentials, "no Cloudflare credentials found (set CLOUDFLARE_API_TOKEN, or CLOUDFLARE_GLOBAL_API_KEY and CLOUDFLARE_API_EMAIL, or put them in tests/CF.Net.IntegrationTests/credentials.local.json).");

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

    /// <summary>Looks one setting up: environment variable first, then the local credentials file.</summary>
    /// <param name="name">The environment variable name, e.g. <c>CLOUDFLARE_API_TOKEN</c>.</param>
    /// <returns>The trimmed value, or <see langword="null"/> when it is not set anywhere.</returns>
    private string? Lookup(string name)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment.Trim();
        }

        return _localFile.TryGetValue(name, out var fromFile) && !string.IsNullOrWhiteSpace(fromFile)
            ? fromFile.Trim()
            : null;
    }

    private static bool IsTrue(string? value) =>
        string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase) || value?.Trim() == "1";

    private CloudflareClient BuildClient()
    {
        // Going through the container proves the DI registration works against the real API too.
        var services = new ServiceCollection().AddLogging();

        Action<CloudflareClientOptions> configure = options =>
        {
            // The lookup merges the environment with credentials.local.json, which the library's own
            // FromEnvironment() cannot know about, so the merge happens here and the result is pushed
            // into the options instance the container hands out.
            CloudflareClientOptions.FromEnvironment(_environment).CopyMissingInto(options);

            // Keep retries modest: the integration run should fail fast, not wait out an outage.
            options.MaxRetries = 1;
            options.RetryBaseDelay = TimeSpan.FromMilliseconds(250);
            options.Timeout = TimeSpan.FromSeconds(30);
        };

        var recorder = IntegrationCassette.Handler;
        if (recorder is not null)
        {
            services.AddCloudflareHttpClient(
                configureOptions: configure,
                configureClient: builder => builder.AddHttpMessageHandler(() => recorder));
        }
        else
        {
            services.AddCloudflare(configure);
        }

        _provider = services.BuildServiceProvider();
        return _provider.GetRequiredService<CloudflareClient>();
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

            // xunit builds one fixture instance per test, so every instance flushes; the flush is
            // idempotent and merges rather than truncates.
            IntegrationCassette.Flush();
        }
    }
}

/// <summary>
/// Reads the optional, git-ignored <c>tests/CF.Net.IntegrationTests/credentials.local.json</c>.
/// </summary>
/// <remarks>
/// Both key spellings are accepted, because people reach for whichever they last saw:
/// <code>
/// {
///   "apiToken":  "v1.0-abc...",          // or "CLOUDFLARE_API_TOKEN"
///   "accountId": "023e105f4ecef8ad...",   // or "CLOUDFLARE_ACCOUNT_ID"
///   "zoneId":    "8d0c8239f88f98a8...",   // or "CLOUDFLARE_ZONE_ID"
///   "enableWrites": false                  // or "CLOUDFLARE_IT_ENABLE_WRITES"
/// }
/// </code>
/// </remarks>
internal static class LocalCredentials
{
    private static readonly Dictionary<string, string> FileKeyToVariable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["apiToken"] = "CLOUDFLARE_API_TOKEN",
        ["globalApiKey"] = "CLOUDFLARE_GLOBAL_API_KEY",
        ["apiEmail"] = "CLOUDFLARE_API_EMAIL",
        ["accountId"] = "CLOUDFLARE_ACCOUNT_ID",
        ["zoneId"] = "CLOUDFLARE_ZONE_ID",
        ["baseAddress"] = "CLOUDFLARE_BASE_ADDRESS",
        ["enableWrites"] = "CLOUDFLARE_IT_ENABLE_WRITES",
        ["cassette"] = "CLOUDFLARE_IT_CASSETTE",
        ["record"] = "CLOUDFLARE_IT_RECORD",
    };

    /// <summary>Loads the file, keyed by environment variable name.</summary>
    /// <returns>An empty dictionary when the file is absent or unreadable.</returns>
    public static Dictionary<string, string?> Load()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);

        string path;
        try
        {
            path = RepoPaths.LocalCredentialsFile;
        }
        catch (DirectoryNotFoundException)
        {
            return values;
        }

        if (!File.Exists(path))
        {
            return values;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));

            foreach (var property in document.RootElement.EnumerateObject())
            {
                // Keys starting with an underscore are comments; the template uses one to explain
                // itself, and JSON has no comment syntax.
                if (property.Name.StartsWith('_', StringComparison.Ordinal))
                {
                    continue;
                }

                if (!FileKeyToVariable.TryGetValue(property.Name, out var variable))
                {
                    // Already an environment-style key, for example "CLOUDFLARE_API_TOKEN".
                    variable = property.Name;
                }

                values[variable] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Number => property.Value.GetRawText(),
                    _ => null,
                };
            }
        }
        catch (JsonException)
        {
            // A malformed local file must not turn every integration test into a crash: the tests
            // will skip and say the credentials are missing.
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }
        catch (IOException)
        {
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }
        catch (UnauthorizedAccessException)
        {
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }

        return values;
    }
}

/// <summary>
/// Owns the one cassette a recording run writes to. Static on purpose: xunit creates a fresh test
/// instance per test, and a cassette is only useful if the whole run lands in one file.
/// </summary>
internal static class IntegrationCassette
{
    private static readonly object Gate = new();
    private static CassetteHandler? _handler;
    private static Cassette? _cassette;
    private static string? _name;
    private static bool _flushHooked;

    /// <summary>The shared recorder, or <see langword="null"/> when this run is not recording.</summary>
    public static CassetteHandler? Handler
    {
        get
        {
            lock (Gate)
            {
                return _handler ??= TryCreate();
            }
        }
    }

    private static CassetteHandler? TryCreate()
    {
        var name = Read("CLOUDFLARE_IT_CASSETTE");
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var mode = CassetteHandler.ParseMode(Read("CLOUDFLARE_IT_RECORD"));
        if (mode == CassetteMode.Passthrough)
        {
            // Naming a cassette is the intent; make it record rather than silently doing nothing.
            mode = CassetteMode.Record;
        }

        if (mode != CassetteMode.Record)
        {
            return null;
        }

        _name = name.Trim();
        _cassette = new Cassette(_name, CloudflareDefaults.BaseAddress);
        _cassette.LibraryVersion = typeof(CloudflareClient).Assembly.GetName().Version?.ToString();

        if (!_flushHooked)
        {
            _flushHooked = true;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => FlushCore();
        }

        return new CassetteHandler(_cassette, CassetteMode.Record);
    }

    /// <summary>Merges what has been recorded so far into the cassette file.</summary>
    public static void Flush()
    {
        lock (Gate)
        {
            FlushCore();
        }
    }

    private static void FlushCore()
    {
        if (_cassette is null || _handler is null || _name is null || _handler.RecordedCount == 0)
        {
            return;
        }

        try
        {
            var path = RepoPaths.CassettePath(_name);
            var recorded = _cassette.Exchanges;

            // Merge instead of truncate: a filtered run should add to a cassette, not replace it.
            if (File.Exists(path))
            {
                var existing = Cassette.Load(path);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var exchange in existing.Exchanges)
                {
                    seen.Add(Signature(exchange));
                }

                foreach (var exchange in recorded)
                {
                    if (seen.Add(Signature(exchange)))
                    {
                        existing.Exchanges.Add(exchange);
                    }
                }

                existing.RecordedAt = _cassette.RecordedAt;
                existing.BaseUrl = _cassette.BaseUrl;
                existing.Save(path);
            }
            else
            {
                _cassette.Save(path);
            }
        }
        catch (IOException)
        {
            // Losing a cassette must never fail a test run that was talking to the real API.
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (InvalidDataException)
        {
        }
    }

    private static string Signature(RecordedExchange exchange) =>
        string.Join("|", exchange.MatchKey, exchange.StatusCode, exchange.ResponseBodyBase64 ?? exchange.ResponseBody ?? string.Empty);

    private static string? Read(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
