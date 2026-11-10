using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Exceptions;
using CFNet.Http;
using CFNet.Serialization;

namespace CFNet.Models.Radar;

/// <summary>What to group a radar bot summary or series by.</summary>
/// <remarks>
/// A dimension is part of the path rather than a filter — <c>/radar/bots/summary/BOT</c> — and Cloudflare
/// publishes four of them and no others, so it is typed: a value this library cannot spell has no path to be
/// put in.
/// </remarks>
[JsonConverter(typeof(CloudflareEnumConverter<RadarBotDimension>))]
public enum RadarBotDimension
{
    /// <summary>A value this version of the library has not met. Sending one is not possible.</summary>
    Unknown = 0,

    /// <summary>One entry per bot.</summary>
    [CloudflareValue("BOT")]
    Bot = 1,

    /// <summary>One entry per kind of bot - an agent or a bot.</summary>
    [CloudflareValue("BOT_KIND")]
    BotKind = 2,

    /// <summary>One entry per whoever runs the bot.</summary>
    [CloudflareValue("BOT_OPERATOR")]
    BotOperator = 3,

    /// <summary>One entry per category of bot.</summary>
    [CloudflareValue("BOT_CATEGORY")]
    BotCategory = 4,
}

/// <summary>What to group a radar web-crawler summary or series by.</summary>
/// <remarks>
/// The crawler endpoints are the bot ones narrowed to traffic that identifies itself, and their dimensions are
/// their own: what the client was, what it sent as its user agent or referer, and how the origin answered.
/// </remarks>
[JsonConverter(typeof(CloudflareEnumConverter<RadarWebCrawlerDimension>))]
public enum RadarWebCrawlerDimension
{
    /// <summary>A value this version of the library has not met. Sending one is not possible.</summary>
    Unknown = 0,

    /// <summary>One entry per client type - human, AI bot, non-AI bot, or mixed purpose.</summary>
    [CloudflareValue("CLIENT_TYPE")]
    ClientType = 1,

    /// <summary>One entry per user agent.</summary>
    [CloudflareValue("USER_AGENT")]
    UserAgent = 2,

    /// <summary>One entry per referer.</summary>
    [CloudflareValue("REFERER")]
    Referer = 3,

    /// <summary>One entry per ratio of crawled to referred requests.</summary>
    [CloudflareValue("CRAWL_REFER_RATIO")]
    CrawlReferRatio = 4,

    /// <summary>One entry per vertical.</summary>
    [CloudflareValue("VERTICAL")]
    Vertical = 5,

    /// <summary>One entry per industry.</summary>
    [CloudflareValue("INDUSTRY")]
    Industry = 6,

    /// <summary>One entry per HTTP response status.</summary>
    [CloudflareValue("RESPONSE_STATUS")]
    ResponseStatus = 7,

    /// <summary>One entry per HTTP response status category.</summary>
    [CloudflareValue("RESPONSE_STATUS_CATEGORY")]
    ResponseStatusCategory = 8,
}

/// <summary>Which kind of automated client a bot is.</summary>
/// <remarks>
/// Cloudflare's own two words for it: an <c>AGENT</c> acts on somebody's behalf - an AI assistant fetching a
/// page for a person who asked - where a <c>BOT</c> acts on its own.
/// </remarks>
[JsonConverter(typeof(CloudflareEnumConverter<RadarBotKind>))]
public enum RadarBotKind
{
    /// <summary>A value this version of the library has not met. Sending one is not possible.</summary>
    Unknown = 0,

    /// <summary>A client acting on somebody's behalf.</summary>
    [CloudflareValue("AGENT")]
    Agent = 1,

    /// <summary>A client acting on its own.</summary>
    [CloudflareValue("BOT")]
    Bot = 2,
}

/// <summary>What a bot is for, in Cloudflare's categories.</summary>
/// <remarks>
/// A filter, so it is typed - and the answer beside it is not: <see cref="RadarBot.Category"/> stays the string
/// Cloudflare sends, because a category added tomorrow should read as itself rather than as
/// <see cref="Unknown"/>.
/// </remarks>
[JsonConverter(typeof(CloudflareEnumConverter<RadarBotCategory>))]
public enum RadarBotCategory
{
    /// <summary>A value this version of the library has not met. Sending one is not possible.</summary>
    Unknown = 0,

