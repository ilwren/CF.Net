using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Http;
using CFNet.Models.Common;
using CFNet.Serialization;

namespace CFNet.Models.Firewall;

/// <summary>
/// An IP Access rule: one thing to recognise a request by — an address, a range, an autonomous system number or
/// a country — and one thing to do about it.
/// </summary>
/// <remarks>
/// <para>
/// Cloudflare serves the same resource from an account path and from a zone path, and an account-level rule
/// applies to every zone in the account. Its own schema spells the scope <c>{accounts_or_zones}</c>, so the
/// service that wraps it takes both ids and works against a zone only when one is passed explicitly.
/// </para>
/// <para>
/// <b><see cref="Mode"/> is what the dashboard calls an action, and one of its words is older than the
/// dashboard.</b> Cloudflare still answers <c>whitelist</c> for the mode that skips the other checks, where the
/// product now says "skip" — the wire word is what <see cref="AccessRuleMode.Whitelist"/> carries.
/// </para>
/// <para>
/// <b><see cref="AllowedModes"/> is Cloudflare's own answer to what this rule may be switched to</b>, and it is
/// not the whole vocabulary: which modes an account may use depends on its plan and on the rule's scope. So a
/// change is worth checking against <see cref="Allows"/> before it is sent.
/// </para>
/// </remarks>
public sealed class AccessRule
{
    /// <summary>The rule's identifier.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>What to do with a request this rule matches.</summary>
    [JsonPropertyName("mode")]
    public AccessRuleMode? Mode { get; set; }

    /// <summary>The modes Cloudflare says this rule may be switched to, which is not always all of them.</summary>
    [JsonPropertyName("allowed_modes")]
    public List<AccessRuleMode>? AllowedModes { get; set; }

    /// <summary>What the rule recognises a request by.</summary>
    [JsonPropertyName("configuration")]
    public AccessRuleConfiguration? Configuration { get; set; }

    /// <summary>A note for whoever reads the rule later.</summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>When the rule was created, exactly as Cloudflare wrote it.</summary>
    [JsonPropertyName("created_on")]
    public string? CreatedOn { get; set; }

    /// <summary>When the rule was last changed, exactly as Cloudflare wrote it.</summary>
    [JsonPropertyName("modified_on")]
    public string? ModifiedOn { get; set; }

    /// <summary>Whose rule this is: one person, or an organisation.</summary>
    [JsonPropertyName("scope")]
    public AccessRuleScope? Scope { get; set; }

    /// <summary>Fields Cloudflare answers that this version of the library does not model.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary><see cref="CreatedOn"/> parsed, or null when Cloudflare did not answer one it could parse.</summary>
    [JsonIgnore]
    public DateTimeOffset? CreatedOnUtc => CloudflareTimestamps.Read(CreatedOn);

    /// <summary><see cref="ModifiedOn"/> parsed, or null when Cloudflare did not answer one it could parse.</summary>
    [JsonIgnore]
    public DateTimeOffset? ModifiedOnUtc => CloudflareTimestamps.Read(ModifiedOn);

    /// <summary>
    /// Whether Cloudflare says this rule may be switched to a mode, by the <see cref="AllowedModes"/> it
    /// answered. Asking first is cheaper than sending a change and reading the refusal.
    /// </summary>
    /// <param name="mode">The mode to switch to.</param>
    /// <returns>True when the answered list carries it.</returns>
    public bool Allows(AccessRuleMode mode) => AllowedModes?.Contains(mode) == true;

    /// <summary>Names the rule by what it matches and what it does.</summary>
    /// <returns>A one-line description of the rule.</returns>
    public override string ToString()
    {
        var matches = Configuration is { } configuration ? configuration.ToString() : "nothing";
        var mode = Mode is { } action && action != AccessRuleMode.Unknown
            ? CloudflareEnumConverter<AccessRuleMode>.ToWire(action)
            : "an unpublished mode";
        var note = Notes is { Length: > 0 } text ? $" - {text}" : string.Empty;
        return $"access rule {Id ?? "(no id)"}: {mode} {matches}{note}";
    }
}

/// <summary>What to do with a request an access rule matches.</summary>
[JsonConverter(typeof(CloudflareEnumConverter<AccessRuleMode>))]
public enum AccessRuleMode
{
    /// <summary>Value Cloudflare returned that this version of the library does not know.</summary>
    Unknown = 0,

    /// <summary>Refuse the request.</summary>
    [CloudflareValue("block")]
    Block = 1,

    /// <summary>Show a challenge, whichever kind Cloudflare picks.</summary>
    [CloudflareValue("challenge")]
    Challenge = 2,

