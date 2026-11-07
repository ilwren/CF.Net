using System.Net;
using CFNet.Http;
using CFNet.Models.Firewall;

namespace CFNet.Services;

/// <summary>
/// The firewall's older surface: the zone's saved <b>filters</b>, and the account's or zone's <b>IP Access
/// rules</b>.
/// </summary>
/// <remarks>
/// <para>
/// Two resources that are neighbours in the dashboard and strangers in the API, which is why one service covers
/// both and why their permissions differ.
/// </para>
/// <para>
/// <b>Filters are deprecated.</b> Cloudflare deprecated the filters API together with the legacy firewall rules
/// API that consumed it, in favour of the ruleset engine, where a rule carries its expression inline instead of
/// pointing at a saved one. The path still answers, and an account with older rules can still only read or
/// remove its filters here, so it is wrapped — but <c>client.Rulesets</c> is where anything new belongs, and
/// filters want <b>Firewall Services</b> Read and Write on the zone.
/// </para>
/// <para>
/// <b>IP Access rules are current, and they have a permission group of their own.</b> Not Firewall Services:
/// Cloudflare publishes <b>Account Firewall Access Rules</b> Read and Write for these, so a token that can read
/// a zone's filters can still be refused here with a 403 that has nothing to do with the zone. Its own
/// documentation also spells the scope <c>{accounts_or_zones}</c> — the same resource is served from an account
/// path and from a zone path, and an account-level rule applies to every zone in the account.
/// </para>
/// <para>
/// <b>Scope.</b> Every access rule method therefore takes both an <c>accountId</c> and a <c>zoneId</c>, and
/// works against the zone <em>only when a zone id is passed explicitly</em>: the configured default
/// <c>ZoneId</c> is deliberately not consulted, because a method that silently changed scope depending on how
/// the client was configured would be worse than one that asks. Leaving both null means "this account". The
/// filter methods are zone-only, so they take a <c>zoneId</c> and fall back to the configured default like the
/// rest of the zone-scoped services.
/// </para>
/// <para>
/// <b>What a live run against these endpoints said.</b> Three things the published reference does not. First,
/// <c>POST /zones/{zone_id}/filters</c> answered <b>410 Gone with error 10020, <c>filters.api.deprecated</c></b>
/// — "This API has been deprecated. Please use the Rulesets API instead." — so the deprecation has reached at
/// least some accounts and no permission brings the endpoint back; the methods are still here because the API
/// is still published and an account whose rules were never migrated may still be served it. Second, the two
/// access rule paths do <em>not</em> share a permission requirement: one token created a rule on the zone path
/// and listed it back, then was refused on the account path with <c>10000</c>, so <b>Account Firewall Access
/// Rules</b> is what the account scope wants and the zone scope was reachable without it. Third, the answer
/// carries two fields Cloudflare's schema for this resource does not list — <c>paused</c> on the rule and
/// <c>name</c> on its scope — and both are modelled rather than left in <c>AdditionalData</c>, while neither is
/// sent in a request because nothing published says one is accepted there.
/// </para>
/// </remarks>
public interface IFirewallService
{
    /// <summary>Lists the filters saved on a zone.</summary>
    /// <param name="parameters">Paging and the field filters; none when null.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One page of filters, with the paging Cloudflare reported.</returns>
    /// <remarks><c>GET /zones/{zone_id}/filters</c>.</remarks>
    Task<CloudflarePage<FirewallFilter>> ListFiltersAsync(
        FirewallFilterListParameters? parameters = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every filter saved on a zone, walking the pages.</summary>
    /// <param name="parameters">The field filters and the page size to walk with; none when null.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Every filter, page by page, so a caller can stop early without reading the rest.</returns>
    IAsyncEnumerable<FirewallFilter> ListAllFiltersAsync(
        FirewallFilterListParameters? parameters = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one filter.</summary>
    /// <param name="filterId">The filter's identifier.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The filter, or null when the zone has no filter with that identifier.</returns>
    /// <remarks><c>GET /zones/{zone_id}/filters/{filter_id}</c>.</remarks>
    Task<FirewallFilter?> GetFilterAsync(
        string filterId,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Saves one or more filters in a single call.</summary>
    /// <param name="filters">The filters to save; Cloudflare assigns their identifiers.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The filters as Cloudflare saved them, with the identifiers it assigned.</returns>
    /// <remarks>
    /// <c>POST /zones/{zone_id}/filters</c>, whose body is a bare array — there is no single-filter create, so
    /// one filter is an array of one, and the answer is an array too.
    /// </remarks>
    Task<IReadOnlyList<FirewallFilter>> CreateFiltersAsync(
        IEnumerable<FirewallFilterRequest> filters,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces one filter.</summary>
    /// <param name="filterId">The filter's identifier.</param>
    /// <param name="filter">What the filter should become.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The filter as it now stands.</returns>
    /// <remarks>
    /// <c>PUT /zones/{zone_id}/filters/{filter_id}</c>. A <c>PUT</c>, so it replaces the whole filter: read it,
    /// change it, send it all back. An <see cref="FirewallFilterRequest.Id"/> in the body has to agree with
    /// <paramref name="filterId"/>, because the path is what says which filter changes.
    /// </remarks>
    Task<FirewallFilter> UpdateFilterAsync(
        string filterId,
        FirewallFilterRequest filter,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces several filters in a single call.</summary>
    /// <param name="filters">The filters to change, each carrying the identifier of the one it replaces.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The filters as they now stand.</returns>
    /// <remarks>
    /// <c>PUT /zones/{zone_id}/filters</c>, whose body is a bare array and whose members carry their own
    /// <c>id</c>, because there is no path to say which filter each one is.
    /// </remarks>
    Task<IReadOnlyList<FirewallFilter>> UpdateFiltersAsync(
        IEnumerable<FirewallFilterRequest> filters,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Removes one filter.</summary>
    /// <param name="filterId">The filter's identifier.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What Cloudflare removed, or null when the zone had no filter with that identifier.</returns>
    /// <remarks><c>DELETE /zones/{zone_id}/filters/{filter_id}</c>.</remarks>
    Task<FirewallFilterDeletion?> DeleteFilterAsync(
        string filterId,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Removes several filters in a single call.</summary>
    /// <param name="filterIds">The identifiers to remove.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What Cloudflare removed, one entry per identifier it accepted.</returns>
    /// <remarks>
    /// <c>DELETE /zones/{zone_id}/filters</c> — a delete with a body, because the identifiers to remove are
    /// <c>{"id":["…"]}</c> rather than a query string of them.
    /// </remarks>
    Task<IReadOnlyList<FirewallFilterDeletion>> DeleteFiltersAsync(
        IEnumerable<string> filterIds,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the IP Access rules of an account or of one zone.</summary>
    /// <param name="parameters">Paging, ordering and the filters; none when null.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="zoneId">Zone id, to list one zone's rules instead of the account's; the configured default
    /// is not consulted, so this has to be passed explicitly.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One page of rules, with the paging Cloudflare reported.</returns>
    /// <remarks><c>GET /{accounts_or_zones}/{account_or_zone_id}/firewall/access_rules/rules</c>.</remarks>
    Task<CloudflarePage<AccessRule>> ListAccessRulesAsync(
        AccessRuleListParameters? parameters = null,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one IP Access rule.</summary>
    /// <param name="ruleId">The rule's identifier.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="zoneId">Zone id, for a zone's rule; not the configured default unless passed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rule, or null when that scope has no rule with the identifier.</returns>
    /// <remarks><c>GET /{accounts_or_zones}/{account_or_zone_id}/firewall/access_rules/rules/{rule_id}</c>.</remarks>
    Task<AccessRule?> GetAccessRuleAsync(
        string ruleId,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Makes an IP Access rule.</summary>
    /// <param name="rule">What it matches and what it does.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="zoneId">Zone id, for a rule of one zone; not the configured default unless passed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rule as Cloudflare saved it, with the modes it says the rule may be switched to.</returns>
    /// <remarks>
    /// <c>POST /{accounts_or_zones}/{account_or_zone_id}/firewall/access_rules/rules</c>. An account-level rule
    /// applies to every zone in the account. Which modes are available depends on the plan and on the target —
    /// a country rule is not something every account may make — so a refusal here is as likely to be about the
    /// account as about the request.
    /// </remarks>
    Task<AccessRule> CreateAccessRuleAsync(
        AccessRuleRequest rule,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Changes an IP Access rule.</summary>
    /// <param name="ruleId">The rule's identifier.</param>
    /// <param name="rule">What it should match and do, whole: Cloudflare publishes both as required.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="zoneId">Zone id, for a zone's rule; not the configured default unless passed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rule as it now stands.</returns>
    /// <remarks>
    /// <c>PATCH /{accounts_or_zones}/{account_or_zone_id}/firewall/access_rules/rules/{rule_id}</c>. It is
    /// spelled <c>PATCH</c> and behaves like a <c>PUT</c>: the configuration and the mode are both required, so
    /// there is no changing one field on its own.
    /// </remarks>
    Task<AccessRule> EditAccessRuleAsync(
        string ruleId,
        AccessRuleRequest rule,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Removes an IP Access rule.</summary>
    /// <param name="ruleId">The rule's identifier.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="zoneId">Zone id, for a zone's rule; not the configured default unless passed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What Cloudflare removed, or null when that scope had no rule with the identifier.</returns>
    /// <remarks>
    /// <c>DELETE /{accounts_or_zones}/{account_or_zone_id}/firewall/access_rules/rules/{rule_id}</c>.
    /// </remarks>
    Task<AccessRuleDeletion?> DeleteAccessRuleAsync(
        string ruleId,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IFirewallService" />
public sealed class FirewallService : CloudflareService, IFirewallService
{
    /// <summary>Creates the service over a transport.</summary>
    /// <param name="transport">The transport every call goes through.</param>
    public FirewallService(ICloudflareTransport transport)
        : base(transport)
    {
    }

    /// <inheritdoc />
    public async Task<CloudflarePage<FirewallFilter>> ListFiltersAsync(
        FirewallFilterListParameters? parameters = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get(FiltersPath(ResolveZoneId(zoneId)));
        parameters?.Apply(request.Query);
        request.OperationName = "List Firewall Filters";

        var response = await Transport
            .SendAsync<List<FirewallFilter>>(request, cancellationToken)
            .ConfigureAwait(false);
        return response.ToPage();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<FirewallFilter> ListAllFiltersAsync(
        FirewallFilterListParameters? parameters = null,
        string? zoneId = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var zone = ResolveZoneId(zoneId);
        var page = 1;

        while (true)
        {
            var effective = new FirewallFilterListParameters
            {
                Id = parameters?.Id,
                Description = parameters?.Description,
                Expression = parameters?.Expression,
                Paused = parameters?.Paused,
                Ref = parameters?.Ref,
                PerPage = parameters?.PerPage,
                Page = page,
            };

            var result = await ListFiltersAsync(effective, zone, cancellationToken).ConfigureAwait(false);
            foreach (var filter in result.Items)
            {
                yield return filter;
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
    public async Task<FirewallFilter?> GetFilterAsync(
        string filterId,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest
            .Get(FilterPath(ResolveZoneId(zoneId), filterId))
            .Tolerate(HttpStatusCode.NotFound);
        request.OperationName = "Get Firewall Filter";

        var response = await Transport
            .SendAsync<FirewallFilter>(request, cancellationToken)
            .ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FirewallFilter>> CreateFiltersAsync(
        IEnumerable<FirewallFilterRequest> filters,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filters);

        var body = filters.ToList();
        if (body.Count == 0)
        {
            throw new Exceptions.CloudflareConfigurationException(
                "a filter create needs at least one filter: the body is an array, and an empty one would spend " +
                "a call and save nothing.");
        }

        foreach (var filter in body)
        {
            ArgumentNullException.ThrowIfNull(filter);
            filter.EnsureValid();
        }

        var request = CloudflareRequest.Post(FiltersPath(ResolveZoneId(zoneId)), body);
        request.OperationName = "Create Firewall Filters";

        var response = await Transport
            .SendAsync<List<FirewallFilter>>(request, cancellationToken)
            .ConfigureAwait(false);
        return response.Result ?? new List<FirewallFilter>();
    }

    /// <inheritdoc />
    public async Task<FirewallFilter> UpdateFilterAsync(
        string filterId,
        FirewallFilterRequest filter,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        filter.EnsureValid();

        var id = Require(filterId, nameof(filterId));
        if (filter.Id is { Length: > 0 } bodyId && !string.Equals(bodyId, id, StringComparison.Ordinal))
        {
            throw new Exceptions.CloudflareConfigurationException(
                $"a single filter update says which filter to change in the path, and this one's path says " +
                $"'{id}' while its body says '{bodyId}'. Send the same identifier in both, or leave the body's " +
                "out: Cloudflare would otherwise be told to change two different filters at once.");
        }

        var request = CloudflareRequest.Put(FilterPath(ResolveZoneId(zoneId), id), filter);
        request.OperationName = "Update Firewall Filter";

        var response = await Transport
            .SendAsync<FirewallFilter>(request, cancellationToken)
            .ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FirewallFilter>> UpdateFiltersAsync(
        IEnumerable<FirewallFilterRequest> filters,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filters);

        var body = filters.ToList();
        if (body.Count == 0)
        {
            throw new Exceptions.CloudflareConfigurationException(
                "a bulk filter update needs at least one filter: the body is an array, and an empty one would " +
                "spend a call and change nothing.");
        }

        foreach (var filter in body)
        {
            ArgumentNullException.ThrowIfNull(filter);
            filter.EnsureValidForBulkUpdate();
        }

        var request = CloudflareRequest.Put(FiltersPath(ResolveZoneId(zoneId)), body);
        request.OperationName = "Bulk Update Firewall Filters";

        var response = await Transport
            .SendAsync<List<FirewallFilter>>(request, cancellationToken)
            .ConfigureAwait(false);
        return response.Result ?? new List<FirewallFilter>();
    }

    /// <inheritdoc />
    public async Task<FirewallFilterDeletion?> DeleteFilterAsync(
        string filterId,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest
            .Delete(FilterPath(ResolveZoneId(zoneId), filterId))
            .Tolerate(HttpStatusCode.NotFound);
        request.OperationName = "Delete Firewall Filter";

        var response = await Transport
            .SendAsync<FirewallFilterDeletion>(request, cancellationToken)
            .ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FirewallFilterDeletion>> DeleteFiltersAsync(
        IEnumerable<string> filterIds,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filterIds);

        var body = FirewallFilterBulkDeleteRequest.Of(filterIds);
        body.EnsureValid();

        // A DELETE with a body: the identifiers are what Cloudflare's own schema puts in one, and a query
        // string of repeated ids is not what it publishes.
        var request = CloudflareRequest.Delete(FiltersPath(ResolveZoneId(zoneId)), body);
        request.OperationName = "Bulk Delete Firewall Filters";

        var response = await Transport
            .SendAsync<List<FirewallFilterDeletion>>(request, cancellationToken)
            .ConfigureAwait(false);
        return response.Result ?? new List<FirewallFilterDeletion>();
    }

    /// <inheritdoc />
    public async Task<CloudflarePage<AccessRule>> ListAccessRulesAsync(
        AccessRuleListParameters? parameters = null,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get(AccessRulesPath(ScopePath(accountId, zoneId)));
        parameters?.Apply(request.Query);
        request.OperationName = "List IP Access Rules";

        var response = await Transport
            .SendAsync<List<AccessRule>>(request, cancellationToken)
            .ConfigureAwait(false);
        return response.ToPage();
    }

    /// <inheritdoc />
    public async Task<AccessRule?> GetAccessRuleAsync(
        string ruleId,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest
            .Get(AccessRulePath(ScopePath(accountId, zoneId), ruleId))
            .Tolerate(HttpStatusCode.NotFound);
        request.OperationName = "Get IP Access Rule";

        var response = await Transport
            .SendAsync<AccessRule>(request, cancellationToken)
            .ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<AccessRule> CreateAccessRuleAsync(
        AccessRuleRequest rule,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        rule.EnsureValid();

        var request = CloudflareRequest.Post(AccessRulesPath(ScopePath(accountId, zoneId)), rule);
        request.OperationName = "Create IP Access Rule";

        var response = await Transport
            .SendAsync<AccessRule>(request, cancellationToken)
            .ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<AccessRule> EditAccessRuleAsync(
        string ruleId,
        AccessRuleRequest rule,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        rule.EnsureValid();

        var request = CloudflareRequest.Patch(AccessRulePath(ScopePath(accountId, zoneId), ruleId), rule);
        request.OperationName = "Edit IP Access Rule";

        var response = await Transport
            .SendAsync<AccessRule>(request, cancellationToken)
            .ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<AccessRuleDeletion?> DeleteAccessRuleAsync(
        string ruleId,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest
            .Delete(AccessRulePath(ScopePath(accountId, zoneId), ruleId))
            .Tolerate(HttpStatusCode.NotFound);
        request.OperationName = "Delete IP Access Rule";

        var response = await Transport
            .SendAsync<AccessRuleDeletion>(request, cancellationToken)
            .ConfigureAwait(false);
        return response.Result;
    }

    /// <summary>
    /// Picks the scope an access rule call is made against: the zone when a zone id was passed explicitly, and
    /// the account otherwise. The configured default zone is deliberately not consulted, so a call cannot change
    /// scope because of how the client happens to be set up.
    /// </summary>
    /// <param name="accountId">Account id, or null for the configured default.</param>
    /// <param name="zoneId">Zone id, or null to stay account-scoped.</param>
    /// <returns>The path prefix the resource hangs off.</returns>
    private string ScopePath(string? accountId, string? zoneId) =>
        string.IsNullOrWhiteSpace(zoneId)
            ? AccountPath(ResolveAccountId(accountId))
            : ZonePath(zoneId.Trim());

    private static string FiltersPath(string zoneId) => $"{ZonePath(zoneId)}/filters";

    private static string FilterPath(string zoneId, string filterId) =>
        $"{FiltersPath(zoneId)}/{CloudflareUrl.EncodeSegment(Require(filterId, nameof(filterId)))}";

    private static string AccessRulesPath(string scopePath) => $"{scopePath}/firewall/access_rules/rules";

    private static string AccessRulePath(string scopePath, string ruleId) =>
        $"{AccessRulesPath(scopePath)}/{CloudflareUrl.EncodeSegment(Require(ruleId, nameof(ruleId)))}";
}
