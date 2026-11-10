using CFNet.Http;
using CFNet.Models.Zones;

namespace CFNet.Services;

/// <summary>
/// A zone's URL normalization setting - which normalizer runs on which of the zone's requests, and therefore how
/// URLs look to the rules that match against them.
/// </summary>
/// <remarks>
/// This resource is a single zone-level setting, so all three calls take one zone id and none takes an id of its
/// own. Cloudflare publishes the answer as optional on every field and on the result itself, which is why
/// <see cref="GetAsync"/> can come back <see langword="null"/> and why an update has to be checked by
/// <see cref="UrlNormalizationSettings.EnsureValid"/> before it is sent.
/// </remarks>
public interface IUrlNormalizationService
{
    /// <summary>
    /// Reads the zone's URL normalization setting.
    /// </summary>
    /// <param name="zoneId">Zone id, or <see langword="null"/> for the configured one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The setting, or <see langword="null"/> when Cloudflare answered the call with no result.</returns>
    Task<UrlNormalizationSettings?> GetAsync(string? zoneId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the zone's URL normalization setting. Cloudflare requires both the scope and the type, so an update
    /// never changes one of them on its own.
    /// </summary>
    /// <param name="settings">The scope and the type to set.</param>
    /// <param name="zoneId">Zone id, or <see langword="null"/> for the configured one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The setting Cloudflare answered with, or <see langword="null"/> when it answered with no result.</returns>
    /// <exception cref="CFNet.Exceptions.CloudflareConfigurationException">Thrown when a field is missing or
    /// carries a value this library has not met.</exception>
    Task<UrlNormalizationSettings?> UpdateAsync(
        UrlNormalizationSettings settings,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets the zone's URL normalization setting to Cloudflare's default by deleting it.
    /// </summary>
    /// <param name="zoneId">Zone id, or <see langword="null"/> for the configured one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The setting Cloudflare answered with, or <see langword="null"/> when it answered with no result.</returns>
    Task<UrlNormalizationSettings?> ResetAsync(string? zoneId = null, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IUrlNormalizationService" />
public sealed class UrlNormalizationService : CloudflareService, IUrlNormalizationService
{
    private const string Suffix = "url_normalization";

    /// <summary>Builds the service over a transport.</summary>
    /// <param name="transport">The transport that sends requests and reads Cloudflare's envelope.</param>
    public UrlNormalizationService(ICloudflareTransport transport)
        : base(transport)
    {
    }

    /// <inheritdoc />
    public async Task<UrlNormalizationSettings?> GetAsync(
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get(SettingsPath(zoneId));
        request.OperationName = "Get URL normalization";

        var response = await Transport.SendAsync<UrlNormalizationSettings>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<UrlNormalizationSettings?> UpdateAsync(
        UrlNormalizationSettings settings,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.EnsureValid();

        var request = CloudflareRequest.Put(SettingsPath(zoneId), settings);
        request.OperationName = "Update URL normalization";

        var response = await Transport.SendAsync<UrlNormalizationSettings>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<UrlNormalizationSettings?> ResetAsync(
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Delete(SettingsPath(zoneId));
        request.OperationName = "Reset URL normalization";

        var response = await Transport.SendAsync<UrlNormalizationSettings>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    private string SettingsPath(string? zoneId) => $"{ZonePath(ResolveZoneId(zoneId))}/{Suffix}";
}
