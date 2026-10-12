using System.Runtime.CompilerServices;
using CFNet.Http;
using CFNet.Models.Ssl;

namespace CFNet.Services;

/// <summary>
/// SSL for SaaS: the custom hostnames under <c>/zones/{zone_id}/custom_hostnames</c>, which are hostnames
/// on other people's domains that CNAME into this zone and get their certificates from Cloudflare.
/// </summary>
/// <remarks>
/// <para>
/// Reading needs <c>SSL and Certificates Read</c>; creating, editing and deleting need
/// <c>SSL and Certificates Write</c>.
/// </para>
/// <para>
/// The lifecycle is longer than one call, which is why the response is worth reading rather than discarding.
/// Creating a hostname returns the challenge the customer has to answer - a TXT record, an HTTP token or an
/// email - and the certificate only becomes <c>active</c> once they do. With the <c>http</c> method and a
/// CNAME that is not in place yet, <see cref="UpdateAsync"/> has to be called again afterwards to complete
/// validation; <see cref="RetryValidationAsync"/> is that call with the body already built.
/// </para>
/// </remarks>
public interface ICustomHostnamesService
{
    /// <summary>
    /// Lists custom hostnames via <c>GET /zones/{zone_id}/custom_hostnames</c>.
    /// </summary>
    /// <param name="parameters">Optional filters and sorting; the hostname filters exclude each other and <c>id</c>.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One page of hostnames.</returns>
    /// <remarks>Page size runs from 5 to 1000 on this endpoint, wider than almost any other.</remarks>
    Task<CloudflarePage<CustomHostname>> ListAsync(
        CustomHostnameListParameters? parameters = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every custom hostname of a zone, walking the pages for you.</summary>
    /// <param name="parameters">Optional filters and sorting; <c>page</c> is overwritten as the walk advances.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<CustomHostname> ListAllAsync(
        CustomHostnameListParameters? parameters = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches one custom hostname via <c>GET /zones/{zone_id}/custom_hostnames/{custom_hostname_id}</c>.</summary>
    /// <param name="customHostnameId">The hostname id, as the list reports it.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The hostname, or <c>null</c> when the zone has no such hostname.</returns>
    Task<CustomHostname?> GetAsync(
        string customHostnameId,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a custom hostname and asks for a certificate via
    /// <c>POST /zones/{zone_id}/custom_hostnames</c>.
    /// </summary>
    /// <param name="request">The hostname and its certificate configuration; validated locally first.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The hostname as created, including the challenge the customer has to answer.</returns>
    /// <exception cref="Exceptions.CloudflareConfigurationException">The hostname is missing, or a certificate bundle has the wrong number of pairs.</exception>
    Task<CustomHostname> CreateAsync(
        CreateCustomHostnameRequest request,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a custom hostname with a Cloudflare-issued certificate, the common case of <see cref="CreateAsync(CreateCustomHostnameRequest, string?, CancellationToken)"/>.</summary>
    /// <param name="hostname">Hostname to add.</param>
    /// <param name="dcvMethod">Validation method, one of <see cref="CustomHostnameDcvMethods"/>.</param>
    /// <param name="wildcard">Whether the hostname is a wildcard.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The hostname as created.</returns>
    Task<CustomHostname> CreateAsync(
        string hostname,
        string dcvMethod = CustomHostnameDcvMethods.Http,
        bool wildcard = false,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes a custom hostname via <c>PATCH /zones/{zone_id}/custom_hostnames/{custom_hostname_id}</c>.
    /// </summary>
    /// <param name="customHostnameId">The hostname id.</param>
    /// <param name="request">What to change. The hostname itself cannot be renamed.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The hostname as Cloudflare reports it after the change, or <c>null</c> when there is no such hostname.</returns>
    Task<CustomHostname?> UpdateAsync(
        string customHostnameId,
        UpdateCustomHostnameRequest request,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks Cloudflare to try domain control validation again, which is the PATCH endpoint with the body
    /// already built: <see cref="UpdateCustomHostnameRequest.ToRetryValidation"/>.
    /// </summary>
    /// <param name="customHostnameId">The hostname id.</param>
    /// <param name="dcvMethod">The method to validate with, one of <see cref="CustomHostnameDcvMethods"/>.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The hostname, with fresh validation records if the method changed.</returns>
    /// <remarks>
    /// This is the call an <c>http</c> hostname needs once its CNAME finally resolves, and the call that
    /// switches a hostname from one validation method to another.
    /// </remarks>
    Task<CustomHostname?> RetryValidationAsync(
        string customHostnameId,
        string dcvMethod = CustomHostnameDcvMethods.Http,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a custom hostname and any certificate issued for it, via
    /// <c>DELETE /zones/{zone_id}/custom_hostnames/{custom_hostname_id}</c>.
    /// </summary>
    /// <param name="customHostnameId">The hostname id.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>HTTPS for that hostname stops when the certificate goes. There is nothing to undo it with.</remarks>
    Task DeleteAsync(
        string customHostnameId,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the fallback origin via <c>GET /zones/{zone_id}/custom_hostnames/fallback_origin</c>.</summary>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The fallback origin, or <c>null</c> when the zone has none.</returns>
    /// <remarks>
    /// Where traffic goes for a custom hostname with no <c>custom_origin_server</c> of its own. The result
    /// can carry activation <c>errors</c> as plain strings inside a successful response, so reading it back
    /// after setting it is worth the call. Needs <c>SSL and Certificates Read</c>.
    /// </remarks>
    Task<CustomHostnameFallbackOrigin?> GetFallbackOriginAsync(
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Sets the fallback origin via <c>PUT /zones/{zone_id}/custom_hostnames/fallback_origin</c>.</summary>
    /// <param name="request">Body carrying the single origin hostname.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The fallback origin Cloudflare recorded, with its activation status.</returns>
    /// <remarks>
    /// Validates the request and throws <see cref="Exceptions.CloudflareConfigurationException"/> before
    /// anything is sent. There is no merge: a <c>PUT</c> of one origin replaces whatever was there. Needs
    /// <c>SSL and Certificates Write</c>.
    /// </remarks>
    Task<CustomHostnameFallbackOrigin> SetFallbackOriginAsync(
        UpdateCustomHostnameFallbackOriginRequest request,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Sets the fallback origin to one hostname via <c>PUT /zones/{zone_id}/custom_hostnames/fallback_origin</c>.</summary>
    /// <param name="origin">Origin hostname, at most 255 characters.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The fallback origin Cloudflare recorded, with its activation status.</returns>
    /// <remarks>Convenience for a request body that is exactly <c>{"origin": ...}</c>.</remarks>
    Task<CustomHostnameFallbackOrigin> SetFallbackOriginAsync(
        string origin,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the fallback origin via <c>DELETE /zones/{zone_id}/custom_hostnames/fallback_origin</c>.
    /// </summary>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when Cloudflare has accepted the deletion.</returns>
    /// <remarks>
    /// Custom hostnames that relied on it have nowhere to fall back to until one is set again. Needs
    /// <c>SSL and Certificates Write</c>.
    /// </remarks>
    Task DeleteFallbackOriginAsync(
        string? zoneId = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class CustomHostnamesService : CloudflareService, ICustomHostnamesService
{
    /// <summary>Creates the service.</summary>
    /// <param name="transport">The transport to execute requests through.</param>
    public CustomHostnamesService(ICloudflareTransport transport)
        : base(transport)
    {
    }

    /// <inheritdoc />
    public async Task<CloudflarePage<CustomHostname>> ListAsync(
        CustomHostnameListParameters? parameters = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var zone = ResolveZoneId(zoneId);
        var request = Apply(CloudflareRequest.Get($"{ZonePath(zone)}/custom_hostnames"), parameters);
        request.OperationName = "List Custom Hostnames";

        var response = await Transport.SendAsync<List<CustomHostname>>(request, cancellationToken).ConfigureAwait(false);
        return response.ToPage();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<CustomHostname> ListAllAsync(
        CustomHostnameListParameters? parameters = null,
        string? zoneId = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var zone = ResolveZoneId(zoneId);
        var page = 1;

        while (true)
        {
            var result = await ListAsync(WithPage(parameters, page), zone, cancellationToken).ConfigureAwait(false);
            foreach (var hostname in result.Items)
            {
                yield return hostname;
            }

            if (result.Items.Count == 0 || !result.HasMore)
            {
                yield break;
            }

            page++;
        }
    }

    /// <inheritdoc />
    public async Task<CustomHostname?> GetAsync(
        string customHostnameId,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get(HostnamePath(zoneId, customHostnameId))
            .Tolerate(System.Net.HttpStatusCode.NotFound);
        request.OperationName = "Get Custom Hostname";

        var response = await Transport.SendAsync<CustomHostname>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<CustomHostname> CreateAsync(
        CreateCustomHostnameRequest request,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();

        var zone = ResolveZoneId(zoneId);
        var post = CloudflareRequest.Post($"{ZonePath(zone)}/custom_hostnames", request);
        post.OperationName = "Create Custom Hostname";

        var response = await Transport.SendAsync<CustomHostname>(post, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public Task<CustomHostname> CreateAsync(
        string hostname,
        string dcvMethod = CustomHostnameDcvMethods.Http,
        bool wildcard = false,
        string? zoneId = null,
        CancellationToken cancellationToken = default) =>
        CreateAsync(
            CreateCustomHostnameRequest.For(
                Require(hostname, nameof(hostname)),
                CustomHostnameSslRequest.ForValidation(dcvMethod, wildcard)),
            zoneId,
            cancellationToken);

    /// <inheritdoc />
    public async Task<CustomHostname?> UpdateAsync(
        string customHostnameId,
        UpdateCustomHostnameRequest request,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var patch = CloudflareRequest.Patch(HostnamePath(zoneId, customHostnameId), request)
            .Tolerate(System.Net.HttpStatusCode.NotFound);
        patch.OperationName = "Update Custom Hostname";

        var response = await Transport.SendAsync<CustomHostname>(patch, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public Task<CustomHostname?> RetryValidationAsync(
        string customHostnameId,
        string dcvMethod = CustomHostnameDcvMethods.Http,
        string? zoneId = null,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(customHostnameId, UpdateCustomHostnameRequest.ToRetryValidation(dcvMethod), zoneId, cancellationToken);

    /// <inheritdoc />
    public async Task DeleteAsync(
        string customHostnameId,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Delete(HostnamePath(zoneId, customHostnameId));
        request.OperationName = "Delete Custom Hostname";

        await SendVoidAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private string HostnamePath(string? zoneId, string customHostnameId)
    {
        var zone = ResolveZoneId(zoneId);
        var id = Require(customHostnameId, nameof(customHostnameId));
        return $"{ZonePath(zone)}/custom_hostnames/{CloudflareUrl.EncodeSegment(id)}";
    }

    /// <summary>
    /// Copies every filter onto a new parameter object with the page number replaced, so walking the pages
    /// cannot quietly drop a filter the caller asked for.
    /// </summary>
    private static CustomHostnameListParameters WithPage(CustomHostnameListParameters? parameters, int page) =>
        new()
        {
            Id = parameters?.Id,
            CertificateAuthority = parameters?.CertificateAuthority,
            CustomOriginServer = parameters?.CustomOriginServer,
            Direction = parameters?.Direction,
            HostnameContains = parameters?.HostnameContains,
            HostnameExact = parameters?.HostnameExact,
            HostnameStartsWith = parameters?.HostnameStartsWith,
            HostnameStatus = parameters?.HostnameStatus,
            Order = parameters?.Order,
            Ssl = parameters?.Ssl,
            SslStatus = parameters?.SslStatus,
            Wildcard = parameters?.Wildcard,
            PerPage = parameters?.PerPage,
            Page = page,
        };

    /// <inheritdoc />
    public async Task<CustomHostnameFallbackOrigin?> GetFallbackOriginAsync(
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get(FallbackOriginPath(zoneId)).Tolerate(System.Net.HttpStatusCode.NotFound);
        request.OperationName = "Get Custom Hostname Fallback Origin";

        var response = await Transport.SendAsync<CustomHostnameFallbackOrigin>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<CustomHostnameFallbackOrigin> SetFallbackOriginAsync(
        UpdateCustomHostnameFallbackOriginRequest request,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();

        var put = CloudflareRequest.Put(FallbackOriginPath(zoneId), request);
        put.OperationName = "Set Custom Hostname Fallback Origin";

        var response = await Transport.SendAsync<CustomHostnameFallbackOrigin>(put, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public Task<CustomHostnameFallbackOrigin> SetFallbackOriginAsync(
        string origin,
        string? zoneId = null,
        CancellationToken cancellationToken = default) =>
        SetFallbackOriginAsync(
            UpdateCustomHostnameFallbackOriginRequest.For(Require(origin, nameof(origin))),
            zoneId,
            cancellationToken);

    /// <inheritdoc />
    public async Task DeleteFallbackOriginAsync(
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Delete(FallbackOriginPath(zoneId));
        request.OperationName = "Delete Custom Hostname Fallback Origin";

        await SendVoidAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Path of the zone-wide fallback origin, which sits beside the custom hostnames themselves.</summary>
    private string FallbackOriginPath(string? zoneId) =>
        $"{ZonePath(ResolveZoneId(zoneId))}/custom_hostnames/fallback_origin";
}
