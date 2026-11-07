using System.Net;
using CFNet.Http;
using CFNet.Models.Rules;

namespace CFNet.Services;

/// <summary>
/// The ruleset engine: <c>/accounts/{account_id}/rulesets</c> and <c>/zones/{zone_id}/rulesets</c>, their
/// rules, their versions and their phase entrypoints.
/// </summary>
/// <remarks>
/// <para>
/// A ruleset is an ordered set of rules that Cloudflare runs at one phase of the request lifecycle. This
/// service covers the whole resource: reading and writing rulesets, appending, editing and removing the
/// rules inside one, reading and pruning its versions, and reading or replacing the entrypoint that decides
/// what runs for a phase.
/// </para>
/// <para>
/// <b>Scope.</b> Cloudflare serves the identical resource from an account path and from a zone path, which
/// its own schema spells <c>{accounts_or_zones}</c>. Every method here therefore takes both an
/// <c>accountId</c> and a <c>zoneId</c>, and works against the zone <em>only when a zone id is passed
/// explicitly</em>: the configured default <c>ZoneId</c> is deliberately not consulted,
/// because a method that silently changed scope depending on how the client was configured would be worse
/// than one that asks. Leaving both null means "this account".
/// </para>
/// <para>
/// <b>Rule operations answer with the whole ruleset.</b> Appending, editing or deleting a rule returns the
/// ruleset as it now stands, not the rule, so the change is read back out of <see cref="Ruleset.Rules"/>.
/// </para>
/// <para>
/// <b>Updates replace.</b> <c>PUT</c> on a ruleset or on an entrypoint is a whole-object replacement:
/// leaving <see cref="UpdateRulesetRequest.Rules"/> out removes every rule. Read it, change it, send it all
/// back.
/// </para>
/// </remarks>
public interface IRulesetService
{
    /// <summary>Lists the rulesets an account or zone has.</summary>
    /// <param name="parameters">Optional cursor and page size.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="zoneId">Pass one to list the zone's rulesets instead of the account's.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// There is no filter by kind or phase, so narrowing down happens over what comes back. The rules of
    /// each ruleset are not included in a list; read one to get them.
    /// </remarks>
    Task<CloudflarePage<Ruleset>> ListAsync(
        RulesetListParameters? parameters = null,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one ruleset, with its rules.</summary>
    /// <param name="rulesetId">The ruleset's id.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="zoneId">Pass one to read from the zone instead of the account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ruleset, or <c>null</c> when there is no such ruleset in that scope.</returns>
    Task<Ruleset?> GetAsync(
        string rulesetId,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a ruleset.</summary>
    /// <param name="request">Its name, kind, phase, and optionally its first rules.</param>
    /// <param name="dryRun">True to have Cloudflare validate the request without applying it.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="zoneId">Pass one to create it in the zone instead of the account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ruleset Cloudflare created, with its id and first version.</returns>
    /// <remarks>
    /// A <c>root</c> ruleset can only be created where none exists for that phase; a <c>custom</c> one can
    /// always be created and does nothing until a root ruleset executes it, which is what makes it the safe
    /// thing to create against a live account. A request missing a name, kind or phase is refused locally.
    /// </remarks>
    Task<Ruleset> CreateAsync(
        CreateRulesetRequest request,
        bool? dryRun = null,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces a ruleset.</summary>
    /// <param name="rulesetId">The ruleset's id.</param>
    /// <param name="request">The state it should have afterwards.</param>
    /// <param name="dryRun">True to have Cloudflare validate the request without applying it.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="zoneId">Pass one to update the zone's ruleset instead of the account's.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ruleset as it now stands.</returns>
    /// <remarks>
    /// A whole-object replacement, so sending nothing would empty it: a request with no field set at all is
    /// refused locally. To clear the rules on purpose, send an empty <see cref="UpdateRulesetRequest.Rules"/>
    /// list, which is not the same thing as leaving it null.
    /// </remarks>
    Task<Ruleset> UpdateAsync(
        string rulesetId,
        UpdateRulesetRequest request,
        bool? dryRun = null,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a ruleset.</summary>
    /// <param name="rulesetId">The ruleset's id.</param>
    /// <param name="dryRun">True to have Cloudflare say whether it could be deleted, without deleting it.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="zoneId">Pass one to delete from the zone instead of the account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// A root ruleset cannot be deleted while its phase is running rules, and a ruleset another one executes
    /// cannot be deleted while that reference stands.
    /// </remarks>
    Task DeleteAsync(
        string rulesetId,
        bool? dryRun = null,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the entrypoint ruleset for one phase.</summary>
    /// <param name="phase">One of the <see cref="RulesetPhase"/> values.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="zoneId">Pass one to read the zone's entrypoint instead of the account's.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The root ruleset for that phase, or <c>null</c> when the scope has none - which is the normal state
    /// for a phase nothing has been configured for.
    /// </returns>
    Task<Ruleset?> GetEntrypointAsync(
        string phase,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces the entrypoint ruleset for one phase.</summary>
    /// <param name="phase">One of the <see cref="RulesetPhase"/> values.</param>
    /// <param name="request">The rules the phase should run afterwards.</param>
    /// <param name="dryRun">True to have Cloudflare validate the request without applying it.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="zoneId">Pass one to change the zone's entrypoint instead of the account's.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The entrypoint as it now stands.</returns>
    /// <remarks>
    /// This changes what live traffic does, so it is a whole-object replacement and the caller's
    /// responsibility to have read the entrypoint first. A request with nothing set is refused locally.
    /// </remarks>
    Task<Ruleset> UpdateEntrypointAsync(
        string phase,
        UpdateRulesetRequest request,
        bool? dryRun = null,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Appends a rule to a ruleset.</summary>
    /// <param name="rulesetId">The ruleset to add to.</param>
    /// <param name="rule">The rule: its action, expression and whatever parameters the action needs.</param>
    /// <param name="dryRun">True to have Cloudflare validate the rule without adding it.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="zoneId">Pass one when the ruleset belongs to a zone.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The ruleset as it now stands - Cloudflare answers a rule change with the whole ruleset, not the rule.
    /// The rule just added is the last one in <see cref="Ruleset.Rules"/>, since a rule is appended.
    /// </returns>
    /// <remarks>
    /// A rule with no action or no expression cannot match anything, so it is refused locally rather than
    /// sent. <see cref="CreateRulesetRuleRequest.ForRedirectFromList"/> builds the Bulk Redirects case.
    /// </remarks>
    Task<Ruleset> CreateRuleAsync(
        string rulesetId,
        CreateRulesetRuleRequest rule,
        bool? dryRun = null,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Changes one rule inside a ruleset.</summary>
    /// <param name="rulesetId">The ruleset holding the rule.</param>
    /// <param name="ruleId">The rule's id.</param>
    /// <param name="rule">The fields to change; unset ones are left as they are.</param>
    /// <param name="dryRun">True to have Cloudflare validate the change without applying it.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="zoneId">Pass one when the ruleset belongs to a zone.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ruleset as it now stands, with the changed rule in <see cref="Ruleset.Rules"/>.</returns>
    /// <remarks>
    /// Cloudflare requires the action and the expression on an edit as well as on a create - it refuses one
    /// without them with <c>20015</c> and <c>20125</c>, observed live - so the rule has to be sent back
    /// whole with whatever changed, which is why this takes the same type a create does. The remaining
    /// fields are genuinely optional: the serializer omits what is unset, so they are left alone.
    /// </remarks>
    Task<Ruleset> EditRuleAsync(
        string rulesetId,
        string ruleId,
        CreateRulesetRuleRequest rule,
        bool? dryRun = null,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Removes one rule from a ruleset.</summary>
    /// <param name="rulesetId">The ruleset holding the rule.</param>
    /// <param name="ruleId">The rule's id.</param>
    /// <param name="dryRun">True to have Cloudflare say whether it could be removed, without removing it.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="zoneId">Pass one when the ruleset belongs to a zone.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ruleset as it now stands, without the rule.</returns>
    Task<Ruleset> DeleteRuleAsync(
        string rulesetId,
        string ruleId,
        bool? dryRun = null,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every version a ruleset has.</summary>
    /// <param name="rulesetId">The ruleset's id.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="zoneId">Pass one when the ruleset belongs to a zone.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// One entry per version, each a frozen copy of the ruleset as it was - same shape, so the same model.
    /// </returns>
    /// <remarks>
    /// Every change to a ruleset or its rules makes a version, and they are kept until pruned, which is what
    /// <see cref="DeleteVersionAsync"/> is for.
    /// </remarks>
    Task<IReadOnlyList<Ruleset>> ListVersionsAsync(
        string rulesetId,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one version of a ruleset.</summary>
    /// <param name="rulesetId">The ruleset's id.</param>
    /// <param name="version">The version number, as a string.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="zoneId">Pass one when the ruleset belongs to a zone.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>That version, or <c>null</c> when it has been pruned or never existed.</returns>
    Task<Ruleset?> GetVersionAsync(
        string rulesetId,
        string version,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Prunes one version of a ruleset.</summary>
    /// <param name="rulesetId">The ruleset's id.</param>
    /// <param name="version">The version number, as a string.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="zoneId">Pass one when the ruleset belongs to a zone.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// The version in force cannot be pruned, and neither can the one before it, because a rollback needs
    /// somewhere to roll back to.
    /// </remarks>
    Task DeleteVersionAsync(
        string rulesetId,
        string version,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class RulesetService : CloudflareService, IRulesetService
{
    /// <summary>Creates the service.</summary>
    /// <param name="transport">The transport to execute requests through.</param>
    public RulesetService(ICloudflareTransport transport)
        : base(transport)
    {
    }

    /// <inheritdoc />
    public async Task<CloudflarePage<Ruleset>> ListAsync(
        RulesetListParameters? parameters = null,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get($"{ScopePath(accountId, zoneId)}/rulesets");
        request.OperationName = "List Rulesets";
        parameters?.Apply(request.Query);

        var response = await Transport.SendAsync<List<Ruleset>>(request, cancellationToken).ConfigureAwait(false);
        return response.ToPage();
    }

    /// <inheritdoc />
    public async Task<Ruleset?> GetAsync(
        string rulesetId,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest
            .Get(RulesetPath(ScopePath(accountId, zoneId), rulesetId))
            .Tolerate(HttpStatusCode.NotFound);
        request.OperationName = "Get Ruleset";

        var response = await Transport.SendAsync<Ruleset>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<Ruleset> CreateAsync(
        CreateRulesetRequest request,
        bool? dryRun = null,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            problems.Add("A ruleset needs a name.");
        }

        if (string.IsNullOrWhiteSpace(request.Kind))
        {
            problems.Add($"A ruleset needs a kind ({RulesetKind.Root}, {RulesetKind.Custom}, " +
                $"{RulesetKind.Managed} or {RulesetKind.Zone}). A custom one is the kind that does nothing " +
                "until a root ruleset executes it.");
        }

        if (string.IsNullOrWhiteSpace(request.Phase))
        {
            problems.Add($"A ruleset needs a phase to run at, e.g. {RulesetPhase.HttpRequestRedirect} for " +
                $"redirects or {RulesetPhase.HttpRequestFirewallCustom} for custom WAF rules.");
        }

        if (problems.Count > 0)
        {
            throw new Exceptions.CloudflareConfigurationException(problems);
        }

        var send = CloudflareRequest.Post($"{ScopePath(accountId, zoneId)}/rulesets", request);
        send.OperationName = "Create Ruleset";
        send.Query.Add("dry_run", dryRun);

        var response = await Transport.SendAsync<Ruleset>(send, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<Ruleset> UpdateAsync(
        string rulesetId,
        UpdateRulesetRequest request,
        bool? dryRun = null,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireSomethingToUpdate(request);

        var send = CloudflareRequest.Put(RulesetPath(ScopePath(accountId, zoneId), rulesetId), request);
        send.OperationName = "Update Ruleset";
        send.Query.Add("dry_run", dryRun);

        var response = await Transport.SendAsync<Ruleset>(send, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        string rulesetId,
        bool? dryRun = null,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Delete(RulesetPath(ScopePath(accountId, zoneId), rulesetId));
        request.OperationName = "Delete Ruleset";
        request.Query.Add("dry_run", dryRun);

        await SendVoidAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Ruleset?> GetEntrypointAsync(
        string phase,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest
            .Get(EntrypointPath(ScopePath(accountId, zoneId), phase))
            .Tolerate(HttpStatusCode.NotFound);
        request.OperationName = "Get Ruleset Entrypoint";

        var response = await Transport.SendAsync<Ruleset>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<Ruleset> UpdateEntrypointAsync(
        string phase,
        UpdateRulesetRequest request,
        bool? dryRun = null,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireSomethingToUpdate(request);

        var send = CloudflareRequest.Put(EntrypointPath(ScopePath(accountId, zoneId), phase), request);
        send.OperationName = "Update Ruleset Entrypoint";
        send.Query.Add("dry_run", dryRun);

        var response = await Transport.SendAsync<Ruleset>(send, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<Ruleset> CreateRuleAsync(
        string rulesetId,
        CreateRulesetRuleRequest rule,
        bool? dryRun = null,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        RequireAMatchableRule(rule);

        var send = CloudflareRequest.Post(
            $"{RulesetPath(ScopePath(accountId, zoneId), rulesetId)}/rules",
            rule);
        send.OperationName = "Create Ruleset Rule";
        send.Query.Add("dry_run", dryRun);

        var response = await Transport.SendAsync<Ruleset>(send, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<Ruleset> EditRuleAsync(
        string rulesetId,
        string ruleId,
        CreateRulesetRuleRequest rule,
        bool? dryRun = null,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);

        // Cloudflare answers an edit that omits them with 20015 and 20125, so the requirement is the same
        // as for a create: an edit is the rule sent back whole, not a sparse patch.
        RequireAMatchableRule(rule);

        var send = CloudflareRequest.Patch(
            $"{RulePath(ScopePath(accountId, zoneId), rulesetId, ruleId)}",
            rule);
        send.OperationName = "Edit Ruleset Rule";
        send.Query.Add("dry_run", dryRun);

        var response = await Transport.SendAsync<Ruleset>(send, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<Ruleset> DeleteRuleAsync(
        string rulesetId,
        string ruleId,
        bool? dryRun = null,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Delete(RulePath(ScopePath(accountId, zoneId), rulesetId, ruleId));
        request.OperationName = "Delete Ruleset Rule";
        request.Query.Add("dry_run", dryRun);

        var response = await Transport.SendAsync<Ruleset>(request, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Ruleset>> ListVersionsAsync(
        string rulesetId,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get($"{RulesetPath(ScopePath(accountId, zoneId), rulesetId)}/versions");
        request.OperationName = "List Ruleset Versions";

        var response = await Transport.SendAsync<List<Ruleset>>(request, cancellationToken).ConfigureAwait(false);
        return response.Result ?? new List<Ruleset>();
    }

    /// <inheritdoc />
    public async Task<Ruleset?> GetVersionAsync(
        string rulesetId,
        string version,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest
            .Get($"{RulesetPath(ScopePath(accountId, zoneId), rulesetId)}/versions/{CloudflareUrl.EncodeSegment(Require(version, nameof(version)))}")
            .Tolerate(HttpStatusCode.NotFound);
        request.OperationName = "Get Ruleset Version";

        var response = await Transport.SendAsync<Ruleset>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task DeleteVersionAsync(
        string rulesetId,
        string version,
        string? accountId = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Delete(
            $"{RulesetPath(ScopePath(accountId, zoneId), rulesetId)}/versions/{CloudflareUrl.EncodeSegment(Require(version, nameof(version)))}");
        request.OperationName = "Delete Ruleset Version";

        await SendVoidAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The path prefix this call works under: the zone when one was named, the account otherwise. The
    /// configured default zone id is not consulted, so that a call cannot change scope by accident.
    /// </summary>
    private string ScopePath(string? accountId, string? zoneId) =>
        string.IsNullOrWhiteSpace(zoneId)
            ? AccountPath(ResolveAccountId(accountId))
            : ZonePath(zoneId.Trim());

    private static string RulesetPath(string scopePath, string rulesetId) =>
        $"{scopePath}/rulesets/{CloudflareUrl.EncodeSegment(Require(rulesetId, nameof(rulesetId)))}";

    private static string RulePath(string scopePath, string rulesetId, string ruleId) =>
        $"{RulesetPath(scopePath, rulesetId)}/rules/{CloudflareUrl.EncodeSegment(Require(ruleId, nameof(ruleId)))}";

    private static string EntrypointPath(string scopePath, string phase) =>
        $"{scopePath}/rulesets/phases/{CloudflareUrl.EncodeSegment(Require(phase, nameof(phase)))}/entrypoint";

    /// <summary>Refuses a replacement that would set nothing, because it would empty the ruleset.</summary>
    private static void RequireSomethingToUpdate(UpdateRulesetRequest request)
    {
        if (IsEmpty(request))
        {
            throw new Exceptions.CloudflareConfigurationException(new[]
            {
                "Updating a ruleset replaces it, so a request with nothing set would empty it. Send the " +
                "fields it should keep, or an empty Rules list to clear the rules on purpose.",
            });
        }
    }

    private static bool IsEmpty(UpdateRulesetRequest request) =>
        request.Name is null
        && request.Description is null
        && request.Kind is null
        && request.Phase is null
        && request.Rules is null;

    /// <summary>
    /// Refuses a rule that could not match anything. Cloudflare asks for both on a create and on an edit.
    /// </summary>
    private static void RequireAMatchableRule(CreateRulesetRuleRequest rule)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(rule.Action))
        {
            problems.Add("A rule needs an action, e.g. redirect, block, skip or execute.");
        }

        if (string.IsNullOrWhiteSpace(rule.Expression))
        {
            problems.Add("A rule needs an expression deciding when it matches. \"true\" matches every request.");
        }

        if (problems.Count > 0)
        {
            throw new Exceptions.CloudflareConfigurationException(problems);
        }
    }
}