    /// <summary>
    /// Let the request through and skip the other checks. The wire word is Cloudflare's older one: the product
    /// now calls this "skip", and the API still says <c>whitelist</c>.
    /// </summary>
    [CloudflareValue("whitelist")]
    Whitelist = 3,

    /// <summary>Show a JavaScript challenge.</summary>
    [CloudflareValue("js_challenge")]
    JavaScriptChallenge = 4,

    /// <summary>Show a managed challenge, which may be a Turnstile or a JavaScript one.</summary>
    [CloudflareValue("managed_challenge")]
    ManagedChallenge = 5,
}

/// <summary>What an access rule recognises a request by.</summary>
[JsonConverter(typeof(CloudflareEnumConverter<AccessRuleTarget>))]
public enum AccessRuleTarget
{
    /// <summary>Value Cloudflare returned that this version of the library does not know.</summary>
    Unknown = 0,

    /// <summary>One IPv4 address.</summary>
    [CloudflareValue("ip")]
    Ip = 1,

    /// <summary>One IPv6 address.</summary>
    [CloudflareValue("ip6")]
    Ipv6 = 2,

    /// <summary>A range of addresses, written with a prefix length.</summary>
    [CloudflareValue("ip_range")]
    IpRange = 3,

    /// <summary>An autonomous system number.</summary>
    [CloudflareValue("asn")]
    Asn = 4,

    /// <summary>A country, by its two-letter ISO-3166-1 alpha-2 code.</summary>
    [CloudflareValue("country")]
    Country = 5,
}

/// <summary>
/// What a <i>list</i> of access rules may be filtered by — the same words as <see cref="AccessRuleTarget"/>
/// minus one.
/// </summary>
/// <remarks>
/// Cloudflare types the list's filter separately from the rule's own configuration, and its vocabulary is
/// shorter: <c>ip6</c> is not in it, so a list cannot be narrowed to one IPv6 address. That is worth encoding
/// rather than passing through, because a caller who picks <c>ip6</c> here would get a refusal from Cloudflare
/// and no hint that the filter was never offered. Listing without the filter and narrowing locally is the way
/// to do it.
/// </remarks>
[JsonConverter(typeof(CloudflareEnumConverter<AccessRuleFilterTarget>))]
public enum AccessRuleFilterTarget
{
    /// <summary>Value Cloudflare returned that this version of the library does not know.</summary>
    Unknown = 0,

    /// <summary>One IPv4 address.</summary>
    [CloudflareValue("ip")]
    Ip = 1,

    /// <summary>A range of addresses.</summary>
    [CloudflareValue("ip_range")]
    IpRange = 2,

    /// <summary>An autonomous system number.</summary>
    [CloudflareValue("asn")]
    Asn = 3,

    /// <summary>A country code.</summary>
    [CloudflareValue("country")]
    Country = 4,
}

/// <summary>What an access rule matches, as a target and a value.</summary>
/// <remarks>
/// Cloudflare publishes five separate shapes for this — one per target — and every one of them is the same two
/// fields, so this library has one type and an enum for the target rather than five types that differ only in a
/// word. What the value has to look like depends on that word, and the factories name the five cases.
/// </remarks>
public sealed class AccessRuleConfiguration
{
    /// <summary>Which kind of thing the value is.</summary>
    [JsonPropertyName("target")]
    public AccessRuleTarget? Target { get; set; }

    /// <summary>The thing itself: an address, a range, an AS number or a country code.</summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    /// <summary>
    /// Anything else Cloudflare sent inside the configuration. <c>target</c> and <c>value</c> are the whole of
    /// it today, so this stays empty - and a new target kind, or a field that qualifies one, arrives here
    /// first instead of being dropped, which is how the integration test learns to model it.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Builds a configuration matching one IPv4 address.</summary>
    /// <param name="address">The address to match.</param>
    /// <returns>A configuration ready to send.</returns>
    public static AccessRuleConfiguration ForIp(string address) =>
        Build(AccessRuleTarget.Ip, address);

    /// <summary>Builds a configuration matching one IPv6 address.</summary>
    /// <param name="address">The address to match.</param>
    /// <returns>A configuration ready to send.</returns>
    public static AccessRuleConfiguration ForIpv6(string address) =>
        Build(AccessRuleTarget.Ipv6, address);

    /// <summary>
    /// Builds a configuration matching a range of addresses. Cloudflare publishes the prefix lengths it takes:
    /// <c>/16</c> and <c>/24</c> for IPv4, and <c>/32</c>, <c>/48</c> and <c>/64</c> for IPv6.
    /// </summary>
    /// <param name="range">The range, written as an address and a prefix length.</param>
    /// <returns>A configuration ready to send.</returns>
    public static AccessRuleConfiguration ForRange(string range) =>
        Build(AccessRuleTarget.IpRange, range);

