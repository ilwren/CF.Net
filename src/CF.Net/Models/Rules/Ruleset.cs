using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Http;

namespace CFNet.Models.Rules;

/// <summary>
/// A ruleset: an ordered set of rules that Cloudflare runs at one point in the request lifecycle.
/// </summary>
/// <remarks>
/// <para>
/// The same shape is served from two places - <c>/accounts/{account_id}/rulesets</c> and
/// <c>/zones/{zone_id}/rulesets</c> - because a ruleset belongs either to an account or to a zone, and the
/// API spells that as <c>{accounts_or_zones}</c> in its own paths.
/// </para>
/// <para>
/// <see cref="Kind"/> says how the ruleset came to exist: <c>root</c> is the one Cloudflare runs for a
/// phase, of which an account or zone has exactly one per phase; <c>custom</c> is one you wrote, which does
/// nothing until a root ruleset <c>execute</c>s it; <c>managed</c> is published by Cloudflare; <c>zone</c>
/// is the managed-rules grouping a zone sees. Creating a <c>custom</c> ruleset is therefore the safe way to
/// exercise this API against a live account: nothing serves it until something refers to it.
/// </para>
/// <para>
/// <see cref="Version"/> is a <em>string</em> in the API's own schema, even though it looks like a number,
/// and every change to the ruleset or its rules produces a new one - see <c>ListVersionsAsync</c>.
/// </para>
/// </remarks>
public sealed class Ruleset
{
    /// <summary>The ruleset's id, which the rule and version endpoints address.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Its name. Required on a custom or root ruleset; managed ones carry their own.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Free text.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>How it came to exist. One of the <see cref="RulesetKind"/> values.</summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    /// <summary>When in the request lifecycle it runs. One of the <see cref="RulesetPhase"/> values.</summary>
    [JsonPropertyName("phase")]
    public string? Phase { get; set; }

    /// <summary>
    /// The version currently in force, as a string. Every change bumps it, and older ones stay readable.
    /// </summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>When the ruleset last changed.</summary>
    [JsonPropertyName("last_updated")]
    public DateTimeOffset? LastUpdated { get; set; }

    /// <summary>
    /// Its rules, in execution order. Present when a single ruleset is read; the list endpoint omits it.
    /// </summary>
    [JsonPropertyName("rules")]
    public List<RulesetRule>? Rules { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>True when this is the ruleset Cloudflare actually runs for its phase.</summary>
    [JsonIgnore]
    public bool IsRoot => string.Equals(Kind, RulesetKind.Root, StringComparison.Ordinal);

    /// <summary>True when this is a ruleset nobody wrote but Cloudflare publishes.</summary>
    [JsonIgnore]
    public bool IsManaged => string.Equals(Kind, RulesetKind.Managed, StringComparison.Ordinal);

    /// <summary>How many rules came back with it, or 0 when the rules were not included.</summary>
    [JsonIgnore]
    public int RuleCount => Rules?.Count ?? 0;

    /// <summary>Name, kind, phase, version and rule count on one line.</summary>
    /// <returns>The ruleset described in one line.</returns>
    public override string ToString() =>
        $"{Name ?? "(unnamed)"} ({Kind}, {Phase}, version {Version}, {RuleCount} rule(s))";
}

/// <summary>The kinds of ruleset. See <see cref="Ruleset.Kind"/>.</summary>
public static class RulesetKind
{
    /// <summary>The one ruleset Cloudflare runs for a phase. One per phase per account or zone.</summary>
    public const string Root = "root";

    /// <summary>Published by Cloudflare, read-only in substance, and executed by a root ruleset.</summary>
    public const string Managed = "managed";

    /// <summary>Written by you. Does nothing until a root ruleset executes it.</summary>
    public const string Custom = "custom";

