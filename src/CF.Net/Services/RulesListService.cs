using System.Net;
using CFNet.Http;
using CFNet.Models.Rules;

namespace CFNet.Services;

/// <summary>
/// Account Lists: <c>/accounts/{account_id}/rules/lists</c> and their items.
/// </summary>
/// <remarks>
/// <para>
/// A list is a named set of values - IPs, hostnames, ASNs, or source/target URL pairs - that rules match
/// against. This service covers the whole lifecycle of one: create it, read it, change its description,
/// delete it, and add, replace, read and remove its items.
/// </para>
/// <para>
/// Item changes are <em>asynchronous</em>: the API accepts them and answers with an operation id instead of
/// the items, and the work happens in the background. <see cref="GetBulkOperationAsync"/> reports on one
/// and <see cref="WaitForBulkOperationAsync"/> polls until it stops, which is what a caller writing items
/// and then reading them back needs - reading immediately after a write can still show the old contents.
/// </para>
/// <para>
/// The ruleset rule that turns a <c>redirect</c> list into actual redirects is a separate resource
/// (<c>/accounts/{account_id}/rulesets</c>), not covered here: this service manages the data, not the rule
/// that consumes it.
/// </para>
/// </remarks>
public interface IRulesListService
{
    /// <summary>Lists every list in the account.</summary>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// <c>GET /accounts/{account_id}/rules/lists</c> answers with all of them at once and no
    /// <c>result_info</c>, so there is nothing to page through and no filters to send.
    /// </remarks>
    Task<IReadOnlyList<RulesList>> ListAsync(
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one list.</summary>
    /// <param name="listId">The list's id.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The list, or <c>null</c> when the account has no list with that id.</returns>
    Task<RulesList?> GetAsync(
        string listId,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a list.</summary>
    /// <param name="request">Its name and kind, and optionally a description.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The list Cloudflare created, with its id.</returns>
    /// <remarks>
    /// The kind is fixed here for good: it decides what the items may hold and cannot be changed later, so
    /// a list of the wrong kind has to be deleted and recreated. Items are added afterwards with
    /// <see cref="CreateItemsAsync"/>. A call with no name or no kind is refused locally rather than sent.
    /// </remarks>
    Task<RulesList> CreateAsync(
        CreateRulesListRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Changes a list's description.</summary>
    /// <param name="listId">The list's id.</param>
    /// <param name="request">The new description.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The list as it now stands.</returns>
    /// <remarks>
    /// The endpoint is a <c>PUT</c> and still only accepts a description: the name and kind are fixed at
    /// creation, so this is the whole of what an update can do.
    /// </remarks>
    Task<RulesList> UpdateAsync(
        string listId,
        UpdateRulesListRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a list and every item in it.</summary>
    /// <param name="listId">The list's id.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The id Cloudflare echoes back, confirming which list went.</returns>
    /// <remarks>
    /// A list a rule still refers to is refused rather than deleted, which is what
    /// <see cref="RulesList.NumReferencingFilters"/> is there to check first.
    /// </remarks>
    Task<string?> DeleteAsync(
        string listId,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one page of a list's items.</summary>
    /// <param name="listId">The list's id.</param>
    /// <param name="parameters">Optional cursor, page size and search phrase.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// This endpoint pages by cursor: pass the previous response's <see cref="CloudflarePage{T}.NextCursor"/>
    /// back in <see cref="RulesListItemListParameters.After"/> to keep going. Items of a list whose kind does
    /// not match the value they carry are not a thing Cloudflare stores, so every item on the page holds the
    /// same one of <see cref="RulesListItem.Ip"/>, <see cref="RulesListItem.Hostname"/>,
    /// <see cref="RulesListItem.Redirect"/> and <see cref="RulesListItem.Asn"/>.
    /// </remarks>
    Task<CloudflarePage<RulesListItem>> ListItemsAsync(
        string listId,
        RulesListItemListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one item.</summary>
    /// <param name="listId">The list's id.</param>
    /// <param name="itemId">The item's id.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The item, or <c>null</c> when the list has no item with that id.</returns>
    Task<RulesListItem?> GetItemAsync(
        string listId,
        string itemId,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Adds items to a list.</summary>
    /// <param name="listId">The list's id.</param>
    /// <param name="items">The items to add. Nothing is sent for an empty sequence.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The receipt, whose operation id says when the additions landed.</returns>
    /// <remarks>
    /// <c>POST …/rules/lists/{list_id}/items</c> sends the items as a bare JSON array - no wrapping object -
    /// and answers with only an operation id, so the items are not visible until the operation completes.
    /// Every item has to carry a value, and they all have to carry the same kind of value as the list; a
    /// call that breaks either rule is refused locally with the offending items named.
    /// </remarks>
    Task<RulesListItemOperation> CreateItemsAsync(
        string listId,
        IEnumerable<CreateRulesListItemRequest> items,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces every item in a list.</summary>
    /// <param name="listId">The list's id.</param>
    /// <param name="items">The items the list should hold afterwards. An empty sequence clears it.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The receipt, whose operation id says when the replacement landed.</returns>
    /// <remarks>
    /// <c>PUT …/rules/lists/{list_id}/items</c> is a whole-set replacement in the same shape as
    /// <see cref="CreateItemsAsync"/>: whatever the list held before is gone afterwards. Sending nothing
    /// empties the list, which is the only way to clear it in one call.
    /// </remarks>
    Task<RulesListItemOperation> ReplaceItemsAsync(
        string listId,
        IEnumerable<CreateRulesListItemRequest> items,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Removes items from a list, by id.</summary>
    /// <param name="listId">The list's id.</param>
    /// <param name="itemIds">The ids of the items to remove. Nothing is sent for an empty sequence.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The receipt, whose operation id says when the removals landed.</returns>
    /// <remarks>
    /// Unlike adding and replacing, which send a bare array, this <c>DELETE</c> carries a body:
    /// <c>{"items": [{"id": "…"}]}</c>. Items are identified by id rather than by value because a value can
    /// appear more than once in a list.
    /// </remarks>
    Task<RulesListItemOperation> DeleteItemsAsync(
        string listId,
        IEnumerable<string> itemIds,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the state of a bulk item operation.</summary>
    /// <param name="operationId">The id an item change answered with.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The operation, or <c>null</c> when Cloudflare does not know it - which happens once an operation has
    /// aged out, not only when the id was never issued.
    /// </returns>
    Task<RulesListBulkOperation?> GetBulkOperationAsync(
        string operationId,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Polls a bulk item operation until it finishes or the time runs out.</summary>
    /// <param name="operationId">The id an item change answered with.</param>
    /// <param name="timeout">How long to keep asking. Defaults to one minute.</param>
    /// <param name="pollInterval">How long to wait between asks. Defaults to one second.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token, which also stops the waiting.</param>
    /// <returns>
    /// The last state read. Check <see cref="RulesListBulkOperation.IsFinished"/> - a call that returns
    /// before the operation finished is not an error, it is the timeout, and the state says how far it got.
    /// </returns>
    /// <remarks>
    /// An operation Cloudflare does not know raises the API error rather than looping, and a failed
    /// operation stops the polling immediately with <see cref="RulesListBulkOperation.Error"/> populated.
    /// </remarks>
    Task<RulesListBulkOperation> WaitForBulkOperationAsync(
        string operationId,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class RulesListService : CloudflareService, IRulesListService
{
    /// <summary>Creates the service.</summary>
    /// <param name="transport">The transport to execute requests through.</param>
    public RulesListService(ICloudflareTransport transport)
        : base(transport)
    {
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RulesList>> ListAsync(
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get($"{AccountPath(ResolveAccountId(accountId))}/rules/lists");
        request.OperationName = "List Account Lists";

        var response = await Transport.SendAsync<List<RulesList>>(request, cancellationToken).ConfigureAwait(false);
        return response.Result ?? new List<RulesList>();
    }

    /// <inheritdoc />
    public async Task<RulesList?> GetAsync(
        string listId,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest
            .Get(ListPath(ResolveAccountId(accountId), listId))
            .Tolerate(HttpStatusCode.NotFound);
        request.OperationName = "Get Account List";

        var response = await Transport.SendAsync<RulesList>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<RulesList> CreateAsync(
        CreateRulesListRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            problems.Add("A list needs a name: rules refer to it by name, so it cannot be blank.");
        }

        if (string.IsNullOrWhiteSpace(request.Kind))
        {
            problems.Add($"A list needs a kind ({RulesListKind.Ip}, {RulesListKind.Redirect}, " +
                $"{RulesListKind.Hostname} or {RulesListKind.Asn}), and it cannot be changed afterwards.");
        }

        if (problems.Count > 0)
        {
            throw new Exceptions.CloudflareConfigurationException(problems);
        }

        var send = CloudflareRequest.Post(
            $"{AccountPath(ResolveAccountId(accountId))}/rules/lists",
            request);
        send.OperationName = "Create Account List";

        var response = await Transport.SendAsync<RulesList>(send, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<RulesList> UpdateAsync(
        string listId,
        UpdateRulesListRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var send = CloudflareRequest.Put(ListPath(ResolveAccountId(accountId), listId), request);
        send.OperationName = "Update Account List";

        var response = await Transport.SendAsync<RulesList>(send, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<string?> DeleteAsync(
        string listId,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Delete(ListPath(ResolveAccountId(accountId), listId));
        request.OperationName = "Delete Account List";

        var response = await Transport.SendAsync<RulesListDeleteResult>(request, cancellationToken).ConfigureAwait(false);
        return response.Result?.Id;
    }

    /// <inheritdoc />
    public async Task<CloudflarePage<RulesListItem>> ListItemsAsync(
        string listId,
        RulesListItemListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get($"{ListPath(ResolveAccountId(accountId), listId)}/items");
        request.OperationName = "List Account List Items";
        parameters?.Apply(request.Query);

        var response = await Transport.SendAsync<List<RulesListItem>>(request, cancellationToken).ConfigureAwait(false);
        return response.ToPage();
    }

    /// <inheritdoc />
    public async Task<RulesListItem?> GetItemAsync(
        string listId,
        string itemId,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var path = $"{ListPath(ResolveAccountId(accountId), listId)}/items/{CloudflareUrl.EncodeSegment(Require(itemId, nameof(itemId)))}";
        var request = CloudflareRequest.Get(path).Tolerate(HttpStatusCode.NotFound);
        request.OperationName = "Get Account List Item";

        var response = await Transport.SendAsync<RulesListItem>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public Task<RulesListItemOperation> CreateItemsAsync(
        string listId,
        IEnumerable<CreateRulesListItemRequest> items,
        string? accountId = null,
        CancellationToken cancellationToken = default) =>
        SendItemsAsync(HttpMethod.Post, listId, items, "Create Account List Items", accountId, cancellationToken);

    /// <inheritdoc />
    public Task<RulesListItemOperation> ReplaceItemsAsync(
        string listId,
        IEnumerable<CreateRulesListItemRequest> items,
        string? accountId = null,
        CancellationToken cancellationToken = default) =>
        SendItemsAsync(HttpMethod.Put, listId, items, "Replace Account List Items", accountId, cancellationToken);

    /// <inheritdoc />
    public async Task<RulesListItemOperation> DeleteItemsAsync(
        string listId,
        IEnumerable<string> itemIds,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        var ids = itemIds
            .Select(id => id?.Trim() ?? string.Empty)
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (ids.Count == 0)
        {
            throw new Exceptions.CloudflareConfigurationException(new[]
            {
                "Deleting items needs at least one item id. Replacing a list's items with an empty set is " +
                "ReplaceItemsAsync, which is the call that clears a list.",
            });
        }

        var path = $"{ListPath(ResolveAccountId(accountId), listId)}/items";
        var request = CloudflareRequest.Delete(path);
        request.OperationName = "Delete Account List Items";
        // A DELETE with a body: Cloudflare takes the items to remove inside one, rather than as query
        // parameters or a bare array.
        request.Body = new JsonRequestBody(new DeleteRulesListItemsRequest
        {
            Items = ids.Select(id => new RulesListItemReference { Id = id }).ToList(),
        });

        var response = await Transport.SendAsync<RulesListItemOperation>(request, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<RulesListBulkOperation?> GetBulkOperationAsync(
        string operationId,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest
            .Get(BulkOperationPath(ResolveAccountId(accountId), operationId))
            .Tolerate(HttpStatusCode.NotFound);
        request.OperationName = "Get Account List Bulk Operation";

        var response = await Transport.SendAsync<RulesListBulkOperation>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<RulesListBulkOperation> WaitForBulkOperationAsync(
        string operationId,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var limit = timeout is { } given && given > TimeSpan.Zero ? given : TimeSpan.FromMinutes(1);
        var interval = pollInterval is { } wait && wait > TimeSpan.Zero ? wait : TimeSpan.FromSeconds(1);
        var account = ResolveAccountId(accountId);
        var name = Require(operationId, nameof(operationId));

        var operation = await ReadBulkOperationAsync(account, name, cancellationToken).ConfigureAwait(false);
        var deadline = DateTime.UtcNow + limit;

        while (!operation.IsFinished && DateTime.UtcNow < deadline)
        {
            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            operation = await ReadBulkOperationAsync(account, name, cancellationToken).ConfigureAwait(false);
        }

        return operation;
    }

    /// <summary>
    /// Reads an operation without tolerating a miss: a wait is only useful if an unknown id is an error
    /// rather than a reason to keep polling until the timeout.
    /// </summary>
    private async Task<RulesListBulkOperation> ReadBulkOperationAsync(
        string account,
        string operationId,
        CancellationToken cancellationToken)
    {
        var request = CloudflareRequest.Get(BulkOperationPath(account, operationId));
        request.OperationName = "Get Account List Bulk Operation";

        var response = await Transport.SendAsync<RulesListBulkOperation>(request, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <summary>
    /// Sends a bare array of items with the given verb, after checking every item carries a value and that
    /// they all carry the same kind of one.
    /// </summary>
    private async Task<RulesListItemOperation> SendItemsAsync(
        HttpMethod method,
        string listId,
        IEnumerable<CreateRulesListItemRequest> items,
        string operationName,
        string? accountId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);

        var toSend = items.ToList();
        var problems = new List<string>();

        // An empty PUT is how a list gets cleared, so only the POST that adds items refuses one: it would
        // be a call that asks Cloudflare to add nothing.
        if (toSend.Count == 0 && method == HttpMethod.Post)
        {
            problems.Add($"{operationName} was given no items, so there is nothing to add. Clearing a list " +
                "is ReplaceItemsAsync with none, which is the one call that sends an empty array.");
        }

        var empty = toSend.Count(item => item.ValueKind is null);
        if (empty > 0)
        {
            problems.Add($"{empty} item(s) carry no value: exactly one of ip, hostname, redirect and asn " +
                "has to be set on each.");
        }

        var kinds = toSend
            .Select(item => item.ValueKind)
            .Where(kind => kind is not null)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (kinds.Count > 1)
        {
            // One list holds one kind of value, so a mixed batch cannot be applied whichever kind it is.
            problems.Add($"Every item has to carry the same kind of value, but this batch mixes " +
                $"{string.Join(", ", kinds)}.");
        }

        if (problems.Count > 0)
        {
            throw new Exceptions.CloudflareConfigurationException(problems);
        }

        var request = new CloudflareRequest(method, $"{ListPath(ResolveAccountId(accountId), listId)}/items")
        {
            Body = new JsonRequestBody(toSend),
        };
        request.OperationName = operationName;

        var response = await Transport.SendAsync<RulesListItemOperation>(request, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }

    private static string ListPath(string accountId, string listId) =>
        $"{AccountPath(accountId)}/rules/lists/{CloudflareUrl.EncodeSegment(Require(listId, nameof(listId)))}";

    private static string BulkOperationPath(string accountId, string operationId) =>
        $"{AccountPath(accountId)}/rules/lists/bulk_operations/{CloudflareUrl.EncodeSegment(Require(operationId, nameof(operationId)))}";
}
