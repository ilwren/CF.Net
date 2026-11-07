using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Http;
using CFNet.Models.Common;
using CFNet.Serialization;

namespace CFNet.Models.PageRules;

/// <summary>
/// A page rule: one or more URL targets, the actions to run on requests that match them, and the order it is
/// evaluated in, as <c>/zones/{zone_id}/pagerules</c> reports it.
/// </summary>
/// <remarks>
/// <para>
/// Page rules are the older, zone-scoped way to change how Cloudflare handles a set of URLs - forwarding one,
/// caching another, turning security off for a third. Rulesets (<c>…/rulesets</c>) are the newer engine and
/// cover most of the same ground; a page rule still matters for the actions only it has, forwarding URLs among
/// them, and every zone carries a plan-sized allowance of them.
/// </para>
/// <para>
/// A rule matches when <em>every</em> target in it matches, and only one page rule runs per request: when two
/// of them could match, the one with the higher <see cref="Priority"/> wins.
/// </para>
/// </remarks>
public sealed class PageRule
{
    /// <summary>Immutable id of the rule.</summary>
    public string? Id { get; set; }

    /// <summary>The URL targets. A request has to match all of them for the rule to apply.</summary>
    public List<PageRuleTarget>? Targets { get; set; }

    /// <summary>What Cloudflare does to a request that matches. Order within a rule does not matter.</summary>
    public List<PageRuleAction>? Actions { get; set; }

    /// <summary>
    /// Which rule wins when two of them could match: **a higher number takes precedence**, so a catch-all
    /// <c>example.com/images/*</c> at priority 1 is overridden by <c>example.com/images/special/*</c> at
    /// priority 2. Defaults to 1 when a create leaves it out.
    /// </summary>
    public int? Priority { get; set; }

    /// <summary>Whether the rule runs at all.</summary>
    public PageRuleStatus? Status { get; set; }

    /// <summary>When the rule was created, exactly as Cloudflare wrote it.</summary>
    public string? CreatedOn { get; set; }

    /// <summary>When the rule was last changed, exactly as Cloudflare wrote it.</summary>
    public string? ModifiedOn { get; set; }

    /// <summary><see cref="CreatedOn"/> parsed, in UTC; null when Cloudflare sent nothing parseable.</summary>
    [JsonIgnore]
    public DateTimeOffset? CreatedOnUtc => CloudflareTimestamps.Read(CreatedOn);

    /// <summary><see cref="ModifiedOn"/> parsed, in UTC; null when Cloudflare sent nothing parseable.</summary>
    [JsonIgnore]
    public DateTimeOffset? ModifiedOnUtc => CloudflareTimestamps.Read(ModifiedOn);

    /// <summary>Members Cloudflare added after this library was released.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The rule's targets, actions and priority on one line.</summary>
    /// <returns>The rule described in one line.</returns>
    public override string ToString()
    {
        var targets = Targets is { } targetList
            ? string.Join(", ", targetList.Select(target => target.ToString()))
            : "(none)";
        var actions = Actions is { } actionList
            ? string.Join(", ", actionList.Select(action => action.ToString()))
            : "(none)";
        return $"{targets} => {actions} (priority {Priority}, {Status})";
    }
}

/// <summary>Whether a page rule runs.</summary>
[JsonConverter(typeof(CloudflareEnumConverter<PageRuleStatus>))]
public enum PageRuleStatus
{
    /// <summary>Value Cloudflare returned that this version of the library does not know.</summary>
    Unknown = 0,

    /// <summary>The rule runs on matching requests.</summary>
    [CloudflareValue("active")]
    Active = 1,

    /// <summary>The rule is stored but skipped.</summary>
    [CloudflareValue("disabled")]
    Disabled = 2,
}