    /// <summary>The managed-rules grouping a zone sees.</summary>
    public const string Zone = "zone";
}

/// <summary>
/// The phases a ruleset can run at: <see cref="Ruleset.Phase"/>, and the value the entrypoint endpoints
/// address.
/// </summary>
/// <remarks>
/// These are the wire values, not labels - <c>http_request_redirect</c> is Bulk Redirects and Dynamic
/// Redirect's phase, <c>http_request_firewall_custom</c> is custom WAF rules, <c>http_ratelimit</c> is rate
/// limiting, and so on. A phase that does not exist is answered with an error rather than ignored, which is
/// why they are constants rather than free text.
/// </remarks>
public static class RulesetPhase
{
    /// <summary>Configuration settings applied to the response.</summary>
    public const string HttpConfigSettings = "http_config_settings";

    /// <summary>Custom error pages.</summary>
    public const string HttpCustomErrors = "http_custom_errors";

    /// <summary>Extra fields on log lines.</summary>
    public const string HttpLogCustomFields = "http_log_custom_fields";

    /// <summary>Rate limiting rules.</summary>
    public const string HttpRatelimit = "http_ratelimit";

    /// <summary>Cache settings decided on the way to the origin.</summary>
    public const string HttpRequestCacheSettings = "http_request_cache_settings";

    /// <summary>Dynamic Redirect, the per-request redirect rules.</summary>
    public const string HttpRequestDynamicRedirect = "http_request_dynamic_redirect";

    /// <summary>Custom WAF rules on the request.</summary>
    public const string HttpRequestFirewallCustom = "http_request_firewall_custom";

    /// <summary>Managed WAF rules on the request.</summary>
    public const string HttpRequestFirewallManaged = "http_request_firewall_managed";

    /// <summary>Transformations applied after most other rules.</summary>
    public const string HttpRequestLateTransform = "http_request_late_transform";

    /// <summary>Origin selection and connection settings.</summary>
    public const string HttpRequestOrigin = "http_request_origin";

    /// <summary>Bulk Redirects: the phase a redirect ruleset runs at.</summary>
    public const string HttpRequestRedirect = "http_request_redirect";

    /// <summary>Removal of sensitive values before they are logged or forwarded.</summary>
    public const string HttpRequestSanitize = "http_request_sanitize";

    /// <summary>Super Bot Fight Mode.</summary>
    public const string HttpRequestSbfm = "http_request_sbfm";

    /// <summary>Request transformations: URL rewrites and header edits.</summary>
    public const string HttpRequestTransform = "http_request_transform";

    /// <summary>Cache settings decided on the way back.</summary>
    public const string HttpResponseCacheSettings = "http_response_cache_settings";

    /// <summary>Response compression.</summary>
    public const string HttpResponseCompression = "http_response_compression";

    /// <summary>Custom WAF rules on the response.</summary>
    public const string HttpResponseFirewallCustom = "http_response_firewall_custom";

    /// <summary>Managed WAF rules on the response.</summary>
    public const string HttpResponseFirewallManaged = "http_response_firewall_managed";

    /// <summary>Response header transformations.</summary>
    public const string HttpResponseHeadersTransform = "http_response_headers_transform";

    /// <summary>Magic Transit rules.</summary>
    public const string MagicTransit = "magic_transit";

    /// <summary>Magic Transit managed intrusion detection rules.</summary>
    public const string MagicTransitIdsManaged = "magic_transit_ids_managed";

    /// <summary>Magic Transit managed rules.</summary>
    public const string MagicTransitManaged = "magic_transit_managed";

