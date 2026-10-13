using CFNet.Configuration;
using CFNet.Http;
using CFNet.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CFNet.DependencyInjection;

/// <summary>
/// <c>IServiceCollection</c> extensions for registering CF.Net.
/// </summary>
/// <example>
/// <code>
/// // 1. Plain registration - CF.Net creates and owns its own HttpClient.
/// builder.Services.AddCloudflare(options =>
/// {
///     options.ApiToken = builder.Configuration["Cloudflare:ApiToken"];
///     options.AccountId = builder.Configuration["Cloudflare:AccountId"];
/// });
///
/// // 2. Bind straight from configuration.
/// builder.Services.AddCloudflareFromConfiguration(builder.Configuration);
///
/// // 3. Use IHttpClientFactory so handler lifetimes and Polly-style policies can be configured.
/// builder.Services.AddCloudflareHttpClient(
///     options => options.ApiToken = token,
///     configureClient: client => client.AddTransientHttpErrorPolicy(...));
/// </code>
/// </example>
public static class ServiceCollectionCloudflareExtensions
{
    /// <summary>Name of the named <see cref="HttpClient"/> used by <see cref="AddCloudflareHttpClient"/>.</summary>
    public const string HttpClientName = "cloudflare";

    /// <summary>
    /// Registers CF.Net with a callback-based configuration. The transport creates and owns a dedicated
    /// <see cref="HttpClient"/> with pooled handlers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional callback that mutates the default options.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddCloudflare(
        this IServiceCollection services,
        Action<CloudflareClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        RegisterCore(services);

        services.TryAddSingleton<ICloudflareTransport>(provider =>
            new CloudflareTransport(
                provider.GetRequiredService<IOptions<CloudflareClientOptions>>().Value,
                provider.GetService<ILoggerFactory>()));