    /// <summary>A search engine's crawler.</summary>
    [CloudflareValue("SEARCH_ENGINE_CRAWLER")]
    SearchEngineCrawler = 1,

    /// <summary>A client working on search-engine optimisation.</summary>
    [CloudflareValue("SEARCH_ENGINE_OPTIMIZATION")]
    SearchEngineOptimization = 2,

    /// <summary>Monitoring or analytics.</summary>
    [CloudflareValue("MONITORING_AND_ANALYTICS")]
    MonitoringAndAnalytics = 3,

    /// <summary>Advertising or marketing.</summary>
    [CloudflareValue("ADVERTISING_AND_MARKETING")]
    AdvertisingAndMarketing = 4,

    /// <summary>Social-media marketing.</summary>
    [CloudflareValue("SOCIAL_MEDIA_MARKETING")]
    SocialMediaMarketing = 5,

    /// <summary>Fetching a page to preview it - a link unfurled in a chat, say.</summary>
    [CloudflareValue("PAGE_PREVIEW")]
    PagePreview = 6,

    /// <summary>Academic research.</summary>
    [CloudflareValue("ACADEMIC_RESEARCH")]
    AcademicResearch = 7,

    /// <summary>Security scanning.</summary>
    [CloudflareValue("SECURITY")]
    Security = 8,

    /// <summary>Accessibility - reading a page aloud or enlarging it.</summary>
    [CloudflareValue("ACCESSIBILITY")]
    Accessibility = 9,

    /// <summary>Calling a webhook.</summary>
    [CloudflareValue("WEBHOOKS")]
    Webhooks = 10,

    /// <summary>Fetching a feed.</summary>
    [CloudflareValue("FEED_FETCHER")]
    FeedFetcher = 11,

    /// <summary>Crawling for an AI model's training data.</summary>
    [CloudflareValue("AI_CRAWLER")]
    AiCrawler = 12,

    /// <summary>Aggregating content from elsewhere.</summary>
    [CloudflareValue("AGGREGATOR")]
    Aggregator = 13,

    /// <summary>An AI assistant acting for a person.</summary>
    [CloudflareValue("AI_ASSISTANT")]
    AiAssistant = 14,

    /// <summary>An AI search.</summary>
    [CloudflareValue("AI_SEARCH")]
    AiSearch = 15,

    /// <summary>Archiving.</summary>
    [CloudflareValue("ARCHIVER")]
    Archiver = 16,
}

/// <summary>Whether Cloudflare has verified a bot as being what it says it is.</summary>
/// <remarks>
/// Cloudflare publishes one value for this filter — <c>VERIFIED</c>, which narrows a list to the bots whose
/// identity it has checked. It is an enum of one because a filter is worth spelling for the caller, and because
/// a second value has somewhere to go when Cloudflare publishes one.
/// </remarks>
[JsonConverter(typeof(CloudflareEnumConverter<RadarBotVerificationStatus>))]
public enum RadarBotVerificationStatus
{
    /// <summary>A value this version of the library has not met. Sending one is not possible.</summary>
    Unknown = 0,

    /// <summary>A bot whose identity Cloudflare has checked.</summary>
    [CloudflareValue("VERIFIED")]
    Verified = 1,
}

/// <summary>Who a request came from, as the crawler endpoints divide traffic.</summary>
[JsonConverter(typeof(CloudflareEnumConverter<RadarBotClientType>))]
public enum RadarBotClientType
{
    /// <summary>A value this version of the library has not met. Sending one is not possible.</summary>
    Unknown = 0,

    /// <summary>A person.</summary>
    [CloudflareValue("HUMAN")]
    Human = 1,

    /// <summary>A bot that is not fetching for an AI model.</summary>
    [CloudflareValue("NON_AI_BOT")]
    NonAiBot = 2,

    /// <summary>A bot fetching for an AI model.</summary>
    [CloudflareValue("AI_BOT")]
    AiBot = 3,

    /// <summary>A client that does both.</summary>
    [CloudflareValue("MIXED_PURPOSE")]
    MixedPurpose = 4,
}