    /// <summary>Magic Transit rate limiting.</summary>
    public const string MagicTransitRatelimit = "magic_transit_ratelimit";
}

/// <summary>One rule inside a ruleset.</summary>
/// <remarks>
/// <para>
/// Cloudflare's schema describes twenty-odd rule types - block, challenge, redirect, rewrite, skip,
/// execute, rate limit, log and the rest - which differ in what their <c>action_parameters</c> hold. This
/// is one model rather than twenty: the fields every rule shares are declared, the redirect parameters (the
/// Bulk Redirects case) are declared because they are the ones this library builds, and any other action's
/// parameters land in <see cref="RulesetActionParameters.AdditionalData"/> where they can be read as they
/// came rather than guessed at.
/// </para>
/// <para>
/// Rules are matched in the order they appear in <see cref="Ruleset.Rules"/>, and a rule's own
/// <see cref="Version"/> records the ruleset version it last changed in.
/// </para>
/// </remarks>
public sealed class RulesetRule
{
    /// <summary>The rule's id, which an edit or a delete addresses.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>What the rule does when it matches, e.g. <c>redirect</c>, <c>block</c>, <c>execute</c>.</summary>
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    /// <summary>
    /// The Wirefilter expression that decides whether the rule matches, e.g.
    /// <c>starts_with(http.request.uri.path, "/old")</c>. <c>true</c> matches everything.
    /// </summary>
    [JsonPropertyName("expression")]
    public string? Expression { get; set; }

    /// <summary>Free text, shown in the dashboard.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Whether the rule runs at all. A disabled rule still counts as a rule.</summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    /// <summary>The ruleset version this rule last changed in.</summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>When the rule last changed.</summary>
    [JsonPropertyName("last_updated")]
    public DateTimeOffset? LastUpdated { get; set; }

    /// <summary>
    /// The reference a managed rule is known by, e.g. <c>cloudflare-engine</c>. Rules you write have none.
    /// </summary>
    [JsonPropertyName("ref")]
    public string? Ref { get; set; }

    /// <summary>Categories a managed rule belongs to. Absent on rules you write.</summary>
    [JsonPropertyName("categories")]
    public List<string>? Categories { get; set; }

    /// <summary>Whether matching this rule is logged.</summary>
    [JsonPropertyName("logging")]
    public RulesetRuleLogging? Logging { get; set; }

    /// <summary>What the action needs to do its job. Shape depends on <see cref="Action"/>.</summary>
    [JsonPropertyName("action_parameters")]
    public RulesetActionParameters? ActionParameters { get; set; }

    /// <summary>
    /// Captures any field Cloudflare adds in the future - which for a rule usually means another action's
    /// own settings, such as <c>ratelimit</c> or <c>exposed_credential_check</c>.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>True when the rule redirects, the Bulk Redirects case.</summary>
    [JsonIgnore]
    public bool IsRedirect => string.Equals(Action, "redirect", StringComparison.Ordinal);

    /// <summary>True when the rule runs another ruleset.</summary>
    [JsonIgnore]
    public bool IsExecute => string.Equals(Action, "execute", StringComparison.Ordinal);

    /// <summary>Action, expression and whether it is enabled, on one line.</summary>
    /// <returns>The rule described in one line.</returns>
    public override string ToString() =>
        Enabled is false ? $"{Action} (disabled): {Expression}" : $"{Action}: {Expression}";
}

/// <summary>Whether matching a rule is logged.</summary>
public sealed class RulesetRuleLogging
{
    /// <summary>True to log matches of this rule.</summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>
/// What a rule's action needs. Only the redirect parameters are declared here; every other action's
/// parameters are captured in <see cref="AdditionalData"/> as they came.
/// </summary>
/// <remarks>
/// A redirect rule points at its targets one of two ways, and never both:
/// <list type="bullet">
///   <item><description>
///     <see cref="FromList"/> - Bulk Redirects. The URLs come from a <see cref="RulesList"/> named here, so
///     one rule can serve thousands of entries.
///   </description></item>
///   <item><description>
///     <see cref="FromValue"/> - Dynamic Redirect. The target is computed per request from an expression.
///   </description></item>
/// </list>
/// An <c>execute</c> rule names the ruleset to run in its own <c>id</c>, <c>version</c> and <c>overrides</c>
/// parameters, which arrive in <see cref="AdditionalData"/> until a caller needs them typed.
/// </remarks>
public sealed class RulesetActionParameters
{
    /// <summary>Bulk Redirects: which list holds the source and target URLs.</summary>
    [JsonPropertyName("from_list")]
    public RulesetRedirectFromList? FromList { get; set; }

