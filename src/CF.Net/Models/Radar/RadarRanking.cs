using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Exceptions;
using CFNet.Http;
using CFNet.Serialization;

namespace CFNet.Models.Radar;

/// <summary>Which of Cloudflare's domain rankings to ask for.</summary>
/// <remarks>
/// A filter, so it is typed: popular domains are the ones of broad appeal, and the two trending rankings are
/// the ones gaining interest - rising quickly, or holding a surge steady. Cloudflare publishes all three
/// spellings and no others, and a value this library has not met cannot be sent, which
/// <see cref="RadarRankingParameters.EnsureValid"/> says before a request is built rather than after.
/// </remarks>
[JsonConverter(typeof(CloudflareEnumConverter<RadarRankingType>))]
public enum RadarRankingType
{
    /// <summary>A value this version of the library has not met. Sending one is not possible.</summary>
    Unknown = 0,

    /// <summary>The domains of broad appeal - how many people use them.</summary>
    [CloudflareValue("POPULAR")]
    Popular = 1,

    /// <summary>The domains gaining interest fastest.</summary>
    [CloudflareValue("TRENDING_RISE")]
    TrendingRise = 2,

    /// <summary>The domains holding a surge in interest steady.</summary>
    [CloudflareValue("TRENDING_STEADY")]
    TrendingSteady = 3,
}

/// <summary>One of the categories Cloudflare puts a ranked domain in.</summary>
public sealed class RadarDomainCategory
{
    /// <summary>The category's id.</summary>
    [JsonPropertyName("id")]
    public long? Id { get; set; }

    /// <summary>What the category is called.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>The id of the category above this one.</summary>
    [JsonPropertyName("superCategoryId")]
    public long? SuperCategoryId { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"{Id?.ToString() ?? "?"} {Name ?? "?"}";
}

/// <summary>One domain in a radar ranking.</summary>
/// <remarks>
/// <see cref="PercentRankChange"/> is published as optional and the trending rankings are what it is for: a
/// popular ranking answers where a domain sits, a trending one answers how fast it moved. It is a
/// <see langword="double"/> because Cloudflare publishes it as a number with no range, and a percentage that
/// arrives as a fraction would fail to read as an integer and take the whole ranking with it.
/// </remarks>
public sealed class RadarRankedDomain
{
    /// <summary>The domain, as Cloudflare ranks it.</summary>
    [JsonPropertyName("domain")]
    public string? Domain { get; set; }

    /// <summary>Where it sits in this ranking.</summary>
    [JsonPropertyName("rank")]
    public long? Rank { get; set; }

    /// <summary>The categories Cloudflare puts it in.</summary>
    [JsonPropertyName("categories")]
    public List<RadarDomainCategory>? Categories { get; set; }

    /// <summary>How far it moved since the period Cloudflare compares against, when it reports a move.</summary>
    [JsonPropertyName("pctRankChange")]
    public double? PercentRankChange { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"{Rank?.ToString() ?? "?"} {Domain ?? "?"}" +
        (PercentRankChange is { } change
            ? $" ({change.ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture)}%)"
            : string.Empty);
}

/// <summary>The result of a domain ranking: the first series, and the meta that labels it.</summary>
/// <remarks>
/// <para>
/// Cloudflare numbers the series by the order the dates and names were asked for, and publishes only
/// <c>top_0</c>, so only the first is typed. A caller who asks for several gets the rest in
/// <see cref="AdditionalData"/> and their labels in <see cref="RadarTopListMeta.AdditionalData"/> - which
/// <see cref="SeriesNames"/> lists, because a numbered key nobody looks for is a series nobody reads.
/// </para>
/// </remarks>
public sealed class RadarRankingTop
{
    /// <summary>What the ranking covers, how it was scaled, and the label of its first series.</summary>
    [JsonPropertyName("meta")]
    public RadarTopListMeta? Meta { get; set; }

