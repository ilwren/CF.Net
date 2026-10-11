namespace CFNet.Configuration;

/// <summary>
/// All knobs that control how <c>CF.Net</c> talks to the Cloudflare REST API.
/// Bind it from configuration, mutate it in an <c>Action&lt;CloudflareClientOptions&gt;</c>,
/// or construct it by hand - whatever fits the host application.
/// </summary>
public sealed class CloudflareClientOptions
{
    /// <summary>
    /// Root address of the API. Override it to point at a recording proxy or a mock server
    /// (the unit tests do exactly that).
    /// </summary>
    public Uri BaseAddress { get; set; } = new(CloudflareDefaults.BaseAddress, UriKind.Absolute);

    /// <summary>Scoped API token. Preferred over <see cref="GlobalApiKey"/>.</summary>
    public string? ApiToken { get; set; }

    /// <summary>Legacy Global API key. Must be combined with <see cref="ApiEmail"/>.</summary>
    public string? GlobalApiKey { get; set; }

    /// <summary>Account e-mail, required when using <see cref="GlobalApiKey"/>.</summary>
    public string? ApiEmail { get; set; }

    /// <summary>
    /// Default account id. Services use it whenever a call does not pass one explicitly, and
    /// <see cref="Validate"/> rejects account scoped operations when it is missing.
    /// </summary>
    public string? AccountId { get; set; }

    /// <summary>Default zone id used when a call omits it.</summary>
    public string? ZoneId { get; set; }

    /// <summary>Per-request timeout applied to the underlying <see cref="System.Net.Http.HttpClient"/>.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);

    /// <summary>Value sent as the <c>User-Agent</c> header.</summary>
    public string UserAgent { get; set; } = CloudflareDefaults.UserAgent;

    /// <summary>
    /// Number of automatic retries for transient failures (HTTP 429, 5xx, socket errors).
    /// Set to <c>0</c> to disable - the unit tests do so to stay deterministic.
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Base delay of the exponential back-off used between retries.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Upper bound for a single back-off delay.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Adds up to 25% of random jitter to every back-off delay to avoid thundering herds.</summary>
    public bool UseRetryJitter { get; set; } = true;

    /// <summary>
    /// When <c>true</c> (default) a response with <c>success == false</c> or a non 2xx status code
    /// is surfaced as a <see cref="Exceptions.CloudflareApiException"/>. Set it to <c>false</c> to
    /// inspect <see cref="Http.CloudflareResponse{T}"/> yourself.
    /// </summary>
    public bool ThrowOnApiError { get; set; } = true;

    /// <summary>
    /// Optional hook invoked for every outgoing request, after authentication headers were applied.
    /// Handy for tracing, extra headers or a corporate proxy token.
    /// </summary>
    public Action<System.Net.Http.HttpRequestMessage>? ConfigureRequest { get; set; }

    /// <summary>
    /// Optional hook invoked for every completed response, before it is parsed.
    /// </summary>
    public Action<System.Net.Http.HttpResponseMessage>? InspectResponse { get; set; }

    /// <summary>
    /// Replaces the JSON serializer options. When <c>null</c> the library uses
    /// <see cref="Serialization.CloudflareJson.Default"/>.
    /// </summary>
    public System.Text.Json.JsonSerializerOptions? JsonSerializerOptions { get; set; }

