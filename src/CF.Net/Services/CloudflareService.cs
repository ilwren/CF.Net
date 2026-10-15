using System.Text.Json;
using CFNet.Configuration;
using CFNet.Http;

namespace CFNet.Services;

/// <summary>
/// Shared plumbing for every typed service: access to the transport, resolution of the default
/// account/zone id, and helpers for the URL shapes Cloudflare uses.
/// </summary>
public abstract class CloudflareService
{
    /// <summary>Initialises the base service.</summary>
    /// <param name="transport">The transport that executes requests.</param>
    protected CloudflareService(ICloudflareTransport transport)
    {
        Transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    /// <summary>The transport used to execute requests.</summary>
    protected ICloudflareTransport Transport { get; }

    /// <summary>The options the transport was created with.</summary>
    protected CloudflareClientOptions Options => Transport.Options;

    /// <summary>
    /// Resolves the account id for a call: the explicitly supplied value wins, otherwise the default
    /// from the options is used, otherwise a descriptive exception is thrown.
    /// </summary>
    /// <param name="accountId">Account id supplied by the caller, may be <c>null</c>.</param>
    protected string ResolveAccountId(string? accountId) => Options.RequireAccountId(accountId);

    /// <summary>
    /// Resolves the zone id for a call, falling back to the default from the options.
    /// </summary>
    /// <param name="zoneId">Zone id supplied by the caller, may be <c>null</c>.</param>
    protected string ResolveZoneId(string? zoneId) => Options.RequireZoneId(zoneId);

    /// <summary>Builds the <c>accounts/{account_id}</c> path prefix.</summary>
    /// <param name="accountId">The account id.</param>
    protected static string AccountPath(string accountId) =>
        "accounts/" + CloudflareUrl.EncodeSegment(accountId);

    /// <summary>Builds the <c>zones/{zone_id}</c> path prefix.</summary>
    /// <param name="zoneId">The zone id.</param>
    protected static string ZonePath(string zoneId) =>
        "zones/" + CloudflareUrl.EncodeSegment(zoneId);

    /// <summary>
    /// Appends a parameter object's filters to a request, tolerating a <c>null</c> parameter object.
    /// </summary>
    /// <param name="request">The request to modify.</param>
    /// <param name="parameters">The filters, may be <c>null</c>.</param>
    protected static CloudflareRequest Apply(CloudflareRequest request, ICloudflareQueryParameters? parameters)
    {
        parameters?.Apply(request.Query);
        return request;
    }

    /// <summary>
    /// Sends a request whose result is not interesting - most deletes and the KV write endpoints.
    /// Failures still throw, because the envelope is parsed and validated.
    /// </summary>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    protected async Task SendVoidAsync(CloudflareRequest request, CancellationToken cancellationToken)
    {
        await Transport.SendAsync<JsonElement>(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Rejects a null or whitespace argument with a message naming the parameter, so a missing
    /// identifier fails before a request is built rather than as a confusing 404 from Cloudflare.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <param name="parameterName">The parameter name used in the message.</param>
    /// <returns>The trimmed value.</returns>
    protected static string Require(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new Exceptions.CloudflareConfigurationException($"'{parameterName}' must not be null or empty.");
        }

        return value.Trim();
    }
}
