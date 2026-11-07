using System.Net;
using CFNet.Http;
using CFNet.Models.PageRules;

namespace CFNet.Services;

/// <summary>
/// Page rules: <c>/zones/{zone_id}/pagerules</c>.
/// </summary>
/// <remarks>
/// <para>
/// A page rule is a URL pattern in one zone and the things Cloudflare should do differently for requests that
/// match it - forward them somewhere else, cache them longer, turn a security feature off. This service covers
/// the whole lifecycle of one: list them, read one, create it, replace it, change part of it, delete it.
/// </para>
/// <para>
/// Three things are worth knowing before calling it. The path is <c>pagerules</c>, one word, not the
/// <c>page_rules</c> the resource's own name suggests. Only one page rule runs per request, and when two of
/// them could match the one with the higher priority wins. And a plan carries an allowance of page rules -
/// three on a free plan - so a create can be refused for running out rather than for anything wrong with the
/// rule.
/// </para>
/// <para>
/// Changing a rule has two shapes: <see cref="UpdateAsync"/> is a <c>PUT</c> that replaces the whole of it, so
/// it takes the same targets and actions a create does, and <see cref="EditAsync"/> is a <c>PATCH</c> that
/// touches only the fields it is given - which is how a rule is disabled or reordered without resending the
/// rest of it.
/// </para>
/// <para>
/// Needs <b>Page Rules Write</b> for the writes and <b>Page Rules Read</b> for the reads, or <b>Zone
/// Read/Write</b>, which Cloudflare accepts for all of them.
/// </para>
/// </remarks>
public interface IPageRulesService
{
    /// <summary>Lists the zone's page rules.</summary>
    /// <param name="parameters">Status, ordering and target-matching filters; none when null.</param>
    /// <param name="zoneId">Zone whose rules to list; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Every rule the zone has, as a bare list. There is no paging: a plan's allowance is small enough that
    /// Cloudflare answers all of them at once.
    /// </returns>
    /// <remarks>
    /// <c>GET /zones/{zone_id}/pagerules</c>, which takes <c>status</c>, <c>order</c>, <c>direction</c> and
    /// <c>match</c> and no <c>page</c> or <c>per_page</c>.
    /// </remarks>
    Task<IReadOnlyList<PageRule>> ListAsync(
        PageRuleListParameters? parameters = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one page rule.</summary>
    /// <param name="pageRuleId">The rule's id.</param>
    /// <param name="zoneId">Zone the rule lives in; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rule, or null when the zone has no rule of that id.</returns>
    /// <remarks>
    /// <c>GET /zones/{zone_id}/pagerules/{pagerule_id}</c> answers the same shape the list does, so a rule
    /// read on its own and one read from the list are the same type.
    /// </remarks>
    Task<PageRule?> GetAsync(
        string pageRuleId,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a page rule.</summary>
    /// <param name="rule">The targets to match and the actions to run on a request that matches them.</param>
    /// <param name="zoneId">Zone to create the rule in; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rule as Cloudflare stored it, with the id and priority it gave it.</returns>
    /// <remarks>
    /// <para>
    /// <c>POST /zones/{zone_id}/pagerules</c>. The body is the rule itself: <c>targets</c> and <c>actions</c>
    /// are required, <c>priority</c> defaults to 1 and <c>status</c> to active.
    /// </para>
    /// <para>
    /// A rule with no targets, no actions, or an action with no id is refused locally. So is a rule whose
    /// actions both redirect and override settings, which Cloudflare publishes as something a page rule cannot
    /// do: <see cref="PageRuleActionIds.ForwardingUrl"/> has to be the only action when it is there.
    /// </para>
    /// </remarks>
    Task<PageRule> CreateAsync(
        PageRuleRequest rule,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces a page rule.</summary>
    /// <param name="pageRuleId">The rule to replace.</param>
    /// <param name="rule">The rule as it should be afterwards, targets and actions included.</param>
    /// <param name="zoneId">Zone the rule lives in; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rule as Cloudflare stored it.</returns>
    /// <remarks>
    /// <c>PUT /zones/{zone_id}/pagerules/{pagerule_id}</c>, which Cloudflare describes as making the rule
    /// exactly match what was sent - so a target or an action left out is gone rather than kept. Read the rule
    /// first when only one part of it is meant to change, or use <see cref="EditAsync"/>.
    /// </remarks>
    Task<PageRule> UpdateAsync(
        string pageRuleId,
        PageRuleRequest rule,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Changes part of a page rule.</summary>
    /// <param name="pageRuleId">The rule to change.</param>
    /// <param name="rule">The fields to change; the ones left null stay as they are.</param>
    /// <param name="zoneId">Zone the rule lives in; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rule as Cloudflare stored it.</returns>
    /// <remarks>
    /// <para>
    /// <c>PATCH /zones/{zone_id}/pagerules/{pagerule_id}</c> - the way to disable a rule or move it in the
    /// order without resending its targets and actions.
    /// </para>
    /// <para>
    /// A patch that sets nothing is refused locally rather than sent: it would spend a call and change no rule.
    /// </para>
    /// </remarks>
    Task<PageRule> EditAsync(
        string pageRuleId,
        PageRuleRequest rule,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a page rule.</summary>
    /// <param name="pageRuleId">The rule to delete.</param>
    /// <param name="zoneId">Zone the rule lives in; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// <c>DELETE /zones/{zone_id}/pagerules/{pagerule_id}</c>. Cloudflare answers the id it deleted and nothing
    /// else worth modelling, so there is nothing to return; deleting a rule also frees the allowance slot it
    /// was using.
    /// </remarks>
    Task DeleteAsync(
        string pageRuleId,
        string? zoneId = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class PageRulesService : CloudflareService, IPageRulesService
{
    /// <summary>Creates the service over a transport.</summary>
    /// <param name="transport">The transport every call goes through.</param>
    public PageRulesService(ICloudflareTransport transport)
        : base(transport)
    {
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PageRule>> ListAsync(
        PageRuleListParameters? parameters = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get(PageRulesPath(ResolveZoneId(zoneId)));
        parameters?.Apply(request.Query);
        request.OperationName = "List Page Rules";

        // A bare array: the endpoint has no paging, because a plan's allowance is small enough not to need it.
        var response = await Transport
            .SendAsync<List<PageRule>>(request, cancellationToken)
            .ConfigureAwait(false);
        return response.Result ?? new List<PageRule>();
    }

    /// <inheritdoc />
    public async Task<PageRule?> GetAsync(
        string pageRuleId,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest
            .Get(PageRulePath(ResolveZoneId(zoneId), pageRuleId))
            .Tolerate(HttpStatusCode.NotFound);
        request.OperationName = "Get Page Rule";

        var response = await Transport
            .SendAsync<PageRule>(request, cancellationToken)
            .ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<PageRule> CreateAsync(
        PageRuleRequest rule,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        rule.EnsureValid();

        var request = CloudflareRequest.Post(PageRulesPath(ResolveZoneId(zoneId)), rule);
        request.OperationName = "Create Page Rule";

        var response = await Transport
            .SendAsync<PageRule>(request, cancellationToken)
            .ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<PageRule> UpdateAsync(
        string pageRuleId,
        PageRuleRequest rule,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        rule.EnsureValid();

        var request = CloudflareRequest.Put(PageRulePath(ResolveZoneId(zoneId), pageRuleId), rule);
        request.OperationName = "Update Page Rule";

        var response = await Transport
            .SendAsync<PageRule>(request, cancellationToken)
            .ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<PageRule> EditAsync(
        string pageRuleId,
        PageRuleRequest rule,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);

        // The lighter check: a patch is allowed to leave the targets and actions alone, which is its purpose.
        rule.EnsureValidForPatch();

        var request = CloudflareRequest.Patch(PageRulePath(ResolveZoneId(zoneId), pageRuleId), rule);
        request.OperationName = "Edit Page Rule";

        var response = await Transport
            .SendAsync<PageRule>(request, cancellationToken)
            .ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        string pageRuleId,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Delete(PageRulePath(ResolveZoneId(zoneId), pageRuleId));
        request.OperationName = "Delete Page Rule";

        await SendVoidAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static string PageRulesPath(string zoneId) => $"{ZonePath(zoneId)}/pagerules";

    private static string PageRulePath(string zoneId, string pageRuleId) =>
        $"{PageRulesPath(zoneId)}/{CloudflareUrl.EncodeSegment(Require(pageRuleId, nameof(pageRuleId)))}";
}
