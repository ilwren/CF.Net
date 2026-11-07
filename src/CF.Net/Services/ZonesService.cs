using System.Runtime.CompilerServices;
using CFNet.Http;
using CFNet.Models.Common;
using CFNet.Models.Zones;

namespace CFNet.Services;

/// <summary>
/// Operations on zones (domains): <c>/zones</c>, plus cache purge and activation checks.
/// </summary>
public interface IZonesService
{
    /// <summary>Lists zones one page at a time.</summary>
    /// <param name="parameters">Optional filters and paging.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CloudflarePage<Zone>> ListAsync(ZoneListParameters? parameters = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerates every zone the credentials can see, following the page-number pagination
    /// automatically. Use this instead of looping over <see cref="ListAsync"/>.
    /// </summary>
    /// <param name="parameters">Optional filters; <c>Page</c> is ignored.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<Zone> ListAllAsync(ZoneListParameters? parameters = null, CancellationToken cancellationToken = default);

    /// <summary>Fetches one zone by id.</summary>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The zone, or <c>null</c> when it does not exist or the credentials may not see it. Cloudflare
    /// answers 403 for an unknown zone id (code 9109) as well as for one outside the token's scope, so
    /// both come back as <c>null</c>.
    /// </returns>
    Task<Zone?> GetAsync(string? zoneId = null, CancellationToken cancellationToken = default);

    /// <summary>Looks a zone up by domain name.</summary>
    /// <param name="name">Exact domain name, e.g. <c>example.com</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The zone, or <c>null</c> when no zone has that name.</returns>
    Task<Zone?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Adds a domain to an account.</summary>
    /// <param name="request">Zone name and owning account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Zone> CreateAsync(CreateZoneRequest request, CancellationToken cancellationToken = default);

    /// <summary>Adds a domain to the configured default account.</summary>
    /// <param name="name">Domain name.</param>
    /// <param name="type">Zone type; defaults to <c>full</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Zone> CreateAsync(string name, string type = ZoneTypeValues.Full, CancellationToken cancellationToken = default);

    /// <summary>Partially updates a zone: pause state, plan or type.</summary>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="request">The fields to change.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Zone> UpdateAsync(string? zoneId, UpdateZoneRequest request, CancellationToken cancellationToken = default);

    /// <summary>Pauses or unpauses a zone.</summary>
    /// <param name="paused">The new pause state.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Zone> SetPausedAsync(bool paused, string? zoneId = null, CancellationToken cancellationToken = default);

    /// <summary>Deletes a zone. This is destructive and cannot be undone.</summary>
    /// <param name="zoneId">Zone id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(string zoneId, CancellationToken cancellationToken = default);

    /// <summary>Asks Cloudflare to re-check whether the nameservers have been updated.</summary>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ZoneActivationCheckResult> RunActivationCheckAsync(string? zoneId = null, CancellationToken cancellationToken = default);

    /// <summary>Purges cached content.</summary>
    /// <param name="request">What to purge. Validated locally before the call is made.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PurgeCacheResult> PurgeCacheAsync(PurgeCacheRequest request, string? zoneId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every setting a zone has via <c>GET /zones/{zone_id}/settings</c>.
    /// </summary>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One entry per setting, each carrying its id, current value and editability.</returns>
    /// <remarks>
    /// Cloudflare marks this endpoint deprecated in favour of reading one setting at a time, but it is
    /// still the only way to discover which settings a zone has and what shape each value takes -
    /// which settings exist depends on the plan. It is also the cheapest way to snapshot a zone before
    /// changing anything.
    /// </remarks>
    Task<IReadOnlyList<ZoneSetting>> ListSettingsAsync(string? zoneId = null, CancellationToken cancellationToken = default);