/// <summary>One of a page rule's URL targets.</summary>
/// <remarks>
/// <c>url</c> is the only target Cloudflare publishes, so <see cref="Target"/> is that word rather than an
/// enum with one member; the interesting part is the constraint, whose operator says how the pattern is
/// matched against the request URL.
/// </remarks>
public sealed class PageRuleTarget
{
    /// <summary>The kind of target. See <see cref="PageRuleTargetKinds"/>.</summary>
    public string? Target { get; set; }

    /// <summary>The pattern and how it is matched.</summary>
    public PageRuleTargetConstraint? Constraint { get; set; }

    /// <summary>Members Cloudflare added after this library was released.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Builds the ordinary target: a URL pattern matched with wildcards.</summary>
    /// <param name="pattern">
    /// The pattern, for example <c>example.com/images/*</c>. <c>*</c> matches any characters, and a pattern
    /// has to belong to the zone the rule is created in.
    /// </param>
    /// <returns>A target ready to send.</returns>
    public static PageRuleTarget Matching(string pattern) =>
        Of(PageRuleTargetOperator.Matches, pattern);

    /// <summary>Builds a target with a particular matching operator.</summary>
    /// <param name="operator">How the value is matched against the request URL.</param>
    /// <param name="value">The pattern to match.</param>
    /// <returns>A target ready to send.</returns>
    public static PageRuleTarget Of(PageRuleTargetOperator @operator, string value) =>
        new()
        {
            Target = PageRuleTargetKinds.Url,
            Constraint = new PageRuleTargetConstraint { Operator = @operator, Value = value },
        };

    /// <summary>The pattern and its operator, on one line.</summary>
    /// <returns>The target described in one line.</returns>
    public override string ToString() =>
        Constraint is { } constraint
            ? $"{Target} {CloudflareEnumConverter<PageRuleTargetOperator>.ToWire(constraint.Operator)} {constraint.Value}"
            : $"{Target} (no constraint)";
}

/// <summary>The kinds of target a page rule publishes.</summary>
/// <remarks>
/// One word today, kept as a constant rather than an enum because a second kind would arrive as itself rather
/// than as a failure to parse.
/// </remarks>
public static class PageRuleTargetKinds
{
    /// <summary>A URL pattern, which is the only kind Cloudflare publishes.</summary>
    public const string Url = "url";
}

/// <summary>How a page rule target's pattern is matched against a request URL.</summary>
[JsonConverter(typeof(CloudflareEnumConverter<PageRuleTargetOperator>))]
public enum PageRuleTargetOperator
{
    /// <summary>Value Cloudflare returned that this version of the library does not know.</summary>
    Unknown = 0,

    /// <summary>Wildcard matching, which is what a page rule pattern normally uses.</summary>
    [CloudflareValue("matches")]
    Matches = 1,

    /// <summary>The URL contains the value.</summary>
    [CloudflareValue("contains")]
    Contains = 2,

    /// <summary>The URL equals the value.</summary>
    [CloudflareValue("equals")]
    Equals = 3,

    /// <summary>The URL does not equal the value.</summary>
    [CloudflareValue("not_equal")]
    NotEqual = 4,

    /// <summary>The URL does not contain the value.</summary>
    [CloudflareValue("not_contain")]
    NotContain = 5,
}

/// <summary>The pattern of a page rule target, and how it is matched.</summary>
public sealed class PageRuleTargetConstraint
{
    /// <summary>How <see cref="Value"/> is matched against the request URL.</summary>
    public PageRuleTargetOperator Operator { get; set; }

    /// <summary>The pattern itself.</summary>
    public string? Value { get; set; }

    /// <summary>Members Cloudflare added after this library was released.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The operator and the pattern, on one line.</summary>
    /// <returns>The constraint described in one line.</returns>
    public override string ToString() => $"{Operator} {Value}";
}