        return services;
    }

    /// <summary>Registers CF.Net from an already built options instance.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">The options to use. Copied into the options pipeline.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddCloudflare(this IServiceCollection services, CloudflareClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        return services.AddCloudflare(target => CopyInto(options, target));
    }

    /// <summary>
    /// Binds CF.Net from a configuration section. By default the section is named <c>Cloudflare</c>, so
    /// an <c>appsettings.json</c> like this is enough:
    /// <code>
    /// {
    ///   "Cloudflare": {
    ///     "ApiToken":   "from user-secrets or an environment variable",
    ///     "AccountId":  "023e105f4ecef8ad9ca31a8372d0c353",
    ///     "ZoneId":     "8d0c8239f88f98a8cb82ec7bb29b8556",
    ///     "MaxRetries": 3
    ///   }
    /// }
    /// </code>
    /// Environment variables override the file automatically when the host adds them, which is how the
    /// GitHub Actions workflow injects secrets without writing them to disk.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The root configuration.</param>
    /// <param name="sectionName">Section to bind; defaults to <c>Cloudflare</c>.</param>
    /// <param name="configure">Optional callback applied after binding, for values that must not live in configuration.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddCloudflareFromConfiguration(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = CloudflareDefaults.ConfigurationSectionName,
        Action<CloudflareClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (string.IsNullOrWhiteSpace(sectionName))
        {
            sectionName = CloudflareDefaults.ConfigurationSectionName;
        }

        services.AddOptions();
        services.Configure<CloudflareClientOptions>(configuration.GetSection(sectionName));

        if (configure is not null)
        {
            services.Configure(configure);
        }

        RegisterCore(services);

        services.TryAddSingleton<ICloudflareTransport>(provider =>
            new CloudflareTransport(
                provider.GetRequiredService<IOptions<CloudflareClientOptions>>().Value,
                provider.GetService<ILoggerFactory>()));

        return services;
    }

    /// <summary>
    /// Registers CF.Net from environment variables
    /// (<c>CLOUDFLARE_API_TOKEN</c>, <c>CLOUDFLARE_ACCOUNT_ID</c>, <c>CLOUDFLARE_ZONE_ID</c>, ...).
    /// This is the registration the integration tests and the CI workflow use.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional callback applied after reading the environment.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddCloudflareFromEnvironment(
        this IServiceCollection services,
        Action<CloudflareClientOptions>? configure = null)
    {
        return services.AddCloudflare(options =>
        {
            // Merge rather than overwrite: anything already configured (appsettings.json, an earlier
            // Configure call) keeps winning, and the environment only fills the gaps.
            CloudflareClientOptions.FromEnvironment().CopyMissingInto(options);

            configure?.Invoke(options);
        });
    }

    /// <summary>
    /// Registers CF.Net on top of <c>IHttpClientFactory</c>, so the <see cref="HttpClient"/> pipeline
    /// can be configured further (Polly policies, extra handlers, proxies, logging).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional callback that mutates the default options.</param>
    /// <param name="httpClientName">Name of the named client to register.</param>
    /// <param name="configureClient">Optional callback to configure the <see cref="IHttpClientBuilder"/>.</param>
    /// <returns>The <see cref="IHttpClientBuilder"/>, for chaining further HTTP configuration.</returns>
    public static IHttpClientBuilder AddCloudflareHttpClient(
        this IServiceCollection services,
        Action<CloudflareClientOptions>? configureOptions = null,
        string httpClientName = HttpClientName,
        Action<IHttpClientBuilder>? configureClient = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (string.IsNullOrWhiteSpace(httpClientName))
        {
            httpClientName = HttpClientName;
        }

        services.AddOptions();
        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }

        RegisterCore(services);

        var builder = services.AddHttpClient(httpClientName);

        builder.ConfigureHttpClient((provider, httpClient) =>
        {
            var options = provider.GetRequiredService<IOptions<CloudflareClientOptions>>().Value;

            // The transport builds absolute URIs itself; BaseAddress is set for anyone who resolves
            // the named client directly, and the timeout is shared so both agree.
            httpClient.BaseAddress = options.BaseAddress;
            if (options.Timeout > TimeSpan.Zero)
            {
                httpClient.Timeout = options.Timeout;
            }
        });

        configureClient?.Invoke(builder);

        var name = httpClientName;
        services.TryAddSingleton<ICloudflareTransport>(provider =>
            new CloudflareTransport(
                provider.GetRequiredService<IHttpClientFactory>().CreateClient(name),
                provider.GetRequiredService<IOptions<CloudflareClientOptions>>().Value,
                provider.GetService<ILoggerFactory>()));

        return builder;
    }

    /// <summary>
    /// Registers only the factory, leaving the rest of the container untouched. Use this when an
    /// application manages several Cloudflare identities and wants to create clients on demand.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional callback that mutates the default (template) options.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddCloudflareClientFactory(
        this IServiceCollection services,
        Action<CloudflareClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<ICloudflareClientFactory>(provider =>
            new CloudflareClientFactory(
                provider.GetRequiredService<IOptions<CloudflareClientOptions>>().Value,
                provider.GetService<ILoggerFactory>(),
                provider.GetService<IHttpClientFactory>() is { } factory
                    ? () => factory.CreateClient(HttpClientName)
                    : null));

        return services;
    }

    /// <summary>
    /// Registers the client, the factory and every product service. Each service resolves to the same
    /// singleton the client exposes, so injecting <c>IZonesService</c> and <c>ICloudflareClient.Zones</c>
    /// gives you the identical object.
    /// </summary>
    private static void RegisterCore(IServiceCollection services)
    {
        services.TryAddSingleton<CloudflareClient>(provider =>
            new CloudflareClient(provider.GetRequiredService<ICloudflareTransport>()));

        services.TryAddSingleton<ICloudflareClient>(provider =>
            provider.GetRequiredService<CloudflareClient>());

        services.TryAddSingleton<ICloudflareClientFactory>(provider =>
            new CloudflareClientFactory(
                provider.GetRequiredService<IOptions<CloudflareClientOptions>>().Value,
                provider.GetService<ILoggerFactory>(),
                provider.GetService<IHttpClientFactory>() is { } factory
                    ? () => factory.CreateClient(HttpClientName)
                    : null));

        services.TryAddSingleton<IUserService>(provider => provider.GetRequiredService<ICloudflareClient>().User);
        services.TryAddSingleton<IAccountsService>(provider => provider.GetRequiredService<ICloudflareClient>().Accounts);
        services.TryAddSingleton<IZonesService>(provider => provider.GetRequiredService<ICloudflareClient>().Zones);
        services.TryAddSingleton<IDnsRecordsService>(provider => provider.GetRequiredService<ICloudflareClient>().DnsRecords);
        services.TryAddSingleton<IDnssecService>(provider => provider.GetRequiredService<ICloudflareClient>().Dnssec);
        services.TryAddSingleton<ISslService>(provider => provider.GetRequiredService<ICloudflareClient>().Ssl);
        services.TryAddSingleton<ICustomHostnamesService>(provider => provider.GetRequiredService<ICloudflareClient>().CustomHostnames);
        services.TryAddSingleton<IOriginTlsClientAuthService>(provider => provider.GetRequiredService<ICloudflareClient>().OriginTlsClientAuth);
        services.TryAddSingleton<IWorkersScriptsService>(provider => provider.GetRequiredService<ICloudflareClient>().Workers);
        services.TryAddSingleton<IPagesProjectsService>(provider => provider.GetRequiredService<ICloudflareClient>().Pages);
        services.TryAddSingleton<ID1DatabaseService>(provider => provider.GetRequiredService<ICloudflareClient>().D1);
        services.TryAddSingleton<IKvService>(provider => provider.GetRequiredService<ICloudflareClient>().Kv);
        services.TryAddSingleton<IR2Service>(provider => provider.GetRequiredService<ICloudflareClient>().R2);
    }

    /// <summary>
    /// Copies every setting of <paramref name="source"/> onto <paramref name="target"/>. Used so an
    /// options instance can be fed into the <c>IOptions</c> pipeline without losing members.
    /// </summary>
    private static void CopyInto(CloudflareClientOptions source, CloudflareClientOptions target) =>
        source.CopyTo(target);
}
