using CFNet.Http;
using CFNet.Models.Workers;

namespace CFNet.Services;

/// <summary>
/// Workers Observability: the telemetry destinations an account exports Worker logs, traces and metrics to.
/// </summary>
/// <remarks>
/// <para>
/// These are account-level resources under <c>/accounts/{account_id}/workers/observability/</c>, which is why
/// they are their own service rather than more methods on the one that wraps scripts. A Worker is attached to
/// a destination through its own script settings, where
/// <c>WorkerObservabilityLogs.Destinations</c> and <c>WorkerObservabilityTraces.Destinations</c> carry the
/// slugs defined here.
/// </para>
/// <para>
/// The rest of Workers Observability - querying the telemetry itself, and its live tail - lives under the
/// same path and is not wrapped yet.
/// </para>
/// </remarks>
public interface IWorkersObservabilityService
{
    /// <summary>Creates a telemetry destination.</summary>
    /// <param name="request">The destination to create.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The destination, with the slug later calls address it by.</returns>
    /// <remarks>
    /// Cloudflare checks that the destination can be delivered to before creating it, unless the request says
    /// to skip that preflight. Which telemetry a destination carries - logs, traces or metrics - is fixed
    /// here, because an update cannot change it.
    /// </remarks>
    Task<ObservabilityDestination> CreateDestinationAsync(
        CreateObservabilityDestinationRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Changes a telemetry destination.</summary>
    /// <param name="slug">The destination's slug.</param>
    /// <param name="request">What to change. The enabled flag is required on every update.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The destination as Cloudflare now holds it.</returns>
    /// <remarks>
    /// <c>PATCH /accounts/{account_id}/workers/observability/destinations/{slug}</c>. The published schema
    /// requires <c>enabled</c> even when it is not the field being changed, so an update to the address or
    /// the headers sends the enabled flag it already has along with them.
    /// </remarks>
    Task<ObservabilityDestination> UpdateDestinationAsync(
        string slug,
        UpdateObservabilityDestinationRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the account's telemetry destinations.</summary>
    /// <param name="parameters">Ordering and paging filters, or null for the defaults.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The destinations, empty when the account has none.</returns>
    /// <remarks>
    /// A single page rather than a cursor: the filters are <c>order</c>, <c>orderBy</c>, <c>page</c> and
    /// <c>perPage</c>, and the last two are camelCase on the wire here where most of Cloudflare's endpoints
    /// spell them <c>page</c> and <c>per_page</c>.
    /// </remarks>
    Task<IReadOnlyList<ObservabilityDestination>> ListDestinationsAsync(
        ObservabilityDestinationListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a telemetry destination.</summary>
    /// <param name="slug">The destination's slug.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The destination as it was when Cloudflare deleted it.</returns>
    /// <remarks>
    /// This endpoint answers with the deleted destination rather than an empty result, so what was removed is
    /// readable from the call that removed it. Workers exporting to it stop doing so; deleting a destination
    /// does not delete the telemetry already delivered through it.
    /// </remarks>
    Task<ObservabilityDestination> DeleteDestinationAsync(
        string slug,
        string? accountId = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class WorkersObservabilityService : CloudflareService, IWorkersObservabilityService
{
    /// <summary>Creates the service.</summary>
    /// <param name="transport">The transport that executes requests.</param>
    public WorkersObservabilityService(ICloudflareTransport transport)
        : base(transport)
    {
    }

    /// <inheritdoc />
    public async Task<ObservabilityDestination> CreateDestinationAsync(
        CreateObservabilityDestinationRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();

        var send = CloudflareRequest.Post(DestinationsPath(ResolveAccountId(accountId)), request);
        send.OperationName = "Create Observability Destination";

        var response = await Transport
            .SendAsync<ObservabilityDestination>(send, cancellationToken)
            .ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<ObservabilityDestination> UpdateDestinationAsync(
        string slug,
        UpdateObservabilityDestinationRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();

        var send = CloudflareRequest.Patch(DestinationPath(ResolveAccountId(accountId), slug), request);
        send.OperationName = "Update Observability Destination";

        var response = await Transport
            .SendAsync<ObservabilityDestination>(send, cancellationToken)
            .ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ObservabilityDestination>> ListDestinationsAsync(
        ObservabilityDestinationListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        parameters?.EnsureValid();

        var send = Apply(CloudflareRequest.Get(DestinationsPath(ResolveAccountId(accountId))), parameters);
        send.OperationName = "List Observability Destinations";

        var response = await Transport
            .SendAsync<List<ObservabilityDestination>>(send, cancellationToken)
            .ConfigureAwait(false);
        return response.Result ?? new List<ObservabilityDestination>();
    }

    /// <inheritdoc />
    public async Task<ObservabilityDestination> DeleteDestinationAsync(
        string slug,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var send = CloudflareRequest.Delete(DestinationPath(ResolveAccountId(accountId), slug));
        send.OperationName = "Delete Observability Destination";

        // The answer is the destination that was deleted, not an empty success.
        var response = await Transport
            .SendAsync<ObservabilityDestination>(send, cancellationToken)
            .ConfigureAwait(false);
        return response.EnsureResult();
    }

    private static string DestinationsPath(string accountId) =>
        $"{AccountPath(accountId)}/workers/observability/destinations";

    private static string DestinationPath(string accountId, string slug) =>
        $"{DestinationsPath(accountId)}/{CloudflareUrl.EncodeSegment(Require(slug, nameof(slug)))}";
}