/// <summary>
/// One thing a page rule does to a matching request: an action id, and the value that action takes.
/// </summary>
/// <remarks>
/// <para>
/// The value is deliberately raw JSON. Page rules publish around thirty actions and they do not share a value
/// shape: some take no value at all (<c>disable_apps</c>), some a word (<c>cache_level</c>), some a number of
/// seconds (<c>edge_cache_ttl</c>), some a nested object (<c>cache_key_fields</c>), and
/// <c>cache_ttl_by_status</c> takes a map whose values are numbers <em>or</em> the words <c>no-cache</c> and
/// <c>no-store</c>. Typing all of that from documentation alone, for values no live answer has shown this
/// library, is how a wrong model gets shipped - so the factories below build the common shapes and
/// <see cref="Value"/> keeps whatever arrives.
/// </para>
/// <para>
/// Some action ids are page rules' own (<see cref="PageRuleActionIds"/>); the rest are zone-setting ids, the
/// same words <see cref="CFNet.Models.Zones.ZoneSettingIds"/> carries, because a page rule action is a zone
/// setting applied to one set of URLs.
/// </para>
/// </remarks>
public sealed class PageRuleAction
{
    /// <summary>The action's id, for example <c>forwarding_url</c> or <c>cache_level</c>.</summary>
    public string? Id { get; set; }

    /// <summary>
    /// The value the action takes, as the JSON Cloudflare wants and sends. Null for the actions that take
    /// none, which are sent as an id with no <c>value</c> member at all.
    /// </summary>
    public JsonElement? Value { get; set; }

    /// <summary>Members Cloudflare added after this library was released.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>An action that takes no value, like <c>disable_apps</c>.</summary>
    /// <param name="id">The action's id.</param>
    /// <returns>The action.</returns>
    public static PageRuleAction NoValue(string id) => new() { Id = id };

    /// <summary>An action switched on, which Cloudflare spells as the word <c>on</c>.</summary>
    /// <param name="id">The action's id.</param>
    /// <returns>The action.</returns>
    public static PageRuleAction On(string id) => Text(id, "on");

    /// <summary>An action switched off, which Cloudflare spells as the word <c>off</c>.</summary>
    /// <param name="id">The action's id.</param>
    /// <returns>The action.</returns>
    public static PageRuleAction Off(string id) => Text(id, "off");

    /// <summary>An action switched on or off, in the words Cloudflare uses rather than a boolean.</summary>
    /// <param name="id">The action's id.</param>
    /// <param name="on">Whether the action is on.</param>
    /// <returns>The action.</returns>
    public static PageRuleAction Flag(string id, bool on) => Text(id, on ? "on" : "off");

    /// <summary>An action whose value is a word or a string, like <c>cache_level</c> or <c>resolve_override</c>.</summary>
    /// <param name="id">The action's id.</param>
    /// <param name="value">The value.</param>
    /// <returns>The action.</returns>
    public static PageRuleAction Text(string id, string value) =>
        new() { Id = id, Value = JsonSerializer.SerializeToElement(value) };

    /// <summary>An action whose value is a number, like the seconds of <c>edge_cache_ttl</c>.</summary>
    /// <param name="id">The action's id.</param>
    /// <param name="value">The value.</param>
    /// <returns>The action.</returns>
    public static PageRuleAction Number(string id, int value) =>
        new() { Id = id, Value = JsonSerializer.SerializeToElement(value) };

    /// <summary>
    /// The forwarding action, which is the one page rules have that rulesets do not: send matching requests to
    /// another URL.
    /// </summary>
    /// <param name="statusCode">301 for a permanent redirect, 302 for a temporary one.</param>
    /// <param name="url">
    /// Where to send the request. A <c>*</c> in it is filled from the matched part of the request URL.
    /// </param>
    /// <returns>The action.</returns>
    public static PageRuleAction Forwarding(int statusCode, string url) =>
        new()
        {
            Id = PageRuleActionIds.ForwardingUrl,
            Value = JsonSerializer.SerializeToElement(new { status_code = statusCode, url }),
        };