    /// <summary>Builds a configuration matching an autonomous system number.</summary>
    /// <param name="asn">The AS number, as Cloudflare wants it written.</param>
    /// <returns>A configuration ready to send.</returns>
    public static AccessRuleConfiguration ForAsn(string asn) =>
        Build(AccessRuleTarget.Asn, asn);

    /// <summary>Builds a configuration matching a country.</summary>
    /// <param name="code">The two-letter ISO-3166-1 alpha-2 code.</param>
    /// <returns>A configuration ready to send.</returns>
    public static AccessRuleConfiguration ForCountry(string code) =>
        Build(AccessRuleTarget.Country, code);

    private static AccessRuleConfiguration Build(AccessRuleTarget target, string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new AccessRuleConfiguration { Target = target, Value = value.Trim() };
    }

    /// <summary>
    /// Refuses a configuration Cloudflare would refuse, naming everything wrong with it at once.
    /// </summary>
    /// <exception cref="CFNet.Exceptions.CloudflareConfigurationException">
    /// Thrown when there is no target, no value, a country code that is not two letters, or a range whose
    /// prefix length is not one of the ones Cloudflare publishes; its <c>Problems</c> lists every reason.
    /// </exception>
    public void EnsureValid()
    {
        var problems = new List<string>();

        if (Target is null or AccessRuleTarget.Unknown)
        {
            problems.Add(
                "an access rule has to say what it matches: ip, ip6, ip_range, asn or country, because the same " +
                "value means a different thing under each.");
        }

        if (string.IsNullOrWhiteSpace(Value))
        {
            problems.Add("an access rule has to say what to match, and this one's value is empty.");
        }
        else if (Target == AccessRuleTarget.Country && Value.Length != 2)
        {
            problems.Add(
                $"a country target takes a two-letter ISO-3166-1 alpha-2 code, and '{Value}' is " +
                $"{Value.Length} character(s).");
        }
        else if (Target == AccessRuleTarget.IpRange)
        {
            var problem = RangeProblem(Value);
            if (problem is not null)
            {
                problems.Add(problem);
            }
        }

        if (problems.Count > 0)
        {
            throw new CFNet.Exceptions.CloudflareConfigurationException(problems);
        }
    }

    /// <summary>
    /// Says what is wrong with a range, by the prefix lengths Cloudflare publishes: <c>/16</c> and <c>/24</c>
    /// for IPv4, and <c>/32</c>, <c>/48</c> and <c>/64</c> for IPv6. Whether the address itself is one or the
    /// other is left to Cloudflare, which is the authority on that and answers at once.
    /// </summary>
    /// <param name="value">The range as a caller wrote it.</param>
    /// <returns>What is wrong with it, or null when the prefix length is one Cloudflare publishes.</returns>
    private static string? RangeProblem(string value)
    {
        var separator = value.LastIndexOf('/');
        if (separator < 0 || separator == value.Length - 1)
        {
            return
                $"the range '{value}' has no prefix length in it: a range is an address, a slash and one of " +
                "the lengths Cloudflare publishes.";
        }

        var prefix = value[(separator + 1)..];
        if (!prefix.All(char.IsDigit) || !int.TryParse(prefix, out var length))
        {
            return $"the range '{value}' has '{prefix}' where a prefix length belongs.";
        }

        // A colon in the address part is what tells an IPv6 range from an IPv4 one, and each has its own list.
        var isIpv6 = value[..separator].Contains(':');
        var published = isIpv6 ? new[] { 32, 48, 64 } : new[] { 16, 24 };
        return published.Contains(length)
            ? null
            : $"the range '{value}' has a prefix length of /{length}, and Cloudflare publishes " +
              $"{string.Join(", ", published.Select(allowed => "/" + allowed))} for " +
              $"{(isIpv6 ? "an IPv6" : "an IPv4")} range.";
    }

    /// <summary>Names what this configuration matches.</summary>
    /// <returns>A one-line description of the configuration.</returns>
    public override string ToString()
    {
        var target = Target is { } kind && kind != AccessRuleTarget.Unknown
            ? CloudflareEnumConverter<AccessRuleTarget>.ToWire(kind)
            : "an unpublished target";
        return $"{target} {Value ?? "(no value)"}";
    }
}

