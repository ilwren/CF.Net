using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Models.Workers;

/// <summary>
/// A custom domain that routes traffic to a Worker, as <c>/accounts/{account_id}/workers/domains</c>
/// reports it.
/// </summary>
/// <remarks>
/// This is the "Custom Domain" of the dashboard: a hostname in one of the account's zones, with a
/// certificate Cloudflare issues for it, pointing at a Worker by name. It is not the
/// <c>workers.dev</c> subdomain, which <c>GetSubdomainAsync</c> handles.
/// </remarks>
public sealed class WorkerDomain
{
    /// <summary>Immutable id of the domain attachment.</summary>
    public string? Id { get; set; }

    /// <summary>Id of the TLS certificate Cloudflare issued for this hostname.</summary>
    public string? CertId { get; set; }

    /// <summary>
    /// Whether the hostname itself routes to the Worker. When false, preview routing has to be on and
    /// only subdomains of the hostname route.
    /// </summary>
    public bool? Enabled { get; set; }

    /// <summary>
    /// Worker environment the domain belongs to. Cloudflare marks this member deprecated.
    /// </summary>
    public string? Environment { get; set; }

    /// <summary>The hostname, either a zone apex or a subdomain of one.</summary>
    public string? Hostname { get; set; }

    /// <summary>
    /// Whether previews route as subdomains of <see cref="Hostname"/>: with <c>app.example.com</c> and
    /// previews on, <c>my-feature.app.example.com</c> reaches the preview with that slug.
    /// </summary>
    public bool? PreviewsEnabled { get; set; }

    /// <summary>Name of the Worker this hostname routes to.</summary>
    public string? Service { get; set; }

    /// <summary>Id of the zone the hostname lives in.</summary>
    public string? ZoneId { get; set; }

    /// <summary>Name of the zone the hostname lives in.</summary>
    public string? ZoneName { get; set; }

    /// <summary>Members Cloudflare added after this library was released.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>
/// Body of <c>PUT /accounts/{account_id}/workers/domains</c>, which attaches a hostname to a Worker.
/// </summary>
/// <remarks>
/// The call is a PUT to the collection rather than a POST, which is how Cloudflare spells "attach".
/// Needs the <c>Workers Scripts Write</c> permission, and the zone the hostname belongs to has to be in
/// the same account.
/// </remarks>
public sealed class AttachWorkerDomainRequest
{
    /// <summary>Hostname to attach: a zone apex or a subdomain of one.</summary>
    public string? Hostname { get; set; }

    /// <summary>Name of the Worker to route the hostname to.</summary>
    public string? Service { get; set; }

    /// <summary>Whether the hostname itself routes. Defaults to on when left unset.</summary>
    public bool? Enabled { get; set; }

    /// <summary>Whether previews route as subdomains of <see cref="Hostname"/>.</summary>
    public bool? PreviewsEnabled { get; set; }

    /// <summary>Zone the hostname lives in, when naming it by id.</summary>
    public string? ZoneId { get; set; }

    /// <summary>Zone the hostname lives in, when naming it by name.</summary>
    public string? ZoneName { get; set; }

    /// <summary>Builds the common case: route a hostname to a Worker, in a zone named by id.</summary>
    /// <param name="hostname">Hostname to attach.</param>
    /// <param name="service">Worker name.</param>
    /// <param name="zoneId">Zone the hostname lives in.</param>
    /// <returns>A request ready to send.</returns>
    public static AttachWorkerDomainRequest For(string hostname, string service, string? zoneId = null) =>
        new() { Hostname = hostname, Service = service, ZoneId = zoneId };

    /// <summary>
    /// Rejects a request Cloudflare would refuse: no hostname, no Worker, or neither routing mode on -
    /// Cloudflare requires hostname routing when preview routing is off, and preview routing when
    /// hostname routing is off.
    /// </summary>
    /// <exception cref="Exceptions.CloudflareConfigurationException">The request cannot be sent as it is.</exception>
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Hostname))
        {
            throw new Exceptions.CloudflareConfigurationException(
                $"'{nameof(Hostname)}' is required to attach a Worker domain.");
        }

        if (string.IsNullOrWhiteSpace(Service))
        {
            throw new Exceptions.CloudflareConfigurationException(
                $"'{nameof(Service)}' is required: it names the Worker the hostname routes to.");
        }

        if (Enabled == false && PreviewsEnabled != true)
        {
            throw new Exceptions.CloudflareConfigurationException(
                "With hostname routing off, preview routing has to be on: Cloudflare refuses a domain that routes nothing.");
        }

        if (PreviewsEnabled == false && Enabled != true)
        {
            throw new Exceptions.CloudflareConfigurationException(
                "With preview routing off, hostname routing has to be on: Cloudflare refuses a domain that routes nothing.");
        }
    }
}
