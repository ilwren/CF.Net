using System.Runtime.CompilerServices;
using CFNet.Configuration;
using CFNet.Http;
using CFNet.Models.DurableObjects;

namespace CFNet.Services;

/// <summary>
/// Durable Object namespaces and the objects inside them.
/// </summary>
/// <remarks>
/// Both endpoints are read-only: a namespace comes into existence when a script that exports a Durable Object
/// class is deployed, and objects are created when a script asks for one, so neither can be made from here. The
/// namespace list is account-scoped, so a client with no <see cref="CloudflareClientOptions.AccountId"/> can only
/// call it with an explicit account id.
/// </remarks>
public interface IDurableObjectsService
{
    /// <summary>
    /// Lists one page of the account's Durable Object namespaces.
    /// </summary>
    /// <param name="parameters">Page and per-page, or <see langword="null"/> for Cloudflare's defaults.</param>
    /// <param name="accountId">Account id, or <see langword="null"/> for the configured one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The namespaces on that page.</returns>
    Task<CloudflarePage<DurableObjectNamespace>> ListNamespacesAsync(
        DurableObjectNamespaceListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Walks every page of the account's Durable Object namespaces, newest page first, as Cloudflare publishes
    /// them.
    /// </summary>
    /// <param name="accountId">Account id, or <see langword="null"/> for the configured one.</param>
    /// <param name="pageSize">How many namespaces to ask for per page; <see langword="null"/> for Cloudflare's default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async sequence of every namespace.</returns>
    IAsyncEnumerable<DurableObjectNamespace> ListAllNamespacesAsync(
        string? accountId = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists one page of the objects in a namespace. This endpoint pages by cursor rather than page number, and
    /// sizes a page with <c>limit</c>.
    /// </summary>
    /// <param name="namespaceId">The namespace to list.</param>
    /// <param name="parameters">Cursor and limit, or <see langword="null"/> for the first page.</param>
    /// <param name="accountId">Account id, or <see langword="null"/> for the configured one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The objects on that page.</returns>
    Task<CloudflarePage<DurableObject>> ListObjectsAsync(
        string namespaceId,
        DurableObjectListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Walks every page of the objects in a namespace, following the cursors Cloudflare answers with.
    /// </summary>
    /// <param name="namespaceId">The namespace to list.</param>
    /// <param name="pageSize">How many objects to ask for per page; <see langword="null"/> for Cloudflare's default.</param>
    /// <param name="accountId">Account id, or <see langword="null"/> for the configured one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async sequence of every object.</returns>
    IAsyncEnumerable<DurableObject> ListAllObjectsAsync(
        string namespaceId,
        int? pageSize = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IDurableObjectsService" />
public sealed class DurableObjectsService : CloudflareService, IDurableObjectsService
{
    private const string Segment = "workers/durable_objects";
    private const int MaxPages = 100_000;

    /// <summary>Builds the service over a transport.</summary>
    /// <param name="transport">The transport that sends requests and reads Cloudflare's envelope.</param>
    public DurableObjectsService(ICloudflareTransport transport)
        : base(transport)
    {
    }

    /// <inheritdoc />
    public Task<CloudflarePage<DurableObjectNamespace>> ListNamespacesAsync(
        DurableObjectNamespaceListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var account = ResolveAccountId(accountId);
        var request = CloudflareRequest.Get($"{AccountPath(account)}/{Segment}/namespaces");
        request.OperationName = "List Durable Object namespaces";
        parameters?.Apply(request.Query);

        return SendPageAsync<DurableObjectNamespace>(request, cancellationToken);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<DurableObjectNamespace> ListAllNamespacesAsync(
        string? accountId = null,
        int? pageSize = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var page = 1;

        while (true)
        {
            var response = await ListNamespacesAsync(
                new DurableObjectNamespaceListParameters { Page = page, PerPage = pageSize },
                accountId,
                cancellationToken).ConfigureAwait(false);

            foreach (var item in response.Items)
            {
                yield return item;
            }

            if (!response.HasMore || response.Items.Count == 0 || page >= MaxPages)
            {
                yield break;
            }

            page++;
        }
    }

    /// <inheritdoc />
    public Task<CloudflarePage<DurableObject>> ListObjectsAsync(
        string namespaceId,
        DurableObjectListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var account = ResolveAccountId(accountId);
        var path = $"{AccountPath(account)}/{Segment}/namespaces" +
                   $"/{CloudflareUrl.EncodeSegment(Require(namespaceId, nameof(namespaceId)))}/objects";

        var request = CloudflareRequest.Get(path);
        request.OperationName = "List Durable Objects";
        parameters?.Apply(request.Query);

        return SendPageAsync<DurableObject>(request, cancellationToken);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<DurableObject> ListAllObjectsAsync(
        string namespaceId,
        int? pageSize = null,
        string? accountId = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var account = ResolveAccountId(accountId);
        var id = Require(namespaceId, nameof(namespaceId));
        var cursor = default(string);
        var seen = 0;

        while (seen < MaxPages)
        {
            var response = await ListObjectsAsync(
                id,
                new DurableObjectListParameters { Cursor = cursor, Limit = pageSize },
                account,
                cancellationToken).ConfigureAwait(false);

            foreach (var item in response.Items)
            {
                yield return item;
            }

            // Cloudflare repeats the cursor it was handed when a walk has run out of objects, so an unchanged
            // cursor means the end - and, if it is ignored, an endless loop.
            var next = response.NextCursor;
            if (next is null || response.Items.Count == 0 || next == cursor)
            {
                yield break;
            }

            cursor = next;
            seen++;
        }
    }

    private async Task<CloudflarePage<T>> SendPageAsync<T>(CloudflareRequest request, CancellationToken cancellationToken)
    {
        var response = await Transport.SendAsync<List<T>>(request, cancellationToken).ConfigureAwait(false);
        return response.ToPage();
    }
}
