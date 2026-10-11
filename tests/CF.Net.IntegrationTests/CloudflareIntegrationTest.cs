using System.Runtime.CompilerServices;
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
///     <c>CLOUDFLARE_IT_REPORT=&lt;file&gt;</c> - append one line per skipped test to that file, which
///     is how the workflow builds its summary. Unset means nothing is written anywhere.
///   </description></item>
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

        var apiToken = Lookup(CloudflareDefaults.EnvironmentVariables.ApiToken);
        var globalApiKey = Lookup(CloudflareDefaults.EnvironmentVariables.GlobalApiKey);
        var apiEmail = Lookup(CloudflareDefaults.EnvironmentVariables.ApiEmail);
        var accountId = Lookup(CloudflareDefaults.EnvironmentVariables.AccountId);
        var zoneId = Lookup(CloudflareDefaults.EnvironmentVariables.ZoneId);
        var writes = IsTrue(Lookup("CLOUDFLARE_IT_ENABLE_WRITES"));
        var source = _localFile.Count > 0 && Environment.GetEnvironmentVariable(CloudflareDefaults.EnvironmentVariables.ApiToken) is null
            ? "credentials.local.json"
            : "environment";

        if (IsReplay)
        {
            // A replay authenticates against nothing, so the secret half of the credentials can be
            // invented - which is exactly what lets the suite run with no credentials configured at
            // all. The account and zone ids cannot be invented: they are part of the recorded URLs
            // the match key is built from, so when they were not exported they are recovered from the
            // recording itself.
            var cassette = IntegrationCassette.ReplayCassette;
            apiToken ??= ReplayApiToken;
            accountId = FirstSet(accountId, CassetteContext.DeriveAccountId(cassette));
            zoneId = FirstSet(zoneId, CassetteContext.DeriveZoneId(cassette));

            // A create/delete lifecycle cannot be replayed faithfully: the recorded response belongs
            // to the names the recording happened to use, not to the ones this run would generate.
            writes = false;
            source = $"cassette replay ({cassette?.Name ?? "unnamed"})";
        }

        ApiToken = apiToken;
        GlobalApiKey = globalApiKey;
        ApiEmail = apiEmail;
        AccountId = accountId;
        ZoneId = zoneId;
        WritesEnabled = writes;
        CredentialSource = source;
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
    /// <param name="test">Filled in by the compiler; used to label the run report.</param>
    /// <returns>True when the test should continue.</returns>
    protected bool SkipUnless(bool condition, string reason, [CallerMemberName] string? test = null)
    {
        if (condition)
        {
            return true;
        }

        Output.WriteLine($"SKIPPED: {reason}");

        // Also hand the reason to the run report, so the CI summary can say what did not happen
        // instead of only that everything passed.
        RunReport.Record(RunReportOutcome.Skipped, $"{GetType().Name}.{test ?? "unknown"}", reason);
        return false;
    }

    /// <summary>Ends the test early unless credentials are configured.</summary>
    /// <param name="test">Filled in by the compiler; used to label the run report.</param>
    protected bool SkipWithoutCredentials([CallerMemberName] string? test = null) =>
        SkipUnless(
            HasCredentials,
            "no Cloudflare credentials found (set CLOUDFLARE_API_TOKEN, or CLOUDFLARE_GLOBAL_API_KEY and CLOUDFLARE_API_EMAIL, or put them in tests/CF.Net.IntegrationTests/credentials.local.json).",
            test);

    /// <summary>Ends the test early unless an account id is configured.</summary>
    /// <param name="test">Filled in by the compiler; used to label the run report.</param>
    protected bool SkipWithoutAccount([CallerMemberName] string? test = null) =>
        SkipWithoutCredentials(test) &&
        SkipUnless(!string.IsNullOrWhiteSpace(AccountId), "CLOUDFLARE_ACCOUNT_ID is not set.", test);

    /// <summary>Ends the test early unless a zone id is configured.</summary>
    /// <param name="test">Filled in by the compiler; used to label the run report.</param>
    protected bool SkipWithoutZone([CallerMemberName] string? test = null) =>
        SkipWithoutCredentials(test) &&
        SkipUnless(!string.IsNullOrWhiteSpace(ZoneId), "CLOUDFLARE_ZONE_ID is not set.", test);

    /// <summary>Ends the test early unless destructive operations were explicitly allowed.</summary>
    /// <param name="test">Filled in by the compiler; used to label the run report.</param>
    protected bool SkipWithoutWritePermission([CallerMemberName] string? test = null) =>
        SkipUnless(
            WritesEnabled,
            IsReplay
                ? "this run replays a cassette, and a create/delete lifecycle cannot be replayed faithfully: the recorded responses belong to the names the recording used."
                : "CLOUDFLARE_IT_ENABLE_WRITES is not 'true'.",
            test);

    /// <summary>A unique suffix so parallel or repeated runs never collide.</summary>
    protected static string UniqueSuffix(string prefix) =>
        $"{prefix}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}";

    /// <summary>Logs a Cloudflare call and its outcome, so a failing run shows the CF-RAY to quote.</summary>
    protected void Log(string message) => Output.WriteLine(message);

    /// <summary>True when this run answers from a recorded cassette instead of calling Cloudflare.</summary>
    protected bool IsReplay => IntegrationCassette.Mode == CassetteMode.Replay;

    /// <summary>
    /// The stand-in token used while replaying. Nothing ever validates it: the cassette handler
    /// answers every call before a socket could be opened.
    /// </summary>
    private const string ReplayApiToken = "replay-only-not-a-credential";

    /// <summary>Prefers a value that was configured over one recovered from a cassette.</summary>
    /// <param name="preferred">What the environment or the local file supplied.</param>
    /// <param name="fallback">What the recording implies.</param>
    private static string? FirstSet(string? preferred, string? fallback) =>
        string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;

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

            // Every response also lands in the run report, which makes the CI summary an audit trail
            // of what this run did to the account - and of what a cassette captured. Only the method,
            // path, query and status are written; credentials live in headers, never here.
            options.InspectResponse += response =>
                RunReport.Record(
                    RunReportOutcome.Info,
                    $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.PathAndQuery}",
                    $"{(int)response.StatusCode} {response.ReasonPhrase}");
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
                if (property.Name.Length > 0 && property.Name[0] == '_')
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
    private static CassetteMode _mode = CassetteMode.Passthrough;

    /// <summary>
    /// The shared handler: a recorder when this run is recording, a replayer when it is answering from
    /// a committed cassette, and <see langword="null"/> when it should talk to Cloudflare directly.
    /// </summary>
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

    /// <summary>
    /// What this run is doing with a cassette. Reading it initialises the shared handler, so the
    /// fixture can ask before it has built a client.
    /// </summary>
    public static CassetteMode Mode
    {
        get
        {
            lock (Gate)
            {
                _ = _handler ??= TryCreate();
                return _mode;
            }
        }
    }

    /// <summary>The cassette being replayed, or <see langword="null"/> when this run is not replaying.</summary>
    public static Cassette? ReplayCassette => Mode == CassetteMode.Replay ? _cassette : null;

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

        _name = name.Trim();

        if (mode == CassetteMode.Replay)
        {
            // Answer from the recording and never open a socket. A cassette that is missing or empty
            // still installs the handler, so every call fails with CassetteMissException rather than
            // quietly falling back to the live API.
            _cassette = LoadOrEmpty(_name);
            _mode = CassetteMode.Replay;
            return CassetteHandler.Replaying(_cassette);
        }

        _cassette = new Cassette(_name, CloudflareDefaults.BaseAddress);
        _cassette.LibraryVersion = typeof(CloudflareClient).Assembly.GetName().Version?.ToString();

        if (!_flushHooked)
        {
            _flushHooked = true;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => FlushCore();
        }

        _mode = CassetteMode.Record;
        return new CassetteHandler(_cassette, CassetteMode.Record);
    }

    /// <summary>
    /// Loads the cassette to replay, falling back to an empty one. An empty cassette answers nothing,
    /// so a wrong name surfaces as <see cref="CassetteMissException"/> on the first call.
    /// </summary>
    private static Cassette LoadOrEmpty(string name)
    {
        var path = RepoPaths.CassettePath(name);
        return File.Exists(path) ? Cassette.Load(path) : new Cassette(name, CloudflareDefaults.BaseAddress);
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