/// <summary>Whose access rule it is: one person's, or an organisation's.</summary>
[JsonConverter(typeof(CloudflareEnumConverter<AccessRuleScopeType>))]
public enum AccessRuleScopeType
{
    /// <summary>Value Cloudflare returned that this version of the library does not know.</summary>
    Unknown = 0,

    /// <summary>A rule one person made.</summary>
    [CloudflareValue("user")]
    User = 1,

    /// <summary>A rule made for an organisation.</summary>
    [CloudflareValue("organization")]
    Organization = 2,
}

/// <summary>The scope Cloudflare answers beside an access rule.</summary>
public sealed class AccessRuleScope
{
    /// <summary>The identifier of whoever owns the rule.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Their email address, when the rule is one person's.</summary>
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    /// <summary>Whether the owner is a person or an organisation.</summary>
    [JsonPropertyName("type")]
    public AccessRuleScopeType? Type { get; set; }

    /// <summary>Fields Cloudflare answers that this version of the library does not model.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Names the owner of the rule.</summary>
    /// <returns>A one-line description of the scope.</returns>
    public override string ToString()
    {
        var kind = Type is { } type && type != AccessRuleScopeType.Unknown
            ? CloudflareEnumConverter<AccessRuleScopeType>.ToWire(type)
            : "an unpublished kind";
        var who = Email is { Length: > 0 } email ? email : Id ?? "(nobody named)";
        return $"{kind} {who}";
    }
}

/// <summary>
/// The body of an access rule create and of its <c>PATCH</c>. The two are the same shape, and Cloudflare
/// publishes <c>configuration</c> and <c>mode</c> as required for both — there is no <c>PUT</c> here, so a
/// change always carries the whole rule rather than the one field being changed.
/// </summary>
public sealed class AccessRuleRequest
{
    /// <summary>What the rule matches.</summary>
    [JsonPropertyName("configuration")]
    public AccessRuleConfiguration? Configuration { get; set; }

    /// <summary>What to do with a request that matches.</summary>
    [JsonPropertyName("mode")]
    public AccessRuleMode? Mode { get; set; }

    /// <summary>A note for whoever reads the rule later.</summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>Builds a request for one rule.</summary>
    /// <param name="configuration">What the rule matches.</param>
    /// <param name="mode">What to do with a request that matches.</param>
    /// <param name="notes">A note; none when null.</param>
    /// <returns>A request ready to send.</returns>
    public static AccessRuleRequest For(
        AccessRuleConfiguration configuration,
        AccessRuleMode mode,
        string? notes = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new AccessRuleRequest { Configuration = configuration, Mode = mode, Notes = notes };
    }

    /// <summary>Builds a request that blocks one thing.</summary>
    /// <param name="configuration">What to block.</param>
    /// <param name="notes">A note; none when null.</param>
    /// <returns>A request ready to send.</returns>
    public static AccessRuleRequest Blocking(AccessRuleConfiguration configuration, string? notes = null) =>
        For(configuration, AccessRuleMode.Block, notes);

    /// <summary>Builds a request that lets one thing through and skips the other checks for it.</summary>
    /// <param name="configuration">What to let through.</param>
    /// <param name="notes">A note; none when null.</param>
    /// <returns>A request ready to send.</returns>
    /// <remarks>
    /// The mode is <see cref="AccessRuleMode.Whitelist"/>, which is the wire word for what the dashboard now
    /// calls "skip".
    /// </remarks>
    public static AccessRuleRequest Skipping(AccessRuleConfiguration configuration, string? notes = null) =>
        For(configuration, AccessRuleMode.Whitelist, notes);

    /// <summary>Refuses a rule Cloudflare would refuse, naming everything wrong with it at once.</summary>
    /// <exception cref="CFNet.Exceptions.CloudflareConfigurationException">
    /// Thrown when there is no configuration, no mode, or a configuration Cloudflare would refuse; its
    /// <c>Problems</c> lists every reason.
    /// </exception>
    public void EnsureValid()
    {
        var problems = new List<string>();

        if (Configuration is null)
        {
            problems.Add("an access rule needs a configuration: what it matches is half of what it is.");
        }
        else
        {
            try
            {
                Configuration.EnsureValid();
            }
            catch (CFNet.Exceptions.CloudflareConfigurationException exception)
            {
                problems.AddRange(exception.Problems);
            }
        }

        if (Mode is null or AccessRuleMode.Unknown)
        {
            problems.Add(
                "an access rule needs a mode: block, challenge, js_challenge, managed_challenge or whitelist, " +
                "because a rule that matches something has to say what happens next.");
        }

        if (problems.Count > 0)
        {
            throw new CFNet.Exceptions.CloudflareConfigurationException(problems);
        }
    }