    /// <summary>Resolves the credential scheme implied by the current settings.</summary>
    public CloudflareAuthenticationMode AuthenticationMode
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ApiToken))
            {
                return CloudflareAuthenticationMode.ApiToken;
            }

            return !string.IsNullOrWhiteSpace(GlobalApiKey) && !string.IsNullOrWhiteSpace(ApiEmail)
                ? CloudflareAuthenticationMode.GlobalApiKey
                : CloudflareAuthenticationMode.None;
        }
    }

    /// <summary>True when at least one complete credential pair is present.</summary>
    public bool HasCredentials => AuthenticationMode != CloudflareAuthenticationMode.None;

    /// <summary>
    /// Produces a copy that can be modified without affecting the instance registered in DI.
    /// </summary>
    public CloudflareClientOptions Clone()
    {
        return new CloudflareClientOptions
        {
            BaseAddress = BaseAddress,
            ApiToken = ApiToken,
            GlobalApiKey = GlobalApiKey,
            ApiEmail = ApiEmail,
            AccountId = AccountId,
            ZoneId = ZoneId,
            Timeout = Timeout,
            UserAgent = UserAgent,
            MaxRetries = MaxRetries,
            RetryBaseDelay = RetryBaseDelay,
            MaxRetryDelay = MaxRetryDelay,
            UseRetryJitter = UseRetryJitter,
            ThrowOnApiError = ThrowOnApiError,
            ConfigureRequest = ConfigureRequest,
            InspectResponse = InspectResponse,
            JsonSerializerOptions = JsonSerializerOptions,
        };
    }

    /// <summary>
    /// Builds options from environment variables. This is exactly what the integration tests and
    /// the GitHub Actions workflow use, so a locally exported variable behaves like a CI secret.
    /// </summary>
    /// <param name="environment">Optional lookup function; defaults to <see cref="Environment.GetEnvironmentVariable(string)"/>.</param>
    public static CloudflareClientOptions FromEnvironment(Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;

        var options = new CloudflareClientOptions
        {
            ApiToken = NullIfEmpty(environment(CloudflareDefaults.EnvironmentVariables.ApiToken)),
            GlobalApiKey = NullIfEmpty(environment(CloudflareDefaults.EnvironmentVariables.GlobalApiKey)),
            ApiEmail = NullIfEmpty(environment(CloudflareDefaults.EnvironmentVariables.ApiEmail)),
            AccountId = NullIfEmpty(environment(CloudflareDefaults.EnvironmentVariables.AccountId)),
            ZoneId = NullIfEmpty(environment(CloudflareDefaults.EnvironmentVariables.ZoneId)),
        };

        var baseAddress = NullIfEmpty(environment(CloudflareDefaults.EnvironmentVariables.BaseAddress));
        if (baseAddress is not null && Uri.TryCreate(baseAddress, UriKind.Absolute, out var parsed))
        {
            options.BaseAddress = parsed;
        }

        return options;
    }

    /// <summary>
    /// Validates the options, throwing an <see cref="Exceptions.CloudflareConfigurationException"/>
    /// that spells out exactly what is missing.
    /// </summary>
    /// <param name="requireCredentials">Whether credentials are mandatory.</param>
    /// <param name="requireAccountId">Whether <see cref="AccountId"/> is mandatory.</param>
    /// <param name="requireZoneId">Whether <see cref="ZoneId"/> is mandatory.</param>
    public void Validate(bool requireCredentials = true, bool requireAccountId = false, bool requireZoneId = false)
    {
        var errors = new List<string>();

        if (requireCredentials && !HasCredentials)
        {
            errors.Add(
                $"No Cloudflare credentials configured. Set either '{CloudflareDefaults.EnvironmentVariables.ApiToken}' " +
                $"or both '{CloudflareDefaults.EnvironmentVariables.GlobalApiKey}' and '{CloudflareDefaults.EnvironmentVariables.ApiEmail}'.");
        }

        if (!string.IsNullOrWhiteSpace(GlobalApiKey) && string.IsNullOrWhiteSpace(ApiEmail))
        {
            errors.Add($"'{nameof(GlobalApiKey)}' requires '{nameof(ApiEmail)}' to be set as well.");
        }

        if (BaseAddress is null || !BaseAddress.IsAbsoluteUri)
        {
            errors.Add($"{nameof(BaseAddress)} must be an absolute URI.");
        }

        if (Timeout <= TimeSpan.Zero)
        {
            errors.Add($"{nameof(Timeout)} must be greater than zero.");
        }

        if (MaxRetries < 0)
        {
            errors.Add($"{nameof(MaxRetries)} must not be negative.");
        }

        if (RetryBaseDelay < TimeSpan.Zero)
        {
            errors.Add($"{nameof(RetryBaseDelay)} must not be negative.");
        }

        if (requireAccountId && string.IsNullOrWhiteSpace(AccountId))
        {
            errors.Add($"'{nameof(AccountId)}' is required for this operation.");
        }

        if (requireZoneId && string.IsNullOrWhiteSpace(ZoneId))
        {
            errors.Add($"'{nameof(ZoneId)}' is required for this operation.");
        }

        if (errors.Count > 0)
        {
            throw new Exceptions.CloudflareConfigurationException(errors);
        }
    }

    /// <summary>Returns <see cref="AccountId"/> or throws a descriptive configuration exception.</summary>
    public string RequireAccountId(string? accountId = null)
    {
        var value = string.IsNullOrWhiteSpace(accountId) ? AccountId : accountId;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new Exceptions.CloudflareConfigurationException(
                $"No Cloudflare account id available. Pass one explicitly, set {nameof(CloudflareClientOptions.AccountId)}, " +
                $"or export {CloudflareDefaults.EnvironmentVariables.AccountId}.");
        }

        return value!;
    }

    /// <summary>Returns <see cref="ZoneId"/> or throws a descriptive configuration exception.</summary>
    public string RequireZoneId(string? zoneId = null)
    {
        var value = string.IsNullOrWhiteSpace(zoneId) ? ZoneId : zoneId;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new Exceptions.CloudflareConfigurationException(
                $"No Cloudflare zone id available. Pass one explicitly, set {nameof(CloudflareClientOptions.ZoneId)}, " +
                $"or export {CloudflareDefaults.EnvironmentVariables.ZoneId}.");
        }

        return value!;
    }

    internal static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
