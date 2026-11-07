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

    /// <summary>DNSSEC for a zone, and the Zone Signing Keys behind it.</summary>
    IDnssecService Dnssec { get; }

    /// <summary>SSL certificate packs inside a zone.</summary>
    ISslService Ssl { get; }

    /// <summary>SSL for SaaS: custom hostnames that CNAME into a zone.</summary>
    ICustomHostnamesService CustomHostnames { get; }

    /// <summary>Mutual TLS towards the origin: client certificates and the hostnames that present them.</summary>
    IOriginTlsClientAuthService OriginTlsClientAuth { get; }

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
    /// Account Lists: the named sets of IPs, hostnames, ASNs and redirect entries that rules match
    /// against, plus the bulk operations that change their items.
    /// </summary>
    IRulesListService Lists { get; }

    /// <summary>
    /// The ruleset engine: rulesets, the rules inside them, their versions, and the phase entrypoints -
    /// from an account path or a zone one.
    /// </summary>
    IRulesetService Rulesets { get; }

    /// <summary>
    /// Page rules: a zone's URL patterns and what Cloudflare should do differently for the requests that match
    /// them - forwarding one, caching another, turning a feature off for a third.
    /// </summary>
    IPageRulesService PageRules { get; }

    /// <summary>
    /// Origin CA certificates: the certificates Cloudflare's own CA signs for an origin server, from a CSR the
    /// caller generated so that the private key never leaves the origin.
    /// </summary>
    IOriginCaCertificatesService OriginCaCertificates { get; }

    /// <summary>
    /// Workers Observability: the telemetry destinations an account exports Worker logs, traces and metrics
    /// to.
    /// </summary>
    IWorkersObservabilityService WorkersObservability { get; }

    /// <summary>
    /// Workers AI: the model catalogue an account can run inference against - models, tasks, authors, and
    /// each model's own input and output schema.
    /// </summary>
    IWorkersAiService WorkersAi { get; }

    /// <summary>
    /// Turnstile: the account's challenge widgets - their hostnames and mode, their site keys, and the secret
    /// a server verifies a visitor with.
    /// </summary>
    ITurnstileService Turnstile { get; }

    /// <summary>
    /// Cheap connectivity check that never throws: verifies the credentials using whichever scheme is
    /// configured and reports the outcome.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CloudflareConnectionCheck> CheckConnectionAsync(CancellationToken cancellationToken = default);
}