    /// <summary>Names the rule this request would save.</summary>
    /// <returns>A one-line description of the request.</returns>
    public override string ToString()
    {
        var matches = Configuration is { } configuration ? configuration.ToString() : "nothing";
        var mode = Mode is { } action && action != AccessRuleMode.Unknown
            ? CloudflareEnumConverter<AccessRuleMode>.ToWire(action)
            : "no mode";
        return $"access rule request: {mode} {matches}";
    }
}

/// <summary>What an access rule list may be filtered by, as one query parameter.</summary>
public sealed class AccessRuleListConfiguration
{
    /// <summary>Which kind of thing to look for. <c>ip6</c> is not offered here.</summary>
    public AccessRuleFilterTarget? Target { get; set; }

    /// <summary>The value to look for.</summary>
    public string? Value { get; set; }

    /// <summary>Builds a filter for one kind of target.</summary>
    /// <param name="target">Which kind of thing to look for.</param>
    /// <param name="value">The value to look for.</param>
    /// <returns>A filter ready to apply.</returns>
    public static AccessRuleListConfiguration Of(AccessRuleFilterTarget target, string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new AccessRuleListConfiguration { Target = target, Value = value.Trim() };
    }
}

/// <summary>
/// Query parameters for the access rule list, which pages by page and per_page and can be narrowed by mode, by
/// note, and by the configuration itself.
/// </summary>
/// <remarks>
/// The configuration filter is sent as two dotted names — <c>configuration.target</c> and
/// <c>configuration.value</c> — which is how Cloudflare takes a nested filter in a query string, and the same
/// way the zone list takes <c>account.id</c>.
/// </remarks>
public sealed class AccessRuleListParameters : CloudflarePagedParameters
{
    /// <summary>Only rules matching this configuration.</summary>
    public AccessRuleListConfiguration? Configuration { get; set; }

    /// <summary>Only rules with this mode.</summary>
    public AccessRuleMode? Mode { get; set; }

    /// <summary>Only rules carrying this note.</summary>
    public string? Notes { get; set; }

    /// <summary>Field to order the list by.</summary>
    public AccessRuleOrder? Order { get; set; }

    /// <summary>Sort direction.</summary>
    public SortDirection? Direction { get; set; }

    /// <summary>Whether every filter has to match or only one of them.</summary>
    public FilterMatchMode? Match { get; set; }

    /// <inheritdoc />
    public override void Apply(QueryStringBuilder query)
    {
        base.Apply(query);

        if (Configuration is { } configuration)
        {
            if (configuration.Target is { } target && target != AccessRuleFilterTarget.Unknown)
            {
                query.Add("configuration.target", CloudflareEnumConverter<AccessRuleFilterTarget>.ToWire(target));
            }

            query.AddIfNotEmpty("configuration.value", configuration.Value);
        }

        if (Mode is { } mode && mode != AccessRuleMode.Unknown)
        {
            query.Add("mode", CloudflareEnumConverter<AccessRuleMode>.ToWire(mode));
        }

        query.AddIfNotEmpty("notes", Notes);

        if (Order is { } order && order != AccessRuleOrder.Unknown)
        {
            query.Add("order", CloudflareEnumConverter<AccessRuleOrder>.ToWire(order));
        }

        if (Direction.HasValue)
        {
            query.Add("direction", QueryEnums.ToWire(Direction.Value));
        }

        if (Match.HasValue)
        {
            query.Add("match", QueryEnums.ToWire(Match.Value));
        }
    }
}

/// <summary>What an access rule list may be ordered by.</summary>
[JsonConverter(typeof(CloudflareEnumConverter<AccessRuleOrder>))]
public enum AccessRuleOrder
{
    /// <summary>Value Cloudflare returned that this version of the library does not know.</summary>
    Unknown = 0,

    /// <summary>By the kind of thing the rule matches.</summary>
    [CloudflareValue("configuration.target")]
    ConfigurationTarget = 1,

    /// <summary>By the thing itself.</summary>
    [CloudflareValue("configuration.value")]
    ConfigurationValue = 2,

    /// <summary>By what the rule does.</summary>
    [CloudflareValue("mode")]
    Mode = 3,
}

/// <summary>What an access rule delete answers: the identifier of the rule that is gone.</summary>
public sealed class AccessRuleDeletion
{
    /// <summary>The identifier of the rule that was removed.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Fields Cloudflare answers that this version of the library does not model.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Names the rule that was removed.</summary>
    /// <returns>A one-line description of the deletion.</returns>
    public override string ToString() => $"deleted access rule {Id ?? "(no id)"}";
}