    /// <summary>The first series: the ranked domains, best first.</summary>
    [JsonPropertyName("top_0")]
    public List<RadarRankedDomain>? Top0 { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future - including <c>top_1</c> and the rest.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The names of the series after the first, when Cloudflare answered more than one.</summary>
    [JsonIgnore]
    public IEnumerable<string> SeriesNames =>
        AdditionalData is { Count: > 0 } extras
            ? extras.Keys.Where(key => key.StartsWith("top_", StringComparison.Ordinal))
            : Enumerable.Empty<string>();

    /// <inheritdoc />
    public override string ToString() =>
        $"{Top0?.Count ?? 0} domain(s)" +
        (Meta is { } meta ? $", {meta}" : string.Empty) +
        (SeriesNames.Any() ? $", and {string.Join(", ", SeriesNames)}" : string.Empty);
}

/// <summary>What every radar ranking endpoint takes: how many entries, and what to call the series.</summary>
/// <remarks>
/// <para>
/// The six ranking endpoints take the same two filters and then their own, so the two live here and each
/// endpoint's parameters add what Cloudflare publishes for that path - and only that. A filter an endpoint does
/// not publish is not offered for it, because a property that is quietly not sent reads like one that was.
/// </para>
/// <para>
/// <see cref="Apply"/> stays abstract and the shared filters are sent one at a time, because Cloudflare's own
/// parameter order differs per endpoint and a query string is asserted in that order by the tests that pin it.
/// </para>
/// </remarks>
public abstract class RadarRankingParameters : ICloudflareQueryParameters
{
    /// <summary>How many entries to return, per series.</summary>
    public int? Limit { get; set; }

    /// <summary>
    /// Labels for the series in the answer - one per date or category asked for, in the same order. Cloudflare
    /// uses them to name the <c>top_0</c>, <c>serie_0</c> keys it answers with.
    /// </summary>
    public List<string>? Name { get; set; }

    /// <inheritdoc />
    public abstract void Apply(QueryStringBuilder query);