    /// <summary>Reads the value as a word, tolerating a value Cloudflare sent as something else.</summary>
    /// <returns>The value as a string, or null when there is none.</returns>
    public string? ValueAsText() => Value switch
    {
        { ValueKind: JsonValueKind.String } value => value.GetString(),
        { ValueKind: JsonValueKind.Number } value => value.GetRawText(),
        { ValueKind: JsonValueKind.True } => "on",
        { ValueKind: JsonValueKind.False } => "off",
        _ => null,
    };

    /// <summary>Reads the value as a number of seconds, tolerating a number Cloudflare sent as a word.</summary>
    /// <returns>The value as an integer, or null when there is none or it is not a number.</returns>
    public int? ValueAsNumber() => Value switch
    {
        { ValueKind: JsonValueKind.Number } value => value.TryGetInt32(out var number) ? number : null,
        { ValueKind: JsonValueKind.String } value => int.TryParse(value.GetString(), out var number) ? number : null,
        _ => null,
    };

    /// <summary>The id and, when there is one, the value as Cloudflare wrote it.</summary>
    /// <returns>The action described in one line.</returns>
    public override string ToString() =>
        Value is { } value ? $"{Id}={value.GetRawText()}" : Id ?? "(no id)";
}

/// <summary>
/// The action ids that belong to page rules themselves. The remaining actions a page rule can carry are zone
/// settings applied to one set of URLs, and their ids are the ones <see cref="CFNet.Models.Zones.ZoneSettingIds"/>
/// carries.
/// </summary>
public static class PageRuleActionIds
{
    /// <summary>Which requests are considered the same for the cache, by cookie, header, host, query string and user.</summary>
    public const string CacheKeyFields = "cache_key_fields";

    /// <summary>Cache separately per device type.</summary>
    public const string CacheByDeviceType = "cache_by_device_type";

    /// <summary>Ignore a query string that only looks like a cache-buster.</summary>
    public const string CacheDeceptionArmor = "cache_deception_armor";

    /// <summary>Cache separately per value of a named cookie.</summary>
    public const string CacheOnCookie = "cache_on_cookie";

    /// <summary>Bypass the cache for requests carrying a named cookie.</summary>
    public const string BypassCacheOnCookie = "bypass_cache_on_cookie";

    /// <summary>How long each status code is cached for, as a map of status pattern to seconds or <c>no-cache</c>.</summary>
    public const string CacheTtlByStatus = "cache_ttl_by_status";

    /// <summary>Turn off Cloudflare Apps on matching requests. Takes no value.</summary>
    public const string DisableApps = "disable_apps";

    /// <summary>Turn off Rocket Loader and Polish on matching requests. Takes no value.</summary>
    public const string DisablePerformance = "disable_performance";

    /// <summary>Turn off the security features on matching requests. Takes no value.</summary>
    public const string DisableSecurity = "disable_security";

    /// <summary>Turn off Zaraz on matching requests. Takes no value.</summary>
    public const string DisableZaraz = "disable_zaraz";

    /// <summary>How long Cloudflare caches the resource at the edge, in seconds.</summary>
    public const string EdgeCacheTtl = "edge_cache_ttl";

    /// <summary>Let the origin's cache headers through rather than applying Cloudflare's.</summary>
    public const string ExplicitCacheControl = "explicit_cache_control";

    /// <summary>Send matching requests to another URL. Takes <c>{status_code, url}</c>.</summary>
    public const string ForwardingUrl = "forwarding_url";

    /// <summary>The <c>Host</c> header Cloudflare sends to the origin.</summary>
    public const string HostHeaderOverride = "host_header_override";

    /// <summary>The hostname Cloudflare resolves instead of the request's.</summary>
    public const string ResolveOverride = "resolve_override";

    /// <summary>Honour a strong ETag from the origin.</summary>
    public const string RespectStrongEtag = "respect_strong_etag";

