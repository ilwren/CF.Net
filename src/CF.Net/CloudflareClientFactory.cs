using CFNet.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CFNet;

/// <summary>
/// Creates short-lived <see cref="ICloudflareClient"/> instances from a shared set of defaults.
/// </summary>
/// <remarks>
/// The single-tenant <c>AddCloudflare</c> registration resolves its options once, which does not suit
/// applications that switch credentials at runtime - a CLI that takes a token as an argument, or the
/// Avalonia gallery in this repository, where the user pastes a token into a form. Inject this factory
/// instead and create a client per set of credentials.
/// </remarks>
public interface ICloudflareClientFactory
{
    /// <summary>
    /// Creates a client whose options start from the factory defaults and are then adjusted by
    /// <paramref name="configure"/>. The returned client owns its <see cref="System.Net.Http.HttpClient"/>
    /// and must be disposed.
    /// </summary>
    /// <param name="configure">Optional callback mutating a copy of the default options.</param>
    ICloudflareClient Create(Action<CloudflareClientOptions>? configure = null);

    /// <summary>Creates a client from environment variables, on top of the factory defaults.</summary>
    /// <param name="configure">Optional callback mutating a copy of the resulting options.</param>
    ICloudflareClient CreateFromEnvironment(Action<CloudflareClientOptions>? configure = null);
}

/// <inheritdoc />
public sealed class CloudflareClientFactory : ICloudflareClientFactory
{
    private readonly CloudflareClientOptions _defaults;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Func<HttpClient>? _httpClientFactory;

    /// <summary>Creates a factory.</summary>
    /// <param name="defaults">
    /// Template options. They are copied per client, so mutating the instance afterwards has no effect
    /// on clients that were already created.
    /// </param>
    /// <param name="loggerFactory">Optional logger factory shared by every created client.</param>
    /// <param name="httpClientFactory">
    /// Optional source of <see cref="HttpClient"/> instances. Supply
    /// <c>IHttpClientFactory.CreateClient</c> to let a host application manage handler lifetimes and
    /// socket exhaustion. When <c>null</c>, each client builds and owns its own client.
    /// </param>
    public CloudflareClientFactory(
        CloudflareClientOptions? defaults = null,
        ILoggerFactory? loggerFactory = null,
        Func<HttpClient>? httpClientFactory = null)
    {
        _defaults = (defaults ?? new CloudflareClientOptions()).Clone();
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc />
    public ICloudflareClient Create(Action<CloudflareClientOptions>? configure = null)
    {
        var options = Build(configure);
        return CreateClient(options);
    }

    /// <inheritdoc />
    public ICloudflareClient CreateFromEnvironment(Action<CloudflareClientOptions>? configure = null)
    {
        var options = CloudflareClientOptions.FromEnvironment();

        // Keep the transport tuning from the defaults but let the environment win on credentials.
        options.BaseAddress = _defaults.BaseAddress;
        options.Timeout = _defaults.Timeout;
        options.UserAgent = _defaults.UserAgent;
        options.MaxRetries = _defaults.MaxRetries;
        options.RetryBaseDelay = _defaults.RetryBaseDelay;
        options.MaxRetryDelay = _defaults.MaxRetryDelay;
        options.UseRetryJitter = _defaults.UseRetryJitter;
        options.ThrowOnApiError = _defaults.ThrowOnApiError;
        options.ConfigureRequest = _defaults.ConfigureRequest;
        options.InspectResponse = _defaults.InspectResponse;
        options.JsonSerializerOptions = _defaults.JsonSerializerOptions;

        options.AccountId ??= _defaults.AccountId;
        options.ZoneId ??= _defaults.ZoneId;

        configure?.Invoke(options);
        options.Validate();

        return CreateClient(options);
    }

    private CloudflareClientOptions Build(Action<CloudflareClientOptions>? configure)
    {
        var options = _defaults.Clone();
        configure?.Invoke(options);
        options.Validate();
        return options;
    }

    private ICloudflareClient CreateClient(CloudflareClientOptions options)
    {
        return _httpClientFactory is null
            ? new CloudflareClient(options, _loggerFactory)
            : new CloudflareClient(_httpClientFactory(), options, _loggerFactory);
    }
}