/// <summary>How an origin answered, in the five classes of HTTP status.</summary>
[JsonConverter(typeof(CloudflareEnumConverter<RadarResponseStatusCategory>))]
public enum RadarResponseStatusCategory
{
    /// <summary>A value this version of the library has not met. Sending one is not possible.</summary>
    Unknown = 0,

    /// <summary>1xx.</summary>
    [CloudflareValue("INFORMATIONAL")]
    Informational = 1,

    /// <summary>2xx.</summary>
    [CloudflareValue("SUCCESS")]
    Success = 2,

    /// <summary>3xx.</summary>
    [CloudflareValue("REDIRECTION")]
    Redirection = 3,

    /// <summary>4xx.</summary>
    [CloudflareValue("CLIENT_ERROR")]
    ClientError = 4,

    /// <summary>5xx.</summary>
    [CloudflareValue("SERVER_ERROR")]
    ServerError = 5,
}

/// <summary>One bot Cloudflare knows by name.</summary>
/// <remarks>
/// <para>
/// One type serves both reads, because they answer the same object with different parts of it filled in: the
/// list carries the fields every entry has, and the single-bot read adds <see cref="OperatorUrl"/>,
/// <see cref="UserAgents"/>, <see cref="SignatureAgentUrl"/>, <see cref="DocumentationUrl"/> and
/// <see cref="OperatorWebsiteUrl"/>. Those five stay <see langword="null"/> on an entry that came from the
/// list - the first three because Cloudflare's own two schemas say so, and the last two because a live run
/// watched them arrive on the single read and not on the list.
/// </para>
/// <para>
/// That run also answered three fields the reference does not publish at all: <see cref="Categories"/> beside
/// the single <see cref="Category"/>, <see cref="AccessLevel"/> and <see cref="ContentUse"/> on both reads.
/// They are typed because they were seen filled, which is the rule this library keeps to - a field only ever
/// seen named stays the JSON it arrived as, because a type built on a guess costs the whole read when the
/// guess is wrong.
/// </para>
/// <para>
/// <see cref="Category"/> and <see cref="Kind"/> are the strings Cloudflare answers with, not the enums its
/// filters are typed with: an answer carrying a category this library has not met is worth reading, and turning
/// it into <c>Unknown</c> would drop what Cloudflare said.
/// </para>
/// </remarks>
public sealed class RadarBot
{
    /// <summary>How this bot reaches the sites it asks about.</summary>
    /// <remarks>
    /// Unpublished, and answered as <c>DIRECT</c> by every bot a live run read. Cloudflare publishes no list
    /// of what else it can be, so it stays the string it arrives as: an answer this library cannot enumerate
    /// is not one it should guess at.
    /// </remarks>
    [JsonPropertyName("accessLevel")]
    public string? AccessLevel { get; set; }

    /// <summary>What the bot is for, as Cloudflare spells it.</summary>
    /// <remarks>The one category it leads with. <see cref="Categories"/> is every category it belongs to.</remarks>
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary>Every category this bot belongs to.</summary>
    /// <remarks>
    /// Unpublished, and the reason <see cref="Category"/> on its own is not enough: a live run read one bot
    /// filed under three categories at once, with the single field carrying only the first of them.
    /// </remarks>
    [JsonPropertyName("categories")]
    public List<string>? Categories { get; set; }

    /// <summary>What the bot may do with what it fetches.</summary>
    /// <remarks>
    /// Unpublished, and answered in lower case - <c>immediate</c> - where every category beside it is upper
    /// case, so it stays the string it arrives as rather than an enum built on one sighting.
    /// </remarks>
    [JsonPropertyName("contentUse")]
    public string? ContentUse { get; set; }

    /// <summary>What Cloudflare says the bot does.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>The address of the documentation for this bot.</summary>
    /// <remarks>Unpublished, and answered by the single-bot read only.</remarks>
    [JsonPropertyName("documentationUrl")]
    public string? DocumentationUrl { get; set; }

    /// <summary>Whether it acts for somebody or on its own, as Cloudflare spells it.</summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    /// <summary>What the bot is called.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Whoever runs it.</summary>
    [JsonPropertyName("operator")]
    public string? Operator { get; set; }