    /// <summary>Dynamic Redirect: how to compute the target per request.</summary>
    [JsonPropertyName("from_value")]
    public RulesetRedirectFromValue? FromValue { get; set; }

    /// <summary>
    /// Captures the parameters of every other action, and any field Cloudflare adds in the future.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>True when the parameters send traffic to a list, the Bulk Redirects case.</summary>
    [JsonIgnore]
    public bool IsFromList => FromList is not null;

    /// <summary>Which way the redirect finds its target, or <c>null</c> when this is not a redirect.</summary>
    /// <returns>The redirect's parameters as text.</returns>
    public override string ToString() =>
        FromList is { } list ? list.ToString()
        : FromValue is { } value ? value.ToString()
        : AdditionalData is { Count: > 0 } ? string.Join(", ", AdditionalData.Keys)
        : "(no parameters)";
}

/// <summary>A redirect whose URLs come from a list.</summary>
/// <remarks>
/// <see cref="Name"/> is the list's <em>name</em>, not its id, because that is how rules refer to lists -
/// which is also why renaming is not something the API offers. <see cref="Key"/> says which field of each
/// list item supplies the target; for a redirect list it is <c>target_url</c>.
/// </remarks>
public sealed class RulesetRedirectFromList
{
    /// <summary>The name of the list to read the redirects from.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Which field of each item is the target: <c>target_url</c> for a redirect list.</summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The list name and the key taken from it.</summary>
    /// <returns>The redirect source as text.</returns>
    public override string ToString() => $"from list {Name} ({Key})";
}

/// <summary>A redirect whose target is computed from the request.</summary>
public sealed class RulesetRedirectFromValue
{
    /// <summary>How to build the target URL.</summary>
    [JsonPropertyName("target_url")]
    public RulesetRedirectTargetUrl? TargetUrl { get; set; }

    /// <summary>True to keep the original query string on the target.</summary>
    [JsonPropertyName("preserve_query_string")]
    public bool? PreserveQueryString { get; set; }

    /// <summary>The status to answer with: 301, 302, 303, 307 or 308.</summary>
    [JsonPropertyName("status_code")]
    public int? StatusCode { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The target and the status, on one line.</summary>
    /// <returns>The redirect as text.</returns>
    public override string ToString() =>
        StatusCode is { } code ? $"{TargetUrl} -{code}->" : $"{TargetUrl} ->";
}

/// <summary>The target of a computed redirect: a literal value or an expression.</summary>
public sealed class RulesetRedirectTargetUrl
{
    /// <summary>A literal target URL.</summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    /// <summary>A Wirefilter expression that produces the target URL per request.</summary>
    [JsonPropertyName("expression")]
    public string? Expression { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Whichever of the two was given.</summary>
    /// <returns>The target as text.</returns>
    public override string ToString() => Value ?? Expression ?? string.Empty;
}

/// <summary>Creates a ruleset: <c>POST {accounts_or_zones}/{id}/rulesets</c>.</summary>
/// <remarks>
/// <see cref="Rules"/> may be included here, so a ruleset and its first rules can be created in one call -
/// which also means one version instead of several. A <c>root</c> ruleset can only be created where none
/// exists for that phase yet; a <c>custom</c> one can always be created and does nothing until a root
/// ruleset executes it.
/// </remarks>
public sealed class CreateRulesetRequest
{
    /// <summary>The ruleset's name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>One of the <see cref="RulesetKind"/> values.</summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    /// <summary>One of the <see cref="RulesetPhase"/> values.</summary>
    [JsonPropertyName("phase")]
    public string? Phase { get; set; }

