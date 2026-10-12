using CFNet.Configuration;
using CFNet.Http;
using CFNet.Services;
using Microsoft.Extensions.Logging;

namespace CFNet;

/// <summary>
/// Default <see cref="ICloudflareClient"/> implementation. Construct it directly when you do not use
/// dependency injection, or register it with <c>services.AddCloudflare(...)</c> when you do.
/// </summary>
public sealed class CloudflareClient : ICloudflareClient, IDisposable
{
    private readonly bool _ownsTransport;
    private bool _disposed;

    /// <summary>Creates a client around an existing transport. Ownership stays with the caller.</summary>
    /// <param name="transport">The transport that executes requests.</param>
    public CloudflareClient(ICloudflareTransport transport)
        : this(transport, ownsTransport: false)
    {
    }

    /// <summary>Creates a client with its own transport and <see cref="HttpClient"/>.</summary>
    /// <param name="options">Client options.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    public CloudflareClient(CloudflareClientOptions options, ILoggerFactory? loggerFactory = null)
        : this(new CloudflareTransport(options, loggerFactory), ownsTransport: true)
    {
    }

    /// <summary>
    /// Creates a client around an externally managed <see cref="HttpClient"/> - the shape
    /// <c>IHttpClientFactory</c> produces. The client is not disposed with this instance.
    /// </summary>
    /// <param name="httpClient">The HTTP client to send through.</param>
    /// <param name="options">Client options.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    public CloudflareClient(HttpClient httpClient, CloudflareClientOptions options, ILoggerFactory? loggerFactory = null)
        : this(new CloudflareTransport(httpClient, options, loggerFactory), ownsTransport: false)
    {
    }

    private CloudflareClient(ICloudflareTransport transport, bool ownsTransport)
    {
        Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _ownsTransport = ownsTransport;

        // The services are stateless wrappers over the transport, so one instance each is enough.
        User = new UserService(transport);
        Accounts = new AccountsService(transport);
        Zones = new ZonesService(transport);
        DnsRecords = new DnsRecordsService(transport);
        Ssl = new SslService(transport);
        CustomHostnames = new CustomHostnamesService(transport);
        Workers = new WorkersScriptsService(transport);
        Pages = new PagesProjectsService(transport);
        D1 = new D1DatabaseService(transport);
        Kv = new KvService(transport);
        R2 = new R2Service(transport);
    }

    /// <inheritdoc />
    public CloudflareClientOptions Options => Transport.Options;

    /// <inheritdoc />
    public ICloudflareTransport Transport { get; }

    /// <inheritdoc />
    public IUserService User { get; }

    /// <inheritdoc />
    public IAccountsService Accounts { get; }

    /// <inheritdoc />
    public IZonesService Zones { get; }

    /// <inheritdoc />
    public IDnsRecordsService DnsRecords { get; }

    /// <inheritdoc />
    public ISslService Ssl { get; }

    /// <inheritdoc />
    public ICustomHostnamesService CustomHostnames { get; }

    /// <inheritdoc />
    public IWorkersScriptsService Workers { get; }

    /// <inheritdoc />
    public IPagesProjectsService Pages { get; }

    /// <inheritdoc />
    public ID1DatabaseService D1 { get; }

    /// <inheritdoc />
    public IKvService Kv { get; }

    /// <inheritdoc />
    public IR2Service R2 { get; }

    /// <inheritdoc />
    public Task<CloudflareConnectionCheck> CheckConnectionAsync(CancellationToken cancellationToken = default) =>
        User.CheckConnectionAsync(cancellationToken);

    /// <summary>
    /// Builds a client from a configuration callback. The most convenient entry point outside of DI.
    /// </summary>
    /// <param name="configure">Mutates the default options.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <returns>A ready to use client. Dispose it when the application shuts down.</returns>
    /// <example>
    /// <code>
    /// using var client = CloudflareClient.Create(options =>
    /// {
    ///     options.ApiToken = token;
    ///     options.AccountId = accountId;
    /// });
    /// </code>
    /// </example>
    public static CloudflareClient Create(Action<CloudflareClientOptions>? configure = null, ILoggerFactory? loggerFactory = null)
    {
        var options = new CloudflareClientOptions();
        configure?.Invoke(options);
        options.Validate();

        return new CloudflareClient(options, loggerFactory);
    }

    /// <summary>
    /// Builds a client from environment variables
    /// (<c>CLOUDFLARE_API_TOKEN</c>, <c>CLOUDFLARE_ACCOUNT_ID</c>, <c>CLOUDFLARE_ZONE_ID</c>, ...).
    /// </summary>
    /// <param name="configure">Optional further tweaks.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    public static CloudflareClient CreateFromEnvironment(Action<CloudflareClientOptions>? configure = null, ILoggerFactory? loggerFactory = null)
    {
        var options = CloudflareClientOptions.FromEnvironment();
        configure?.Invoke(options);
        options.Validate();

        return new CloudflareClient(options, loggerFactory);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_ownsTransport && Transport is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