    /// <summary>Where to read about whoever runs it. Answered by the single-bot read only.</summary>
    [JsonPropertyName("operatorUrl")]
    public string? OperatorUrl { get; set; }

    /// <summary>The operator's own website, beside the <see cref="OperatorUrl"/> that documents the bot.</summary>
    /// <remarks>Unpublished, and answered by the single-bot read only.</remarks>
    [JsonPropertyName("operatorWebsiteUrl")]
    public string? OperatorWebsiteUrl { get; set; }

    /// <summary>
    /// The address of the agent that signs this bot's requests, when Cloudflare has one for it. Published as
    /// nullable, and answered by the single-bot read only.
    /// </summary>
    [JsonPropertyName("signatureAgentUrl")]
    public string? SignatureAgentUrl { get; set; }

    /// <summary>The name this bot is read by: <c>GET /radar/bots/{bot_slug}</c>.</summary>
    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    /// <summary>The user-agent patterns that identify it.</summary>
    [JsonPropertyName("userAgentPatterns")]
    public List<string>? UserAgentPatterns { get; set; }

    /// <summary>The user agents themselves. Answered by the single-bot read only.</summary>
    [JsonPropertyName("userAgents")]
    public List<string>? UserAgents { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        (Name ?? "?") +
        (Kind is { Length: > 0 } kind ? $" ({kind})" : string.Empty) +
        (Operator is { Length: > 0 } operatorName ? $" by {operatorName}" : string.Empty);
}

/// <summary>The <c>result</c> of a bot list.</summary>
public sealed class RadarBotListResult
{
    /// <summary>The bots on this page.</summary>
    [JsonPropertyName("bots")]
    public List<RadarBot>? Bots { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>The <c>result</c> of a single-bot read.</summary>
public sealed class RadarBotResult
{
    /// <summary>The bot.</summary>
    [JsonPropertyName("bot")]
    public RadarBot? Bot { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>One of the verified bots Cloudflare sees most.</summary>
/// <remarks>
/// <see cref="Value"/> is published as a string, and stays one: it is a share of traffic that Cloudflare spells
/// however it likes, and a number this library insisted on would fail the whole ranking over one entry.
/// </remarks>
public sealed class RadarVerifiedBot
{
    /// <summary>What the bot is for, as Cloudflare spells it.</summary>
    [JsonPropertyName("botCategory")]
    public string? BotCategory { get; set; }

    /// <summary>What the bot is called.</summary>
    [JsonPropertyName("botName")]
    public string? BotName { get; set; }

    /// <summary>Whoever runs it.</summary>
    [JsonPropertyName("botOwner")]
    public string? BotOwner { get; set; }

    /// <summary>How much of the traffic it was, as Cloudflare spells it.</summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        (BotName ?? "?") +
        (Value is { Length: > 0 } value ? $" {value}" : string.Empty) +
        (BotOwner is { Length: > 0 } owner ? $" by {owner}" : string.Empty);
}

/// <summary>One of the verified-bot categories Cloudflare sees most of.</summary>
public sealed class RadarVerifiedBotCategory
{
    /// <summary>What the category is, as Cloudflare spells it.</summary>
    [JsonPropertyName("botCategory")]
    public string? BotCategory { get; set; }

    /// <summary>How much of the traffic it was, as Cloudflare spells it.</summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        (BotCategory ?? "?") + (Value is { Length: > 0 } value ? $" {value}" : string.Empty);
}

/// <summary>The filters for <c>GET /radar/bots</c>.</summary>
/// <remarks>
/// The bot list takes its filters singly, where the summaries and series beside it take them in lists — one
/// <c>botCategory</c> here, an array of them there — and names the kind filter <c>kind</c> where they name it
/// <c>botKind</c>. Both are Cloudflare's own spellings for the same idea, so this class does not share the
/// others' base: a filter sent under the wrong name is one Cloudflare quietly ignores.
/// </remarks>
public sealed class RadarBotListParameters : ICloudflareQueryParameters
{
    /// <summary>Filters to one category of bot.</summary>
    public RadarBotCategory? BotCategory { get; set; }

    /// <summary>Filters to bots run by this operator.</summary>
    public string? BotOperator { get; set; }

    /// <summary>Filters to the bots Cloudflare has verified.</summary>
    public RadarBotVerificationStatus? BotVerificationStatus { get; set; }

    /// <summary>Filters to one kind of automated client.</summary>
    public RadarBotKind? Kind { get; set; }

    /// <summary>How many bots to return.</summary>
    public int? Limit { get; set; }

    /// <summary>How many bots to skip first.</summary>
    public int? Offset { get; set; }

    /// <inheritdoc />
    public void Apply(QueryStringBuilder query)
    {
        query.AddIfNotEmpty("botCategory", Spell(BotCategory, "bot category"));
        query.AddIfNotEmpty("botOperator", BotOperator);
        query.AddIfNotEmpty("botVerificationStatus", Spell(BotVerificationStatus, "verification status"));
        query.AddIfNotEmpty("kind", Spell(Kind, "bot kind"));
        query.Add("limit", Limit);
        query.Add("offset", Offset);
    }

    /// <summary>A copy of these parameters that starts further along the list.</summary>
    /// <param name="offset">How many bots to skip.</param>
    /// <returns>The new parameters.</returns>
    public RadarBotListParameters WithOffset(int offset) => new()
    {
        BotCategory = BotCategory,
        BotOperator = BotOperator,
        BotVerificationStatus = BotVerificationStatus,
        Kind = Kind,
        Limit = Limit,
        Offset = offset,
    };

    private static string? Spell<TEnum>(TEnum? value, string what)
        where TEnum : struct, Enum
    {
        if (value is not { } filter)
        {
            return null;
        }

        if (!CloudflareEnumConverter<TEnum>.TryToWire(filter, out var wire))
        {
            throw new CloudflareConfigurationException(
                $"'{filter}' is not a {what} this version of the library knows about, so it cannot be sent as " +
                "a filter.");
        }

        return wire;
    }
}

/// <summary>
/// The filters radar's bot summaries and series share: which bots, whose they are, where the traffic was seen,
/// and what period it covers.
/// </summary>
/// <remarks>
/// Every one of them is a list, because a summary answers one series per value a caller asks about — and
/// <see cref="Name"/> is what labels those series, in the order they were asked for.
/// </remarks>
public abstract class RadarBotParameters : ICloudflareQueryParameters
{
    /// <summary>Filters to traffic seen by these ASNs.</summary>
    public List<string>? Asn { get; set; }

    /// <summary>Filters to these bots, by the slug the bot list answers.</summary>
    public List<string>? Bot { get; set; }

    /// <summary>Filters to these categories of bot.</summary>
    public List<RadarBotCategory>? BotCategory { get; set; }

    /// <summary>Filters to these kinds of automated client.</summary>
    public List<RadarBotKind>? BotKind { get; set; }

    /// <summary>Filters to bots run by these operators.</summary>
    public List<string>? BotOperator { get; set; }

    /// <summary>Filters to the bots Cloudflare has verified.</summary>
    public List<RadarBotVerificationStatus>? BotVerificationStatus { get; set; }

    /// <summary>Filters to these continents.</summary>
    public List<string>? Continent { get; set; }

    /// <summary>The starts of the periods to summarise.</summary>
    public List<DateTimeOffset>? DateStart { get; set; }

    /// <summary>The ends of the periods to summarise.</summary>
    public List<DateTimeOffset>? DateEnd { get; set; }

    /// <summary>The periods to summarise, in Cloudflare's own grammar.</summary>
    public List<string>? DateRange { get; set; }

    /// <summary>Filters to these alpha-2 location codes.</summary>
    public List<string>? Location { get; set; }

    /// <summary>Labels for the series in the answer, in the order they were asked for.</summary>
    public List<string>? Name { get; set; }

    /// <inheritdoc />
    public abstract void Apply(QueryStringBuilder query);

    /// <summary>
    /// Checks the request can be sent: every filter this library has a spelling for has one it can use, and a
    /// per-group limit asks for at least one entry.
    /// </summary>
    /// <exception cref="CloudflareConfigurationException">Thrown when a filter cannot be sent.</exception>
    public void EnsureValid()
    {
        var problems = new List<string>();

        RadarQuery.CheckEnums(problems, "bot category", BotCategory);
        RadarQuery.CheckEnums(problems, "bot kind", BotKind);
        RadarQuery.CheckEnums(problems, "bot verification status", BotVerificationStatus);
        AddProblems(problems);

        if (problems.Count > 0)
        {
            throw new CloudflareConfigurationException(problems);
        }
    }

    /// <summary>Adds the problems this endpoint's own filters can have.</summary>
    /// <param name="problems">The problems found so far, to add to.</param>
    protected virtual void AddProblems(List<string> problems)
    {
    }

    /// <summary>Records a per-group limit Cloudflare would refuse.</summary>
    /// <param name="problems">The problems found so far, to add to.</param>
    /// <param name="limitPerGroup">The limit that was asked for.</param>
    protected static void CheckLimitPerGroup(List<string> problems, int? limitPerGroup) =>
        RadarQuery.CheckLimitPerGroup(problems, limitPerGroup);

    /// <summary>Sends the seven bot filters, in the order Cloudflare publishes them.</summary>
    /// <param name="query">The query string to add to.</param>
    protected void ApplyBotFilters(QueryStringBuilder query)
    {
        query.AddMany("asn", Asn);
        query.AddMany("bot", Bot);
        RadarQuery.Enums(query, "botCategory", BotCategory);
        RadarQuery.Enums(query, "botKind", BotKind);
        query.AddMany("botOperator", BotOperator);
        RadarQuery.Enums(query, "botVerificationStatus", BotVerificationStatus);
        query.AddMany("continent", Continent);
    }

    /// <summary>Sends the three period filters, in the order Cloudflare publishes them.</summary>
    /// <param name="query">The query string to add to.</param>
    protected void ApplyDateFilters(QueryStringBuilder query)
    {
        RadarQuery.Timestamps(query, "dateEnd", DateEnd);
        RadarQuery.Ranges(query, "dateRange", DateRange);
        RadarQuery.Timestamps(query, "dateStart", DateStart);
    }

    /// <summary>Sends the two filters that come last on every one of these endpoints.</summary>
    /// <param name="query">The query string to add to.</param>
    protected void ApplyLocationAndNames(QueryStringBuilder query)
    {
        query.AddMany("location", Location);
        query.AddMany("name", Name);
    }
}

/// <summary>The filters for <c>GET /radar/bots/summary/{dimension}</c>.</summary>
public sealed class RadarBotSummaryParameters : RadarBotParameters
{
    /// <summary>How many entries to return per group of the summary.</summary>
    public int? LimitPerGroup { get; set; }

    /// <inheritdoc />
    protected override void AddProblems(List<string> problems) => CheckLimitPerGroup(problems, LimitPerGroup);

    /// <inheritdoc />
    public override void Apply(QueryStringBuilder query)
    {
        ApplyBotFilters(query);
        ApplyDateFilters(query);
        query.Add("limitPerGroup", LimitPerGroup);
        ApplyLocationAndNames(query);
    }
}

/// <summary>The filters for <c>GET /radar/bots/timeseries</c>.</summary>
/// <remarks>
/// This endpoint takes no <c>limitPerGroup</c>: it answers one series per thing the filters named rather than a
/// ranked group of them.
/// </remarks>
public sealed class RadarBotTimeseriesParameters : RadarBotParameters
{
    /// <summary>How widely to aggregate the series.</summary>
    public RadarAggregationInterval? AggInterval { get; set; }

    /// <inheritdoc />
    protected override void AddProblems(List<string> problems) =>
        RadarQuery.CheckEnums(problems, "aggregation interval", AggInterval is { } interval
            ? new List<RadarAggregationInterval> { interval }
            : null);

    /// <inheritdoc />
    public override void Apply(QueryStringBuilder query)
    {
        if (AggInterval is { } interval)
        {
            query.Add("aggInterval", CloudflareEnumConverter<RadarAggregationInterval>.ToWire(interval));
        }

        ApplyBotFilters(query);
        ApplyDateFilters(query);
        ApplyLocationAndNames(query);
    }
}

/// <summary>The filters for <c>GET /radar/bots/timeseries_groups/{dimension}</c>.</summary>
public sealed class RadarBotTimeseriesGroupsParameters : RadarBotParameters
{
    /// <summary>How widely to aggregate the series.</summary>
    public RadarAggregationInterval? AggInterval { get; set; }

    /// <summary>How many entries to return per group of the series.</summary>
    public int? LimitPerGroup { get; set; }

    /// <inheritdoc />
    protected override void AddProblems(List<string> problems)
    {
        RadarQuery.CheckEnums(problems, "aggregation interval", AggInterval is { } interval
            ? new List<RadarAggregationInterval> { interval }
            : null);
        CheckLimitPerGroup(problems, LimitPerGroup);
    }

    /// <inheritdoc />
    public override void Apply(QueryStringBuilder query)
    {
        if (AggInterval is { } interval)
        {
            query.Add("aggInterval", CloudflareEnumConverter<RadarAggregationInterval>.ToWire(interval));
        }

        ApplyBotFilters(query);
        ApplyDateFilters(query);
        query.Add("limitPerGroup", LimitPerGroup);
        ApplyLocationAndNames(query);
    }
}

/// <summary>
/// The filters radar's web-crawler endpoints share: whose crawlers, what kind of client, what the origin
/// answered, and what period it covers.
/// </summary>
public abstract class RadarWebCrawlerParameters : ICloudflareQueryParameters
{
    /// <summary>Filters to crawlers run by these operators.</summary>
    public List<string>? BotOperator { get; set; }

    /// <summary>Filters to these kinds of client - human, AI bot, non-AI bot, or mixed purpose.</summary>
    public List<RadarBotClientType>? ClientType { get; set; }

    /// <summary>The starts of the periods to summarise.</summary>
    public List<DateTimeOffset>? DateStart { get; set; }

    /// <summary>The ends of the periods to summarise.</summary>
    public List<DateTimeOffset>? DateEnd { get; set; }

    /// <summary>The periods to summarise, in Cloudflare's own grammar.</summary>
    public List<string>? DateRange { get; set; }

    /// <summary>Filters to these industries.</summary>
    public List<string>? Industry { get; set; }

    /// <summary>How many entries to return per group.</summary>
    public int? LimitPerGroup { get; set; }

    /// <summary>Labels for the series in the answer, in the order they were asked for.</summary>
    public List<string>? Name { get; set; }

    /// <summary>Filters to these HTTP response statuses.</summary>
    public List<string>? ResponseStatus { get; set; }

    /// <summary>Filters to these classes of HTTP response status.</summary>
    public List<RadarResponseStatusCategory>? ResponseStatusCategory { get; set; }

    /// <summary>Filters to these verticals.</summary>
    public List<string>? Vertical { get; set; }

    /// <inheritdoc />
    public abstract void Apply(QueryStringBuilder query);

    /// <summary>Checks the request can be sent.</summary>
    /// <exception cref="CloudflareConfigurationException">Thrown when a filter cannot be sent.</exception>
    public void EnsureValid()
    {
        var problems = new List<string>();

        RadarQuery.CheckEnums(problems, "client type", ClientType);
        RadarQuery.CheckEnums(problems, "response status category", ResponseStatusCategory);
        AddProblems(problems);

        if (problems.Count > 0)
        {
            throw new CloudflareConfigurationException(problems);
        }
    }

    /// <summary>Adds the problems this endpoint's own filters can have.</summary>
    /// <param name="problems">The problems found so far, to add to.</param>
    protected virtual void AddProblems(List<string> problems)
    {
    }

    /// <summary>Sends every filter these two endpoints share, in the order Cloudflare publishes them.</summary>
    /// <param name="query">The query string to add to.</param>
    protected void ApplyCrawlerFilters(QueryStringBuilder query)
    {
        query.AddMany("botOperator", BotOperator);
        RadarQuery.Enums(query, "clientType", ClientType);
        RadarQuery.Timestamps(query, "dateEnd", DateEnd);
        RadarQuery.Ranges(query, "dateRange", DateRange);
        RadarQuery.Timestamps(query, "dateStart", DateStart);
        query.AddMany("industry", Industry);
        query.Add("limitPerGroup", LimitPerGroup);
        query.AddMany("name", Name);
        query.AddMany("responseStatus", ResponseStatus);
        RadarQuery.Enums(query, "responseStatusCategory", ResponseStatusCategory);
        query.AddMany("vertical", Vertical);
    }
}

/// <summary>The filters for <c>GET /radar/bots/crawlers/summary/{dimension}</c>.</summary>
public sealed class RadarWebCrawlerSummaryParameters : RadarWebCrawlerParameters
{
    /// <inheritdoc />
    protected override void AddProblems(List<string> problems) =>
        RadarQuery.CheckLimitPerGroup(problems, LimitPerGroup);

    /// <inheritdoc />
    public override void Apply(QueryStringBuilder query) => ApplyCrawlerFilters(query);
}

/// <summary>The filters for <c>GET /radar/bots/crawlers/timeseries_groups/{dimension}</c>.</summary>
/// <remarks>
/// The summary's filters plus two that only a series over time can take: how widely to aggregate it, and which
/// of the three normalizations Cloudflare lets a caller ask for.
/// </remarks>
public sealed class RadarWebCrawlerTimeseriesGroupsParameters : RadarWebCrawlerParameters
{
    /// <summary>How widely to aggregate the series.</summary>
    public RadarAggregationInterval? AggInterval { get; set; }

    /// <summary>How to scale the series.</summary>
    public RadarTimeseriesNormalization? Normalization { get; set; }

    /// <inheritdoc />
    protected override void AddProblems(List<string> problems)
    {
        RadarQuery.CheckEnums(problems, "aggregation interval", AggInterval is { } interval
            ? new List<RadarAggregationInterval> { interval }
            : null);
        RadarQuery.CheckEnums(problems, "normalization", Normalization is { } normalization
            ? new List<RadarTimeseriesNormalization> { normalization }
            : null);
        RadarQuery.CheckLimitPerGroup(problems, LimitPerGroup);
    }

    /// <inheritdoc />
    public override void Apply(QueryStringBuilder query)
    {
        if (AggInterval is { } interval)
        {
            query.Add("aggInterval", CloudflareEnumConverter<RadarAggregationInterval>.ToWire(interval));
        }

        query.AddMany("botOperator", BotOperator);
        RadarQuery.Enums(query, "clientType", ClientType);
        RadarQuery.Timestamps(query, "dateEnd", DateEnd);
        RadarQuery.Ranges(query, "dateRange", DateRange);
        RadarQuery.Timestamps(query, "dateStart", DateStart);
        query.AddMany("industry", Industry);
        query.Add("limitPerGroup", LimitPerGroup);
        query.AddMany("name", Name);

        if (Normalization is { } normalization)
        {
            query.Add(
                "normalization",
                CloudflareEnumConverter<RadarTimeseriesNormalization>.ToWire(normalization));
        }

        query.AddMany("responseStatus", ResponseStatus);
        RadarQuery.Enums(query, "responseStatusCategory", ResponseStatusCategory);
        query.AddMany("vertical", Vertical);
    }
}

/// <summary>The filters for both <c>GET /radar/verified_bots/top/…</c> endpoints.</summary>
/// <remarks>
/// The two take the same nine filters, so they share one class: which of them is asked about is the path, not a
/// parameter.
/// </remarks>
public sealed class RadarVerifiedBotTopParameters : RadarRankingParameters
{
    /// <summary>Filters to traffic seen by these ASNs.</summary>
    public List<string>? Asn { get; set; }

    /// <summary>Filters to these continents.</summary>
    public List<string>? Continent { get; set; }

    /// <summary>The starts of the periods to rank.</summary>
    public List<DateTimeOffset>? DateStart { get; set; }

    /// <summary>The ends of the periods to rank.</summary>
    public List<DateTimeOffset>? DateEnd { get; set; }

    /// <summary>The periods to rank, in Cloudflare's own grammar.</summary>
    public List<string>? DateRange { get; set; }

    /// <summary>Filters to these alpha-2 location codes.</summary>
    public List<string>? Location { get; set; }

    /// <inheritdoc />
    public override void Apply(QueryStringBuilder query)
    {
        query.AddMany("asn", Asn);
        query.AddMany("continent", Continent);
        RadarQuery.Timestamps(query, "dateEnd", DateEnd);
        RadarQuery.Ranges(query, "dateRange", DateRange);
        RadarQuery.Timestamps(query, "dateStart", DateStart);
        ApplyLimit(query);
        query.AddMany("location", Location);
        ApplyNames(query);
    }
}
