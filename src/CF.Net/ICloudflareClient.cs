using CFNet.Configuration;
using CFNet.Http;
using CFNet.Services;

namespace CFNet;

/// <summary>
/// Entry point of the library. Exposes one property per Cloudflare product area, each of which is a
/// strongly typed service.
/// </summary>
/// <remarks>
/// Implements <see cref="IDisposable"/> because a client created directly
/// (<c>CloudflareClient.Create(...)</c>, <c>new CloudflareClient(options)</c>) owns the transport and
/// the <see cref="HttpClient"/> inside it. A client resolved from a DI container is owned by the
/// container instead, so disposing it yourself is harmless but unnecessary.
/// </remarks>
/// <example>
/// <code>
/// var client = CloudflareClient.Create(options =>
/// {
///     options.ApiToken = Environment.GetEnvironmentVariable("CLOUDFLARE_API_TOKEN");
///     options.AccountId = Environment.GetEnvironmentVariable("CLOUDFLARE_ACCOUNT_ID");
/// });
///
/// await foreach (var zone in client.Zones.ListAllAsync())
/// {
///     Console.WriteLine(zone.Name);
/// }
/// </code>
/// </example>
public interface ICloudflareClient : IDisposable
{
    /// <summary>The options this client was created with.</summary>
    CloudflareClientOptions Options { get; }

    /// <summary>The transport executing requests. Replace it in tests or to add cross-cutting behaviour.</summary>
    ICloudflareTransport Transport { get; }

    /// <summary>Credential verification and the authenticated user.</summary>
    IUserService User { get; }

    /// <summary>Account discovery.</summary>
    IAccountsService Accounts { get; }

    /// <summary>Zones (domains), cache purge and activation checks.</summary>
    IZonesService Zones { get; }

    /// <summary>DNS records inside a zone.</summary>
    IDnsRecordsService DnsRecords { get; }

    /// <summary>SSL certificate packs inside a zone.</summary>
    ISslService Ssl { get; }

    /// <summary>SSL for SaaS: custom hostnames that CNAME into a zone.</summary>
    ICustomHostnamesService CustomHostnames { get; }

    /// <summary>Workers scripts, deployments and the <c>workers.dev</c> subdomain.</summary>
    IWorkersScriptsService Workers { get; }

    /// <summary>Pages projects, deployments and custom domains.</summary>
    IPagesProjectsService Pages { get; }

    /// <summary>D1 databases, queries and backups.</summary>
    ID1DatabaseService D1 { get; }

    /// <summary>Workers KV namespaces, keys and values.</summary>
    IKvService Kv { get; }

    /// <summary>R2 buckets.</summary>
    IR2Service R2 { get; }

    /// <summary>
    /// Cheap connectivity check that never throws: verifies the credentials using whichever scheme is
    /// configured and reports the outcome.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CloudflareConnectionCheck> CheckConnectionAsync(CancellationToken cancellationToken = default);
}
