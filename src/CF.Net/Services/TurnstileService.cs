using System.Net;
using System.Runtime.CompilerServices;
using CFNet.Http;
using CFNet.Models.Turnstile;

namespace CFNet.Services;

/// <summary>
/// Turnstile widgets: <c>/accounts/{account_id}/challenges/widgets</c>.
/// </summary>
/// <remarks>
/// <para>
/// A widget is one Turnstile challenge: a name, the hostnames it may run on, how it presents itself, and the
/// site key and secret that a page and a server use to verify a visitor. This service covers the whole
/// lifecycle of one - list them, read one, create it, replace it, rotate its secret, delete it.
/// </para>
/// <para>
/// Two things about the shape are worth knowing before calling it. The path is <c>challenges/widgets</c>, not
/// the <c>widgets</c> the product's own name suggests. And a <b>list answers widgets without their secrets</b>
/// where a single widget's answer carries one, so the two are typed apart:
/// <see cref="TurnstileWidgetSummary"/> and <see cref="TurnstileWidget"/>.
/// </para>
/// <para>
/// Updating a widget is a <c>PUT</c> that replaces the whole of it, so <see cref="UpdateAsync"/> takes the
/// same required fields a create does: leaving one out is not leaving it alone.
/// </para>
/// </remarks>
public interface ITurnstileService
{
    /// <summary>Lists the account's widgets, one page of them.</summary>
    /// <param name="parameters">Paging, ordering and filtering; none when null.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The page. Its items carry no secret, which is what Cloudflare answers for a list; read one widget for
    /// that.
    /// </returns>
    /// <remarks><c>GET /accounts/{account_id}/challenges/widgets</c>, paged by page and per_page.</remarks>
    Task<CloudflarePage<TurnstileWidgetSummary>> ListAsync(
        TurnstileWidgetListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every widget in the account, walking the pages.</summary>
    /// <param name="parameters">Ordering and filtering, and the page size to walk with; none when null.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Every widget, page by page, so a caller can stop early without reading the rest.</returns>
    IAsyncEnumerable<TurnstileWidgetSummary> ListAllAsync(
        TurnstileWidgetListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one widget, secret included.</summary>
    /// <param name="sitekey">The widget's site key.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The widget, or null when the account has no widget with that site key.</returns>
    /// <remarks><c>GET /accounts/{account_id}/challenges/widgets/{sitekey}</c>.</remarks>
    Task<TurnstileWidget?> GetAsync(
        string sitekey,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a widget and answers it with the secret Cloudflare generated for it.</summary>
    /// <param name="request">The widget's name, mode and hostnames, and whichever of the rest a caller wants.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created widget, whose <see cref="TurnstileWidget.Secret"/> is worth keeping.</returns>
    /// <remarks>
    /// <c>POST /accounts/{account_id}/challenges/widgets</c>. The secret is only ever answered here, by a
    /// read, or by a rotation, so this is the moment to write it down.
    /// </remarks>
    Task<TurnstileWidget> CreateAsync(
        TurnstileWidgetRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces a widget.</summary>
    /// <param name="sitekey">The widget's site key.</param>
    /// <param name="request">
    /// The widget as it should be afterwards. Name, mode and domains are required again, because this is a
    /// <c>PUT</c>: a field left out is a field Cloudflare does not keep.
    /// </param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The widget as it now is.</returns>
    /// <remarks><c>PUT /accounts/{account_id}/challenges/widgets/{sitekey}</c>.</remarks>
    Task<TurnstileWidget> UpdateAsync(
        string sitekey,
        TurnstileWidgetRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Rotates a widget's secret and answers the new one.</summary>
    /// <param name="sitekey">The widget's site key.</param>
    /// <param name="invalidateImmediately">
    /// Whether the old secret stops working at once. Null sends nothing and leaves Cloudflare's grace period
    /// in place, which is what lets a deployment be rolled without a gap in verification.
    /// </param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The widget with its new secret.</returns>
    /// <remarks><c>POST /accounts/{account_id}/challenges/widgets/{sitekey}/rotate_secret</c>.</remarks>
    Task<TurnstileWidget> RotateSecretAsync(
        string sitekey,
        bool? invalidateImmediately = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a widget.</summary>
    /// <param name="sitekey">The widget's site key.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The widget Cloudflare answers with the deletion, or null when it answers nothing - a delete here is
    /// worth a read afterwards rather than a returned object.
    /// </returns>
    /// <remarks>
    /// <c>DELETE /accounts/{account_id}/challenges/widgets/{sitekey}</c>. Deleting a widget that is not there
    /// is not an error: the call tolerates a 404 and answers null.
    /// </remarks>
    Task<TurnstileWidget?> DeleteAsync(
        string sitekey,
        string? accountId = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class TurnstileService : CloudflareService, ITurnstileService
{
    /// <summary>Creates the service.</summary>
    /// <param name="transport">The transport to execute requests through.</param>
    public TurnstileService(ICloudflareTransport transport)
        : base(transport)
    {
    }

    /// <inheritdoc />
    public async Task<CloudflarePage<TurnstileWidgetSummary>> ListAsync(
        TurnstileWidgetListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get(WidgetsPath(ResolveAccountId(accountId)));
        parameters?.Apply(request.Query);
        request.OperationName = "List Turnstile Widgets";

        var response = await Transport
            .SendAsync<List<TurnstileWidgetSummary>>(request, cancellationToken)
            .ConfigureAwait(false);
        return response.ToPage();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TurnstileWidgetSummary> ListAllAsync(
        TurnstileWidgetListParameters? parameters = null,
        string? accountId = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var account = ResolveAccountId(accountId);
        var page = 1;

        while (true)
        {
            var effective = new TurnstileWidgetListParameters
            {
                Filter = parameters?.Filter,
                Order = parameters?.Order,
                Direction = parameters?.Direction,
                PerPage = parameters?.PerPage,
                Page = page,
            };

            var result = await ListAsync(effective, account, cancellationToken).ConfigureAwait(false);
            foreach (var widget in result.Items)
            {
                yield return widget;
            }

            // Stop on an empty page as well as on result_info saying so: not every list reports its paging.
            if (result.Items.Count == 0 || !result.HasMore)
            {
                yield break;
            }

            page++;
        }
    }

    /// <inheritdoc />
    public async Task<TurnstileWidget?> GetAsync(
        string sitekey,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest
            .Get(WidgetPath(ResolveAccountId(accountId), sitekey))
            .Tolerate(HttpStatusCode.NotFound);
        request.OperationName = "Get Turnstile Widget";

        var response = await Transport
            .SendAsync<TurnstileWidget>(request, cancellationToken)
            .ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<TurnstileWidget> CreateAsync(
        TurnstileWidgetRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();

        var cloudflareRequest = CloudflareRequest.Post(WidgetsPath(ResolveAccountId(accountId)), request);
        cloudflareRequest.OperationName = "Create Turnstile Widget";

        var response = await Transport
            .SendAsync<TurnstileWidget>(cloudflareRequest, cancellationToken)
            .ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<TurnstileWidget> UpdateAsync(
        string sitekey,
        TurnstileWidgetRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();

        var cloudflareRequest = CloudflareRequest.Put(
            WidgetPath(ResolveAccountId(accountId), sitekey),
            request);
        cloudflareRequest.OperationName = "Update Turnstile Widget";

        var response = await Transport
            .SendAsync<TurnstileWidget>(cloudflareRequest, cancellationToken)
            .ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<TurnstileWidget> RotateSecretAsync(
        string sitekey,
        bool? invalidateImmediately = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var body = new TurnstileSecretRotationRequest { InvalidateImmediately = invalidateImmediately };
        var cloudflareRequest = CloudflareRequest.Post(
            $"{WidgetPath(ResolveAccountId(accountId), sitekey)}/rotate_secret",
            body);
        cloudflareRequest.OperationName = "Rotate Turnstile Widget Secret";

        var response = await Transport
            .SendAsync<TurnstileWidget>(cloudflareRequest, cancellationToken)
            .ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<TurnstileWidget?> DeleteAsync(
        string sitekey,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var request = new CloudflareRequest(
            HttpMethod.Delete,
            WidgetPath(ResolveAccountId(accountId), sitekey)).Tolerate(HttpStatusCode.NotFound);
        request.OperationName = "Delete Turnstile Widget";

        var response = await Transport
            .SendAsync<TurnstileWidget>(request, cancellationToken)
            .ConfigureAwait(false);
        return response.Result;
    }

    /// <summary>The collection path, which is <c>challenges/widgets</c> rather than the product's own word.</summary>
    private string WidgetsPath(string account) => $"{AccountPath(account)}/challenges/widgets";

    /// <summary>One widget's path, its site key encoded as the segment it is.</summary>
    private string WidgetPath(string account, string sitekey) =>
        $"{WidgetsPath(account)}/{CloudflareUrl.EncodeSegment(Require(sitekey, nameof(sitekey)))}";
}