    /// <summary>
    /// Checks the request can be sent: a limit has to ask for something, and a filter this library cannot
    /// spell has nothing to send.
    /// </summary>
    /// <exception cref="CloudflareConfigurationException">Thrown when a filter cannot be sent.</exception>
    public void EnsureValid()
    {
        var problems = new List<string>();

        if (Limit is < 1)
        {
            problems.Add(
                $"a radar ranking limit has to ask for at least one entry, and this one asks for {Limit}.");
        }

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

    /// <summary>Sends <c>limit</c>.</summary>
    /// <param name="query">The query string to add to.</param>
    protected void ApplyLimit(QueryStringBuilder query) => query.Add("limit", Limit);

    /// <summary>Sends <c>name</c>, once per label.</summary>
    /// <param name="query">The query string to add to.</param>
    protected void ApplyNames(QueryStringBuilder query) => query.AddMany("name", Name);

    /// <summary>
    /// Sends <c>date</c>, once per date, as the day Cloudflare ranks by: it publishes this filter as a date
    /// rather than a timestamp, so the time part is left off.
    /// </summary>
    /// <param name="query">The query string to add to.</param>
    /// <param name="dates">The dates to send.</param>
    protected static void ApplyDates(QueryStringBuilder query, List<DateTimeOffset>? dates) =>
        query.AddMany(
            "date",
            dates?.Select(date => date.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

    /// <summary>Sends <c>dateStart</c> and <c>dateEnd</c>, once per timestamp, as Cloudflare's own format.</summary>
    /// <param name="query">The query string to add to.</param>
    /// <param name="starts">The starts of the periods to send.</param>
    /// <param name="ends">The ends of the periods to send.</param>
    protected static void ApplyDateBounds(
        QueryStringBuilder query,
        List<DateTimeOffset>? starts,
        List<DateTimeOffset>? ends)
    {
        query.AddMany("dateStart", starts?.Select(start => CloudflareUrl.Format(start)));
        query.AddMany("dateEnd", ends?.Select(end => CloudflareUrl.Format(end)));
    }

    /// <summary>
    /// Sends <c>dateRange</c>, once per range. The values are Cloudflare's own grammar - a count of days or
    /// weeks, with <c>control</c> appended to ask for the period before it as well - and are left as the strings
    /// a caller writes rather than parsed here: the grammar is published in prose, and a range this library
    /// refuses locally but Cloudflare accepts would be a limit of this library, not of the API.
    /// </summary>
    /// <param name="query">The query string to add to.</param>
    /// <param name="ranges">The ranges to send.</param>
    protected static void ApplyDateRanges(QueryStringBuilder query, List<string>? ranges) =>
        query.AddMany("dateRange", ranges);

    /// <summary>Sends <c>rankingType</c>, when one was asked for.</summary>
    /// <param name="query">The query string to add to.</param>
    /// <param name="rankingType">The ranking to ask for.</param>
    protected static void ApplyRankingType(QueryStringBuilder query, RadarRankingType? rankingType)
    {
        if (rankingType is { } ranking)
        {
            query.Add("rankingType", CloudflareEnumConverter<RadarRankingType>.ToWire(ranking));
        }
    }

    /// <summary>Records a ranking type this version of the library has no spelling for.</summary>
    /// <param name="problems">The problems found so far, to add to.</param>
    /// <param name="rankingType">The ranking type that was asked for.</param>
    protected static void CheckRankingType(List<string> problems, RadarRankingType? rankingType)
    {
        if (rankingType is { } ranking &&
            !CloudflareEnumConverter<RadarRankingType>.TryToWire(ranking, out _))
        {
            problems.Add(
                $"'{ranking}' is not a radar ranking type this version of the library knows about, so it " +
                "cannot be sent as a filter.");
        }
    }
}

/// <summary>The filters for <c>GET /radar/ranking/top</c>.</summary>
public sealed class RadarRankingTopParameters : RadarRankingParameters
{
    /// <summary>The dates to rank, one series per date.</summary>
    public List<DateTimeOffset>? Date { get; set; }

    /// <summary>Filters to domains in these categories.</summary>
    public List<string>? DomainCategory { get; set; }

    /// <summary>Filters to these alpha-2 location codes.</summary>
    public List<string>? Location { get; set; }

    /// <summary>Which ranking to ask for.</summary>
    public RadarRankingType? RankingType { get; set; }

    /// <inheritdoc />
    protected override void AddProblems(List<string> problems) => CheckRankingType(problems, RankingType);

    /// <inheritdoc />
    public override void Apply(QueryStringBuilder query)
    {
        // In the order Cloudflare publishes them, which is the order a test that pins the query string asserts.
        ApplyDates(query, Date);
        query.AddMany("domainCategory", DomainCategory);
        ApplyLimit(query);
        query.AddMany("location", Location);
        ApplyNames(query);
        ApplyRankingType(query, RankingType);
    }
}

/// <summary>The filters for <c>GET /radar/ranking/timeseries_groups</c>.</summary>
/// <remarks>
/// This endpoint takes periods rather than days: <see cref="DateStart"/> and <see cref="DateEnd"/> for explicit
/// bounds, <see cref="DateRange"/> for Cloudflare's own shorthand - and no <c>date</c> filter at all, which is
/// why it is not inherited from the ranking endpoints beside it.
/// </remarks>
public sealed class RadarRankingTimeseriesGroupsParameters : RadarRankingParameters
{
    /// <summary>The starts of the periods to rank over.</summary>
    public List<DateTimeOffset>? DateStart { get; set; }

    /// <summary>The ends of the periods to rank over.</summary>
    public List<DateTimeOffset>? DateEnd { get; set; }

    /// <summary>
    /// The periods to rank over, in Cloudflare's own grammar - <c>7d</c>, <c>12w</c>, and the same with
    /// <c>control</c> appended to ask for the period before each one as well.
    /// </summary>
    public List<string>? DateRange { get; set; }

    /// <summary>Filters to domains in these categories.</summary>
    public List<string>? DomainCategory { get; set; }

    /// <summary>Filters to these domains.</summary>
    public List<string>? Domains { get; set; }

    /// <summary>Filters to these alpha-2 location codes.</summary>
    public List<string>? Location { get; set; }

    /// <summary>Which ranking to ask for.</summary>
    public RadarRankingType? RankingType { get; set; }

    /// <inheritdoc />
    protected override void AddProblems(List<string> problems) => CheckRankingType(problems, RankingType);

    /// <inheritdoc />
    public override void Apply(QueryStringBuilder query)
    {
        ApplyDateBounds(query, DateStart, DateEnd);
        ApplyDateRanges(query, DateRange);
        query.AddMany("domainCategory", DomainCategory);
        query.AddMany("domains", Domains);
        ApplyLimit(query);
        query.AddMany("location", Location);
        ApplyNames(query);
        ApplyRankingType(query, RankingType);
    }
}

/// <summary>Where one domain sits in a radar ranking, and what Cloudflare knows about it.</summary>
/// <remarks>
/// Cloudflare numbers the answers by the order the dates and names were asked for and publishes only
/// <c>details_0</c>, so only the first is typed and the rest arrive in <see cref="AdditionalData"/>.
/// </remarks>
public sealed class RadarDomainRanking
{
    /// <summary>The first answer: where the domain sits and what it is categorised as.</summary>
    [JsonPropertyName("details_0")]
    public RadarDomainRankingDetails? Details0 { get; set; }

    /// <summary>The periods the answer covers.</summary>
    [JsonPropertyName("meta")]
    public RadarDomainRankingMeta? Meta { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future - including <c>details_1</c> and the rest.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        (Details0?.ToString() ?? "no details") +
        (Meta is { } meta ? $", {meta}" : string.Empty);
}

/// <summary>What Cloudflare knows about one ranked domain.</summary>
/// <remarks>
/// Every field but the categories is published as optional: a domain outside the ranked bucket answers no rank
/// at all, and <see cref="TopLocations"/> arrives only when the caller asked for it with
/// <c>includeTopLocations</c>. <see cref="Bucket"/> is the size of the list the domain is in - the
/// <c>ranking_top_1000000</c> of the datasets endpoint, spelled here as the bucket a domain fell into.
/// </remarks>
public sealed class RadarDomainRankingDetails
{
    /// <summary>The categories Cloudflare puts the domain in.</summary>
    [JsonPropertyName("categories")]
    public List<RadarDomainCategory>? Categories { get; set; }

    /// <summary>Which ranked bucket the domain is in, when it is in one.</summary>
    [JsonPropertyName("bucket")]
    public string? Bucket { get; set; }

    /// <summary>Where the domain sits in that bucket, when Cloudflare ranks it.</summary>
    [JsonPropertyName("rank")]
    public long? Rank { get; set; }

    /// <summary>Where the domain is most used, when the caller asked for the places.</summary>
    [JsonPropertyName("top_locations")]
    public List<RadarDomainTopLocation>? TopLocations { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        (Rank is { } rank ? $"rank {rank}" : "unranked") +
        (Bucket is { Length: > 0 } bucket ? $" in {bucket}" : string.Empty) +
        (Categories is { Count: > 0 } categories ? $", {string.Join(", ", categories)}" : string.Empty);
}

/// <summary>One place a ranked domain is most used.</summary>
public sealed class RadarDomainTopLocation
{
    /// <summary>The alpha-2 code of the place.</summary>
    [JsonPropertyName("locationCode")]
    public string? LocationCode { get; set; }

    /// <summary>What Cloudflare calls the place.</summary>
    [JsonPropertyName("locationName")]
    public string? LocationName { get; set; }

    /// <summary>Where the place sits in the domain's traffic.</summary>
    [JsonPropertyName("rank")]
    public long? Rank { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"{Rank?.ToString() ?? "?"} {LocationCode ?? "?"}";
}

/// <summary>The meta a single-domain ranking answers: only the periods it covers.</summary>
public sealed class RadarDomainRankingMeta
{
    /// <summary>The periods the answer covers.</summary>
    [JsonPropertyName("dateRange")]
    public List<RadarDateRange>? DateRange { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        DateRange is { Count: > 0 } ranges ? string.Join(", ", ranges) : "?";
}

/// <summary>The filters for <c>GET /radar/ranking/domain/{domain}</c>.</summary>
/// <remarks>
/// This endpoint takes no <c>location</c> or <c>domainCategory</c>: the domain is in the path, and the places
/// it is used in are an answer rather than a filter - asked for with <see cref="IncludeTopLocations"/>.
/// </remarks>
public sealed class RadarDomainRankingParameters : RadarRankingParameters
{
    /// <summary>The dates to rank by, one answer per date.</summary>
    public List<DateTimeOffset>? Date { get; set; }

    /// <summary>Asks for the places the domain is most used in.</summary>
    public bool? IncludeTopLocations { get; set; }

    /// <summary>Which ranking to ask about.</summary>
    public RadarRankingType? RankingType { get; set; }

    /// <inheritdoc />
    protected override void AddProblems(List<string> problems) => CheckRankingType(problems, RankingType);

    /// <inheritdoc />
    public override void Apply(QueryStringBuilder query)
    {
        ApplyDates(query, Date);
        query.Add("includeTopLocations", IncludeTopLocations);
        ApplyLimit(query);
        ApplyNames(query);
        ApplyRankingType(query, RankingType);
    }
}

/// <summary>One of the internet-service categories Cloudflare ranks by.</summary>
public sealed class RadarInternetServiceCategory
{
    /// <summary>What the category is called, which is what a top list is filtered by.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => Name ?? "?";
}

/// <summary>The categories Cloudflare ranks internet services by.</summary>
/// <remarks>
/// Numbered like every radar list answer, and only the first is published, so only the first is typed: the rest
/// arrive in <see cref="AdditionalData"/>.
/// </remarks>
public sealed class RadarInternetServiceCategories
{
    /// <summary>The first set of categories.</summary>
    [JsonPropertyName("categories_0")]
    public List<RadarInternetServiceCategory>? Categories0 { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future - including <c>categories_1</c>.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"{Categories0?.Count ?? 0} category(ies)";
}

/// <summary>The filters for <c>GET /radar/ranking/internet_services/categories</c>.</summary>
public sealed class RadarInternetServiceCategoriesParameters : RadarRankingParameters
{
    /// <summary>The dates to ask about, one set of categories per date.</summary>
    public List<DateTimeOffset>? Date { get; set; }

    /// <inheritdoc />
    public override void Apply(QueryStringBuilder query)
    {
        ApplyDates(query, Date);
        ApplyLimit(query);
        ApplyNames(query);
    }
}

/// <summary>One internet service in a radar ranking.</summary>
public sealed class RadarInternetServiceRank
{
    /// <summary>Where the service sits in this ranking.</summary>
    [JsonPropertyName("rank")]
    public long? Rank { get; set; }

    /// <summary>What Cloudflare calls the service.</summary>
    [JsonPropertyName("service")]
    public string? Service { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"{Rank?.ToString() ?? "?"} {Service ?? "?"}";
}

/// <summary>The result of an internet-service ranking.</summary>
/// <remarks>
/// The same numbered-series shape as the domain ranking, with services in place of domains and a category named
/// in the label beside each series' date.
/// </remarks>
public sealed class RadarInternetServiceTop
{
    /// <summary>What the ranking covers, and the label of its first series.</summary>
    [JsonPropertyName("meta")]
    public RadarTopListMeta? Meta { get; set; }

    /// <summary>The first series: the ranked services, best first.</summary>
    [JsonPropertyName("top_0")]
    public List<RadarInternetServiceRank>? Top0 { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future - including <c>top_1</c> and the rest.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The names of the series after the first, when Cloudflare answered more than one.</summary>
    [JsonIgnore]
    public IEnumerable<string> SeriesNames =>
        AdditionalData is { Count: > 0 } extras
            ? extras.Keys.Where(key => key.StartsWith("top_", StringComparison.Ordinal))
            : Enumerable.Empty<string>();

    /// <inheritdoc />
    public override string ToString() =>
        $"{Top0?.Count ?? 0} service(s)" +
        (Meta is { } meta ? $", {meta}" : string.Empty) +
        (SeriesNames.Any() ? $", and {string.Join(", ", SeriesNames)}" : string.Empty);
}

/// <summary>The filters for <c>GET /radar/ranking/internet_services/top</c>.</summary>
public sealed class RadarInternetServiceTopParameters : RadarRankingParameters
{
    /// <summary>The dates to rank, one series per date.</summary>
    public List<DateTimeOffset>? Date { get; set; }

    /// <summary>Filters to these service categories, as the categories endpoint names them.</summary>
    public List<string>? ServiceCategory { get; set; }

    /// <inheritdoc />
    public override void Apply(QueryStringBuilder query)
    {
        ApplyDates(query, Date);
        ApplyLimit(query);
        ApplyNames(query);
        query.AddMany("serviceCategory", ServiceCategory);
    }
}

/// <summary>The filters for <c>GET /radar/ranking/internet_services/timeseries_groups</c>.</summary>
public sealed class RadarInternetServiceTimeseriesGroupsParameters : RadarRankingParameters
{
    /// <summary>The starts of the periods to rank over.</summary>
    public List<DateTimeOffset>? DateStart { get; set; }

    /// <summary>The ends of the periods to rank over.</summary>
    public List<DateTimeOffset>? DateEnd { get; set; }

    /// <summary>The periods to rank over, in Cloudflare's own grammar.</summary>
    public List<string>? DateRange { get; set; }

    /// <summary>Filters to these service categories, as the categories endpoint names them.</summary>
    public List<string>? ServiceCategory { get; set; }

    /// <inheritdoc />
    public override void Apply(QueryStringBuilder query)
    {
        ApplyDateBounds(query, DateStart, DateEnd);
        ApplyDateRanges(query, DateRange);
        ApplyLimit(query);
        ApplyNames(query);
        query.AddMany("serviceCategory", ServiceCategory);
    }
}