    /// <summary>Fetches one setting via <c>GET /zones/{zone_id}/settings/{setting_id}</c>.</summary>
    /// <param name="settingId">Setting id, for example <see cref="ZoneSettingIds.AlwaysOnline"/>.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The setting, or <c>null</c> when the zone has no setting by that id.</returns>
    Task<ZoneSetting?> GetSettingAsync(string settingId, string? zoneId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes one setting via <c>PATCH /zones/{zone_id}/settings/{setting_id}</c>.
    /// </summary>
    /// <param name="settingId">Setting id.</param>
    /// <param name="request">The body: a value, or an enabled flag for the settings that take one.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The setting as Cloudflare reports it after the change.</returns>
    /// <exception cref="Exceptions.CloudflareConfigurationException">The body carries neither a value nor a flag, or both.</exception>
    Task<ZoneSetting?> UpdateSettingAsync(string settingId, UpdateZoneSettingRequest request, string? zoneId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes one setting to a value, the common case of <see cref="UpdateSettingAsync"/>.
    /// </summary>
    /// <param name="settingId">Setting id.</param>
    /// <param name="value">Value to set: a string such as <c>"on"</c>, a number, an object or an array.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The setting as Cloudflare reports it after the change.</returns>
    Task<ZoneSetting?> SetSettingValueAsync(string settingId, object? value, string? zoneId = null, CancellationToken cancellationToken = default);

    /// <summary>Switches a setting on or off, sending the <c>"on"</c>/<c>"off"</c> value Cloudflare expects.</summary>
    /// <param name="settingId">Setting id.</param>
    /// <param name="on">True for <c>"on"</c>, false for <c>"off"</c>.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The setting as Cloudflare reports it after the change.</returns>
    Task<ZoneSetting?> SetSettingOnOffAsync(string settingId, bool on, string? zoneId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes several settings in one call via <c>PATCH /zones/{zone_id}/settings</c>.
    /// </summary>
    /// <param name="changes">The settings to change, each with its id and value.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The settings Cloudflare reports after the change.</returns>
    /// <remarks>
    /// The body is an array of <c>{id, value}</c> objects, not an object keyed by setting id - the shape
    /// is easy to get wrong. Cloudflare marks this endpoint deprecated, and applies it all-or-nothing.
    /// </remarks>
    /// <exception cref="Exceptions.CloudflareConfigurationException">No changes were supplied, or one has no id.</exception>
    Task<IReadOnlyList<ZoneSetting>> UpdateSettingsAsync(IEnumerable<ZoneSettingChange> changes, string? zoneId = null, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class ZonesService : CloudflareService, IZonesService
{
    /// <summary>Creates the service.</summary>
    /// <param name="transport">The transport to execute requests through.</param>
    public ZonesService(ICloudflareTransport transport)
        : base(transport)
    {
    }

    /// <inheritdoc />
    public async Task<CloudflarePage<Zone>> ListAsync(ZoneListParameters? parameters = null, CancellationToken cancellationToken = default)
    {
        var request = Apply(CloudflareRequest.Get("zones"), parameters);
        request.OperationName = "List Zones";

        var response = await Transport.SendAsync<List<Zone>>(request, cancellationToken).ConfigureAwait(false);
        return response.ToPage();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<Zone> ListAllAsync(
        ZoneListParameters? parameters = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var page = 1;
        while (true)
        {
            var effective = new ZoneListParameters
            {
                Name = parameters?.Name,
                Status = parameters?.Status,
                AccountId = parameters?.AccountId,
                Order = parameters?.Order,
                Direction = parameters?.Direction,
                Match = parameters?.Match,
                PerPage = parameters?.PerPage,
                Page = page,
            };

            var result = await ListAsync(effective, cancellationToken).ConfigureAwait(false);
            foreach (var zone in result.Items)
            {
                yield return zone;
            }

            if (!result.HasMore || result.Items.Count == 0)
            {
                yield break;
            }

            page++;
        }
    }

    /// <inheritdoc />
    public async Task<Zone?> GetAsync(string? zoneId = null, CancellationToken cancellationToken = default)
    {
        var id = ResolveZoneId(zoneId);
        // Cloudflare answers an unknown zone id with 403 and code 9109 ("Invalid zone identifier")
        // rather than 404, and a zone the token may not see with 403 and 10000, so both are tolerated:
        // a lookup returns null for anything it cannot show you. Callers that need to tell "no such
        // zone" from "no permission" use ListAsync, which reports the empty result set instead.
        var request = CloudflareRequest.Get($"{ZonePath(id)}")
            .Tolerate(System.Net.HttpStatusCode.NotFound, System.Net.HttpStatusCode.Forbidden);
        request.OperationName = "Get Zone";

        var response = await Transport.SendAsync<Zone>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<Zone?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        Require(name, nameof(name));

        var parameters = new ZoneListParameters { Name = name.Trim(), PerPage = 1 };
        var page = await ListAsync(parameters, cancellationToken).ConfigureAwait(false);

        // The API matches names exactly, but stay defensive: prefer the exact hit.
        return page.Items.FirstOrDefault(z => string.Equals(z.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
               ?? page.Items.FirstOrDefault();
    }

    /// <inheritdoc />
    public Task<Zone> CreateAsync(CreateZoneRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new Exceptions.CloudflareConfigurationException("A zone name is required to create a zone.");
        }

        request.Account ??= new CloudflareIdentifier { Id = ResolveAccountId(null) };
        if (string.IsNullOrWhiteSpace(request.Account.Id))
        {
            request.Account.Id = ResolveAccountId(null);
        }

        var httpRequest = CloudflareRequest.Post("zones", request);
        httpRequest.OperationName = "Create Zone";

        return SendForZoneAsync(httpRequest, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Zone> CreateAsync(string name, string type = ZoneTypeValues.Full, CancellationToken cancellationToken = default)
    {
        Require(name, nameof(name));

        return CreateAsync(
            new CreateZoneRequest(name.Trim(), ResolveAccountId(null), type),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Zone> UpdateAsync(string? zoneId, UpdateZoneRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = ResolveZoneId(zoneId);
        var httpRequest = CloudflareRequest.Patch(ZonePath(id), request);
        httpRequest.OperationName = "Update Zone";

        return await SendForZoneAsync(httpRequest, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Zone> SetPausedAsync(bool paused, string? zoneId = null, CancellationToken cancellationToken = default) =>
        UpdateAsync(zoneId, new UpdateZoneRequest { Paused = paused }, cancellationToken);

    /// <inheritdoc />
    public async Task DeleteAsync(string zoneId, CancellationToken cancellationToken = default)
    {
        var id = Require(zoneId, nameof(zoneId));
        var request = CloudflareRequest.Delete(ZonePath(id));
        request.OperationName = "Delete Zone";

        await SendVoidAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ZoneActivationCheckResult> RunActivationCheckAsync(string? zoneId = null, CancellationToken cancellationToken = default)
    {
        var id = ResolveZoneId(zoneId);
        var request = CloudflareRequest.Post($"{ZonePath(id)}/activation_check");
        request.OperationName = "Run Zone Activation Check";

        var response = await Transport.SendAsync<ZoneActivationCheckResult>(request, cancellationToken).ConfigureAwait(false);
        return response.Result ?? new ZoneActivationCheckResult { Id = id };
    }

    /// <inheritdoc />
    public async Task<PurgeCacheResult> PurgeCacheAsync(
        PurgeCacheRequest request,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();

        var id = ResolveZoneId(zoneId);
        var httpRequest = CloudflareRequest.Post($"{ZonePath(id)}/purge_cache", request);
        httpRequest.OperationName = "Purge Cache";

        var response = await Transport.SendAsync<PurgeCacheResult>(httpRequest, cancellationToken).ConfigureAwait(false);
        return response.Result ?? new PurgeCacheResult();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ZoneSetting>> ListSettingsAsync(string? zoneId = null, CancellationToken cancellationToken = default)
    {
        var id = ResolveZoneId(zoneId);
        var request = CloudflareRequest.Get($"{ZonePath(id)}/settings");
        request.OperationName = "List Zone Settings";

        var response = await Transport.SendAsync<List<ZoneSetting>>(request, cancellationToken).ConfigureAwait(false);
        return response.Result ?? new List<ZoneSetting>();
    }

    /// <inheritdoc />
    public async Task<ZoneSetting?> GetSettingAsync(string settingId, string? zoneId = null, CancellationToken cancellationToken = default)
    {
        var id = ResolveZoneId(zoneId);
        var setting = Require(settingId, nameof(settingId));

        var request = CloudflareRequest.Get($"{ZonePath(id)}/settings/{CloudflareUrl.EncodeSegment(setting)}")
            .Tolerate(System.Net.HttpStatusCode.NotFound);
        request.OperationName = "Get Zone Setting";

        var response = await Transport.SendAsync<ZoneSetting>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public Task<ZoneSetting?> SetSettingValueAsync(string settingId, object? value, string? zoneId = null, CancellationToken cancellationToken = default) =>
        UpdateSettingAsync(settingId, UpdateZoneSettingRequest.OfValue(value), zoneId, cancellationToken);

    /// <inheritdoc />
    public Task<ZoneSetting?> SetSettingOnOffAsync(string settingId, bool on, string? zoneId = null, CancellationToken cancellationToken = default) =>
        UpdateSettingAsync(settingId, UpdateZoneSettingRequest.OfOnOff(on), zoneId, cancellationToken);

    /// <inheritdoc />
    public async Task<ZoneSetting?> UpdateSettingAsync(
        string settingId,
        UpdateZoneSettingRequest request,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();

        var id = ResolveZoneId(zoneId);
        var setting = Require(settingId, nameof(settingId));

        var httpRequest = CloudflareRequest.Patch($"{ZonePath(id)}/settings/{CloudflareUrl.EncodeSegment(setting)}", request);
        httpRequest.OperationName = "Update Zone Setting";

        var response = await Transport.SendAsync<ZoneSetting>(httpRequest, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ZoneSetting>> UpdateSettingsAsync(
        IEnumerable<ZoneSettingChange> changes,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var batch = changes.ToList();
        if (batch.Count == 0)
        {
            throw new Exceptions.CloudflareConfigurationException(
                "A bulk zone setting edit needs at least one change; there is nothing for Cloudflare to apply otherwise.");
        }

        foreach (var change in batch)
        {
            ArgumentNullException.ThrowIfNull(change);
            change.EnsureValid();
        }

        var id = ResolveZoneId(zoneId);
        var request = CloudflareRequest.Patch($"{ZonePath(id)}/settings", batch);
        request.OperationName = "Update Zone Settings";

        var response = await Transport.SendAsync<List<ZoneSetting>>(request, cancellationToken).ConfigureAwait(false);
        return response.Result ?? new List<ZoneSetting>();
    }

    private async Task<Zone> SendForZoneAsync(CloudflareRequest request, CancellationToken cancellationToken)
    {
        var response = await Transport.SendAsync<Zone>(request, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }
}