    /// <summary>Free text.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>The rules to create it with. Optional; they can be appended afterwards.</summary>
    [JsonPropertyName("rules")]
    public List<CreateRulesetRuleRequest>? Rules { get; set; }
}

/// <summary>Replaces a ruleset: <c>PUT {accounts_or_zones}/{id}/rulesets/{ruleset_id}</c>.</summary>
/// <remarks>
/// This is a whole-ruleset replacement, not a patch: whatever is sent becomes the ruleset, so an update that
/// leaves <see cref="Rules"/> out removes every rule. Read the ruleset first, change what needs changing,
/// and send the whole thing back.
/// </remarks>
public sealed class UpdateRulesetRequest
{
    /// <summary>The new name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>The new description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>The new kind.</summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    /// <summary>The new phase.</summary>
    [JsonPropertyName("phase")]
    public string? Phase { get; set; }

    /// <summary>
    /// The rules the ruleset should hold afterwards. Omitting this empties the ruleset rather than leaving
    /// it alone.
    /// </summary>
    [JsonPropertyName("rules")]
    public List<CreateRulesetRuleRequest>? Rules { get; set; }
}

/// <summary>
/// A rule to create, or the new state of a rule to edit:
/// <c>POST …/rulesets/{ruleset_id}/rules</c> and <c>PATCH …/rulesets/{ruleset_id}/rules/{rule_id}</c>.
/// </summary>
/// <remarks>
/// One type serves both because the serializer omits what is unset: a create sends everything it is given,
/// and an edit sends only the fields being changed. An edit that sets nothing sends an empty object, which
/// Cloudflare refuses, so this library refuses it first.
/// </remarks>
public sealed class CreateRulesetRuleRequest
{
    /// <summary>What the rule does, e.g. <c>redirect</c>, <c>block</c>, <c>skip</c>, <c>execute</c>.</summary>
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    /// <summary>The Wirefilter expression deciding whether it matches. <c>true</c> matches everything.</summary>
    [JsonPropertyName("expression")]
    public string? Expression { get; set; }

    /// <summary>Free text, shown in the dashboard.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Whether the rule runs. Defaults to true when Cloudflare creates it.</summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    /// <summary>Whether matches are logged.</summary>
    [JsonPropertyName("logging")]
    public RulesetRuleLogging? Logging { get; set; }

    /// <summary>What the action needs. For a redirect, one of <c>from_list</c> or <c>from_value</c>.</summary>
    [JsonPropertyName("action_parameters")]
    public RulesetActionParameters? ActionParameters { get; set; }

    /// <summary>Builds a Bulk Redirects rule: every URL in one list, redirected by one rule.</summary>
    /// <param name="listName">The name of a redirect list in the same account.</param>
    /// <param name="expression">Which requests the rule applies to. <c>true</c> applies it to all of them.</param>
    /// <param name="description">Optional text for the dashboard.</param>
    /// <param name="enabled">Whether the rule runs; <c>null</c> leaves Cloudflare's default.</param>
    /// <returns>A rule whose redirects come from a list.</returns>
    /// <remarks>
    /// The key is <c>target_url</c>, which is the field of a redirect list item that holds the target. The
    /// list has to exist first and be of kind <c>redirect</c>.
    /// </remarks>
    public static CreateRulesetRuleRequest ForRedirectFromList(
        string listName,
        string expression = "true",
        string? description = null,
        bool? enabled = null) =>
        new()
        {
            Action = "redirect",
            Expression = expression,
            Description = description,
            Enabled = enabled,
            ActionParameters = new RulesetActionParameters
            {
                FromList = new RulesetRedirectFromList { Name = listName, Key = "target_url" },
            },
        };
}

/// <summary>
/// Filters for listing rulesets: <c>GET {accounts_or_zones}/{id}/rulesets</c>.
/// </summary>
/// <remarks>
/// The endpoint pages by cursor, and takes nothing but the cursor and the page size - there is no filter by
/// kind or phase, so narrowing down is the caller's job over what comes back.
/// </remarks>
public sealed class RulesetListParameters : CloudflareCursorParameters
{
}