    /// <summary>Run a browser integrity check on matching requests.</summary>
    public const string BrowserCheck = "browser_check";
}

/// <summary>
/// Body of <c>POST /zones/{zone_id}/pagerules</c>, of the <c>PUT</c> that replaces a rule and of the
/// <c>PATCH</c> that changes part of one.
/// </summary>
/// <remarks>
/// The three calls carry the same fields, so one type serves them: a <c>POST</c> and a <c>PUT</c> want the
/// targets and the actions, and a <c>PATCH</c> takes only what is meant to change - which is how a rule is
/// disabled or reordered without resending the rest of it.
/// </remarks>
public sealed class PageRuleRequest
{
    /// <summary>The URL targets a request has to match, all of them.</summary>
    public List<PageRuleTarget>? Targets { get; set; }

    /// <summary>What Cloudflare does to a matching request.</summary>
    public List<PageRuleAction>? Actions { get; set; }

    /// <summary>Which rule wins when two could match; a higher number takes precedence.</summary>
    public int? Priority { get; set; }

    /// <summary>Whether the rule runs at all.</summary>
    public PageRuleStatus? Status { get; set; }

    /// <summary>Builds a rule from one wildcard target and the actions to run on it.</summary>
    /// <param name="pattern">The URL pattern, for example <c>example.com/images/*</c>.</param>
    /// <param name="actions">What to do with a request that matches.</param>
    /// <returns>A request ready to send.</returns>
    public static PageRuleRequest For(string pattern, IEnumerable<PageRuleAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        return new PageRuleRequest
        {
            Targets = new List<PageRuleTarget> { PageRuleTarget.Matching(pattern) },
            Actions = actions.ToList(),
        };
    }

    /// <summary>Builds a rule that forwards matching requests to another URL.</summary>
    /// <param name="pattern">The URL pattern to match.</param>
    /// <param name="statusCode">301 for a permanent redirect, 302 for a temporary one.</param>
    /// <param name="url">Where to send the request.</param>
    /// <returns>A request ready to send.</returns>
    public static PageRuleRequest Forwarding(string pattern, int statusCode, string url) =>
        For(pattern, new[] { PageRuleAction.Forwarding(statusCode, url) });

    /// <summary>
    /// Refuses a patch Cloudflare would refuse: the same checks as <see cref="EnsureValid"/>, except that a
    /// patch is allowed to leave the targets and the actions out - that is the point of it - while still not
    /// being allowed to be empty.
    /// </summary>
    /// <exception cref="CFNet.Exceptions.CloudflareConfigurationException">
    /// Thrown when the patch would change nothing, or changes something to a value Cloudflare cannot store;
    /// its <c>Problems</c> lists every reason.
    /// </exception>
    public void EnsureValidForPatch()
    {
        var problems = Problems(requireTargetsAndActions: false);

        if (Targets is null && Actions is null && Priority is null && Status is null)
        {
            problems.Add(
                "a patch needs something to change: targets, actions, priority or status, and this one sets " +
                "none of them, so it would spend a call and create a version of nothing.");
        }

        if (problems.Count > 0)
        {
            throw new CFNet.Exceptions.CloudflareConfigurationException(problems);
        }
    }

    /// <summary>Refuses a rule Cloudflare would refuse, naming everything wrong with it at once.</summary>
    /// <exception cref="CFNet.Exceptions.CloudflareConfigurationException">
    /// Thrown when a rule has no targets, no actions, or an action with no id; its <c>Problems</c> lists every
    /// reason.
    /// </exception>
    public void EnsureValid()
    {
        var problems = Problems(requireTargetsAndActions: true);

        if (problems.Count > 0)
        {
            throw new CFNet.Exceptions.CloudflareConfigurationException(problems);
        }
    }

