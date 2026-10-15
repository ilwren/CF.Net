using CFNet.Http;
using CFNet.Models.Dns;

namespace CFNet.Services;

/// <summary>
/// DNSSEC for a zone: <c>/zones/{zone_id}/dnssec</c>, plus its Zone Signing Keys at
/// <c>/zones/{zone_id}/dnssec/zsk</c>.
/// </summary>
/// <remarks>
/// <para>
/// Permissions are <c>DNS Read</c> and <c>DNS Write</c> - DNSSEC belongs to the DNS family, not to zone
/// settings, so a token with <c>Zone Settings Write</c> alone cannot turn it on and
/// <see cref="IZonesService"/> does not reach it.
/// </para>
/// <para>
/// <b>Enabling is two phases and the second is not ours.</b> <see cref="EnableAsync"/> flips the desired
/// state; Cloudflare then reports <c>pending</c> until the DS record in <see cref="Dnssec.Ds"/> is published
/// at the registrar, at which point the status becomes <c>active</c>. Nothing in this library can complete
/// that step, and polling <see cref="GetAsync"/> is how a caller learns it happened.
/// </para>
/// <para>
/// <b>Disabling has the same shape in reverse</b>, with its own waiting state: <c>pending-disabled</c> lasts
/// until the registrar drops the DS record. Turning DNSSEC off before the DS record is removed breaks
/// resolution for validating resolvers, so the DS record should be removed first and
/// <see cref="DisableAsync"/> called after it has expired.
/// </para>
/// <para>
/// <see cref="DeleteAsync"/> removes Cloudflare's DNSSEC records without going through the disable dance,
/// and its result is a bare string rather than an object - one of the few places in the API where it is.
/// </para>
/// </remarks>
public interface IDnssecService
{
    /// <summary>Reads a zone's DNSSEC state, including the DS record a registrar needs.</summary>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The state; a zone that has never touched DNSSEC comes back <c>disabled</c> rather than 404.</returns>
    Task<Dnssec> GetAsync(
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes DNSSEC. Every field in the request is optional and only what is set is sent, so the three
    /// signing switches can be changed without touching the enabled state.
    /// </summary>
    /// <param name="request">What to change; validated before anything is sent.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new state, usually <c>pending</c> right after an enable.</returns>
    /// <exception cref="Exceptions.CloudflareConfigurationException">
    /// The request asks for a status Cloudflare only reports, or changes nothing.
    /// </exception>
    Task<Dnssec> UpdateAsync(
        UpdateDnssecRequest request,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables DNSSEC. The zone stays <c>pending</c> until the DS record is published at the registrar.
    /// </summary>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new state.</returns>
    Task<Dnssec> EnableAsync(
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Disables DNSSEC. The zone reports <c>pending-disabled</c> until the registrar drops the DS record.
    /// </summary>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new state.</returns>
    Task<Dnssec> DisableAsync(
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a zone's DNSSEC records, without going through the disable states.</summary>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The response's <c>result</c>, which Cloudflare returns as a plain string rather than an object.
    /// </returns>
    Task<string?> DeleteAsync(
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the Zone Signing Keys DNSSEC uses for the zone.
    /// </summary>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Every key. The endpoint has no paging and no filters.</returns>
    Task<IReadOnlyList<DnssecZoneSigningKey>> ListZoneSigningKeysAsync(
        string? zoneId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Default <see cref="IDnssecService"/> implementation.</summary>
public sealed class DnssecService : CloudflareService, IDnssecService
{
    /// <summary>Creates the service.</summary>
    /// <param name="transport">The transport to execute requests through.</param>
    public DnssecService(ICloudflareTransport transport)
        : base(transport)
    {
    }

    private string RootPath(string? zoneId) => $"{ZonePath(ResolveZoneId(zoneId))}/dnssec";

    /// <inheritdoc />
    public async Task<Dnssec> GetAsync(
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get(RootPath(zoneId));
        request.OperationName = "Get DNSSEC";

        var response = await Transport.SendAsync<Dnssec>(request, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<Dnssec> UpdateAsync(
        UpdateDnssecRequest request,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();

        var patch = CloudflareRequest.Patch(RootPath(zoneId), request);
        patch.OperationName = "Edit DNSSEC";

        var response = await Transport.SendAsync<Dnssec>(patch, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public Task<Dnssec> EnableAsync(
        string? zoneId = null,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(UpdateDnssecRequest.Enable(), zoneId, cancellationToken);

    /// <inheritdoc />
    public Task<Dnssec> DisableAsync(
        string? zoneId = null,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(UpdateDnssecRequest.Disable(), zoneId, cancellationToken);

    /// <inheritdoc />
    public async Task<string?> DeleteAsync(
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Delete(RootPath(zoneId));
        request.OperationName = "Delete DNSSEC Records";

        var response = await Transport.SendAsync<string?>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnssecZoneSigningKey>> ListZoneSigningKeysAsync(
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get($"{RootPath(zoneId)}/zsk");
        request.OperationName = "List DNSSEC ZSKs";

        var response = await Transport.SendAsync<List<DnssecZoneSigningKey>>(request, cancellationToken)
            .ConfigureAwait(false);
        return response.EnsureResult();
    }
}
