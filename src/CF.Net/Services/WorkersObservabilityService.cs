using CFNet.Http;
using CFNet.Models.Workers;

namespace CFNet.Services;

/// <summary>
/// Workers Observability: the telemetry destinations an account exports Worker logs, traces and metrics to,
/// and the telemetry itself - the keys and values it holds, queries over them, and a live tail of what
/// arrives.
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
/// The telemetry itself lives under the same path, in a <c>telemetry</c> sub-path of its own: what keys and
/// values it holds, running a query over them, and preparing a live tail of what arrives next.
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

    /// <summary>Lists the attribute keys that have appeared in the account's telemetry.</summary>
    /// <param name="request">Filters, a span, search terms and a limit, or null to ask with no conditions.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The keys, each with the type its values carry and when it was last seen.</returns>
    /// <remarks>
    /// <c>POST /accounts/{account_id}/workers/observability/telemetry/keys</c> - a list that is posted rather
    /// than got, because what narrows it is a filter tree too large for a query string. An empty body asks
    /// for the keys of the default datasets, which is what passing null sends.
    /// </remarks>
    Task<IReadOnlyList<ObservabilityTelemetryKey>> ListTelemetryKeysAsync(
        ObservabilityTelemetryKeysRequest? request = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the values one attribute key has been seen with.</summary>
    /// <param name="request">The key, its type and a timeframe, which are all required, plus any filters.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The values, each with the dataset it was seen in.</returns>
    /// <remarks>
    /// <c>POST /accounts/{account_id}/workers/observability/telemetry/values</c>, posted for the same reason
    /// the key list is. Its datasets are required to be there but may be empty, which the schema documents as
    /// meaning the default ones.
    /// </remarks>
    Task<IReadOnlyList<ObservabilityTelemetryValue>> ListTelemetryValuesAsync(
        ObservabilityTelemetryValuesRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Runs a query over the account's telemetry.</summary>
    /// <param name="request">The query id and the span it covers, plus the view and whatever narrows it.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The run that executed it, what that cost, and the results the view asked for.</returns>
    /// <remarks>
    /// <c>POST /accounts/{account_id}/workers/observability/telemetry/query</c>. One endpoint and six views,
    /// and which fields of the answer are populated moves with the view - so
    /// <c>ObservabilityTelemetryQueryResult</c> carries all six shapes, and the properties whose names start
    /// with <c>All</c> read whichever arrived as one list. Paging is by cursor for the event-shaped views and
    /// by numeric offset for grouped ones. A <c>dry</c> query runs without persisting its results, which is
    /// what validating one before saving it needs.
    /// </remarks>
    Task<ObservabilityTelemetryQueryResult> QueryTelemetryAsync(
        ObservabilityTelemetryQueryRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Prepares a live tail of telemetry and gets the WebSocket address it streams from.</summary>
    /// <param name="request">A script id and any filters, or null to tail everything the account can see.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The prepared tail, with the address to connect to.</returns>
    /// <remarks>
    /// <c>POST /accounts/{account_id}/workers/observability/telemetry/live-tail</c>. Opening the WebSocket is
    /// the caller's part and not this library's; keeping the session alive is
    /// <see cref="SendLiveTailHeartbeatAsync"/>. A session that stops getting heartbeats is dropped.
    /// </remarks>
    Task<ObservabilityLiveTail> PrepareLiveTailAsync(
        ObservabilityLiveTailRequest? request = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Keeps a prepared live tail session alive.</summary>
    /// <param name="scriptId">The Worker the session is tailing, when it was prepared for one.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// <c>POST /accounts/{account_id}/workers/observability/telemetry/live-tail/heartbeat</c>, which answers
    /// with no result of its own: the call either succeeds or the session is gone.
    /// </remarks>
    Task SendLiveTailHeartbeatAsync(
        string? scriptId = null,
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<ObservabilityTelemetryKey>> ListTelemetryKeysAsync(
        ObservabilityTelemetryKeysRequest? request = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        request?.EnsureValid();

        // A list that is posted: an empty object asks with no conditions, which is what null means here.
        var send = CloudflareRequest.Post(
            $"{TelemetryPath(ResolveAccountId(accountId))}/keys",
            request ?? new ObservabilityTelemetryKeysRequest());
        send.OperationName = "List Observability Telemetry Keys";

        var response = await Transport
            .SendAsync<List<ObservabilityTelemetryKey>>(send, cancellationToken)
            .ConfigureAwait(false);
        return response.Result ?? new List<ObservabilityTelemetryKey>();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ObservabilityTelemetryValue>> ListTelemetryValuesAsync(
        ObservabilityTelemetryValuesRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();

        var send = CloudflareRequest.Post($"{TelemetryPath(ResolveAccountId(accountId))}/values", request);
        send.OperationName = "List Observability Telemetry Values";

        var response = await Transport
            .SendAsync<List<ObservabilityTelemetryValue>>(send, cancellationToken)
            .ConfigureAwait(false);
        return response.Result ?? new List<ObservabilityTelemetryValue>();
    }

    /// <inheritdoc />
    public async Task<ObservabilityTelemetryQueryResult> QueryTelemetryAsync(
        ObservabilityTelemetryQueryRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();

        var send = CloudflareRequest.Post($"{TelemetryPath(ResolveAccountId(accountId))}/query", request);
        send.OperationName = "Query Observability Telemetry";

        var response = await Transport
            .SendAsync<ObservabilityTelemetryQueryResult>(send, cancellationToken)
            .ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<ObservabilityLiveTail> PrepareLiveTailAsync(
        ObservabilityLiveTailRequest? request = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        request?.EnsureValid();

        var send = CloudflareRequest.Post(
            $"{TelemetryPath(ResolveAccountId(accountId))}/live-tail",
            request ?? new ObservabilityLiveTailRequest());
        send.OperationName = "Prepare Observability Live Tail";

        var response = await Transport
            .SendAsync<ObservabilityLiveTail>(send, cancellationToken)
            .ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task SendLiveTailHeartbeatAsync(
        string? scriptId = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var send = CloudflareRequest.Post(
            $"{TelemetryPath(ResolveAccountId(accountId))}/live-tail/heartbeat",
            new ObservabilityLiveTailHeartbeatRequest { ScriptId = scriptId });
        send.OperationName = "Observability Live Tail Heartbeat";

        // The published response type is "unknown": there is nothing to read back, only whether it worked.
        await SendVoidAsync(send, cancellationToken).ConfigureAwait(false);
    }

    private static string TelemetryPath(string accountId) =>
        $"{AccountPath(accountId)}/workers/observability/telemetry";

    private static string DestinationsPath(string accountId) =>
        $"{AccountPath(accountId)}/workers/observability/destinations";

    private static string DestinationPath(string accountId, string slug) =>
        $"{DestinationsPath(accountId)}/{CloudflareUrl.EncodeSegment(Require(slug, nameof(slug)))}";
}