    private List<string> Problems(bool requireTargetsAndActions)
    {
        var problems = new List<string>();

        if (Targets is not { Count: > 0 })
        {
            if (requireTargetsAndActions)
            {
                problems.Add("a page rule needs at least one target: it is the URLs the rule applies to.");
            }
        }
        else
        {
            foreach (var target in Targets)
            {
                if (target.Constraint is null || string.IsNullOrWhiteSpace(target.Constraint.Value))
                {
                    problems.Add(
                        "every target needs a constraint with a pattern in it - a target with nothing to match " +
                        "would apply the rule to every request in the zone.");
                }
            }
        }

        if (Actions is not { Count: > 0 })
        {
            if (requireTargetsAndActions)
            {
                problems.Add("a page rule needs at least one action: a rule that changes nothing is not a rule.");
            }
        }
        else
        {
            foreach (var action in Actions)
            {
                if (string.IsNullOrWhiteSpace(action.Id))
                {
                    problems.Add(
                        "every action needs an id, which is the name Cloudflare knows it by - " +
                        "PageRuleActionIds carries the ones that belong to page rules.");
                }
            }
        }

        if (Actions is { Count: > 1 } actions &&
            actions.Any(action => string.Equals(action.Id, PageRuleActionIds.ForwardingUrl, StringComparison.Ordinal)))
        {
            // Cloudflare's own words for this endpoint: actions can redirect to another URL or override
            // settings, but not both.
            problems.Add(
                "a forwarding_url action cannot sit beside another action: Cloudflare publishes that a page " +
                "rule's actions redirect to another URL or override settings, but not both.");
        }

        if (Priority is < 1)
        {
            problems.Add($"priority counts from 1, and this is {Priority}.");
        }

        if (Status == PageRuleStatus.Unknown)
        {
            problems.Add("status, when given, has to be active or disabled.");
        }

        return problems;
    }
}

/// <summary>Filters for <c>GET /zones/{zone_id}/pagerules</c>.</summary>
/// <remarks>
/// No <c>page</c> or <c>per_page</c>: the endpoint answers every rule the zone has as a bare array, which is
/// as many as the plan allows and never enough to need paging.
/// </remarks>
public sealed class PageRuleListParameters : ICloudflareQueryParameters
{
    /// <summary>Only rules in this state.</summary>
    public PageRuleStatus? Status { get; set; }

    /// <summary>What to order the list by.</summary>
    public PageRuleListOrder? Order { get; set; }

    /// <summary>Sort direction.</summary>
    public SortDirection? Direction { get; set; }

    /// <summary>
    /// How a rule's several targets are matched against the request: <c>all</c> of them, which is what a rule
    /// does, or <c>any</c> of them. The same two words the DNS and zone lists use for their <c>match</c>.
    /// </summary>
    public FilterMatchMode? Match { get; set; }

    /// <inheritdoc />
    public void Apply(QueryStringBuilder query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (Status is { } status && status != PageRuleStatus.Unknown)
        {
            query.Add("status", CloudflareEnumConverter<PageRuleStatus>.ToWire(status));
        }

        if (Order is { } order && order != PageRuleListOrder.Unknown)
        {
            query.Add("order", CloudflareEnumConverter<PageRuleListOrder>.ToWire(order));
        }

        if (Direction is { } direction)
        {
            query.Add("direction", QueryEnums.ToWire(direction));
        }

        if (Match is { } match)
        {
            query.Add("match", QueryEnums.ToWire(match));
        }
    }
}

/// <summary>What the page rule list can be ordered by.</summary>
[JsonConverter(typeof(CloudflareEnumConverter<PageRuleListOrder>))]
public enum PageRuleListOrder
{
    /// <summary>Value Cloudflare returned that this version of the library does not know.</summary>
    Unknown = 0,

    /// <summary>By whether the rule runs.</summary>
    [CloudflareValue("status")]
    Status = 1,

    /// <summary>By the order the rules are evaluated in.</summary>
    [CloudflareValue("priority")]
    Priority = 2,
}
