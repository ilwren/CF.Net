using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Http;
using CFNet.Serialization;

namespace CFNet.Models.Radar;

/// <summary>The aggregation intervals Cloudflare reports in a radar series' meta.</summary>
/// <remarks>
/// <para>
/// These are the spellings Cloudflare publishes for <c>meta.aggInterval</c>, offered as constants rather than
/// as an enum because the field is an answer and never a filter: an enum would turn a value this library has
/// not met into <c>Unknown</c> and drop what Cloudflare said, and there is nothing to send that would need one.
/// A caller switches on <see cref="RadarSeriesMeta.AggInterval"/> against these and keeps the string it was
/// given when none of them matches.
/// </para>
/// </remarks>
public static class RadarAggregationIntervals
{
    /// <summary>Fifteen minutes.</summary>
    public const string FifteenMinutes = "FIFTEEN_MINUTES";

    /// <summary>One hour.</summary>
    public const string OneHour = "ONE_HOUR";

    /// <summary>One day.</summary>
    public const string OneDay = "ONE_DAY";

    /// <summary>One week.</summary>
    public const string OneWeek = "ONE_WEEK";

    /// <summary>One month.</summary>
    public const string OneMonth = "ONE_MONTH";
}

/// <summary>The normalizations Cloudflare reports in a radar series' meta.</summary>
/// <remarks>
/// The spellings Cloudflare publishes for <c>meta.normalization</c> - how the numbers in a series were scaled
/// before they were answered. Constants for the same reason as <see cref="RadarAggregationIntervals"/>: this
/// field is an answer, and an answer worth reading is not worth narrowing.
/// </remarks>
public static class RadarNormalizations
{
    /// <summary>Each value as a percentage of the total.</summary>
    public const string Percentage = "PERCENTAGE";

    /// <summary>Scaled between zero and the largest value.</summary>
    public const string Min0Max = "MIN0_MAX";

    /// <summary>Scaled between the smallest and the largest value.</summary>
    public const string MinMax = "MIN_MAX";

    /// <summary>Not scaled at all.</summary>
    public const string RawValues = "RAW_VALUES";

    /// <summary>Each value as a change against the one before it.</summary>
    public const string PercentageChange = "PERCENTAGE_CHANGE";

    /// <summary>A moving average.</summary>
    public const string RollingAverage = "ROLLING_AVERAGE";

    /// <summary>Each value as a percentage, overlapped against the comparison period.</summary>
    public const string OverlappedPercentage = "OVERLAPPED_PERCENTAGE";

    /// <summary>Each value as a ratio.</summary>
    public const string Ratio = "RATIO";
}

/// <summary>How widely to ask Cloudflare to aggregate a radar series.</summary>
/// <remarks>
/// A filter, so it is typed - and note that it is <b>not</b> spelled the way the answer is: a request asks for
/// <c>1d</c> and the answer reports <c>ONE_DAY</c>, which is what <see cref="RadarAggregationIntervals"/>
/// holds. Two spellings of one idea, on the two sides of the call.
/// </remarks>
[JsonConverter(typeof(CloudflareEnumConverter<RadarAggregationInterval>))]
public enum RadarAggregationInterval
{
    /// <summary>A value this version of the library has not met. Sending one is not possible.</summary>
    Unknown = 0,

    /// <summary>Fifteen minutes, asked for as <c>15m</c> and answered as <c>FIFTEEN_MINUTES</c>.</summary>
    [CloudflareValue("15m")]
    FifteenMinutes = 1,

    /// <summary>One hour, asked for as <c>1h</c> and answered as <c>ONE_HOUR</c>.</summary>
    [CloudflareValue("1h")]
    OneHour = 2,

    /// <summary>One day, asked for as <c>1d</c> and answered as <c>ONE_DAY</c>.</summary>
    [CloudflareValue("1d")]
    OneDay = 3,

    /// <summary>One week, asked for as <c>1w</c> and answered as <c>ONE_WEEK</c>.</summary>
    [CloudflareValue("1w")]
    OneWeek = 4,
}

/// <summary>How to ask Cloudflare to scale a radar series.</summary>
/// <remarks>
/// A filter, and a narrower one than the answer: Cloudflare reports eight normalizations in
/// <see cref="RadarNormalizations"/> but lets a caller ask for three of them. The two lists are kept apart
/// rather than merged, because a value that can be read is not a value that can be sent.
/// </remarks>
[JsonConverter(typeof(CloudflareEnumConverter<RadarTimeseriesNormalization>))]
public enum RadarTimeseriesNormalization
{
    /// <summary>A value this version of the library has not met. Sending one is not possible.</summary>
    Unknown = 0,

    /// <summary>Each value as a percentage of the total.</summary>
    [CloudflareValue("PERCENTAGE")]
    Percentage = 1,

    /// <summary>Scaled between zero and the largest value.</summary>
    [CloudflareValue("MIN0_MAX")]
    Min0Max = 2,

    /// <summary>Each value as a change against the one before it.</summary>
    [CloudflareValue("PERCENTAGE_CHANGE")]
    PercentageChange = 3,
}

/// <summary>One period a radar answer covers.</summary>
/// <remarks>
/// Both ends are the strings Cloudflare sends rather than timestamps. Radar answers its dates in more than one
/// spelling across endpoints - <c>yyyy-MM-dd</c> beside <c>yyyy-MM-ddTHH:mm:ssZ</c>, and one botnet feed
/// answers a bare date where its neighbours answer a timestamp - and a wrong guess at which one an endpoint
/// uses does not cost one field, it fails the whole read.
/// </remarks>
public sealed class RadarDateRange
{
    /// <summary>When the period starts, as Cloudflare spells it.</summary>
    [JsonPropertyName("startTime")]
    public string? StartTime { get; set; }

    /// <summary>When the period ends, as Cloudflare spells it.</summary>
    [JsonPropertyName("endTime")]
    public string? EndTime { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"{StartTime ?? "?"} to {EndTime ?? "?"}";
}

/// <summary>What the numbers in a radar series mean.</summary>
/// <remarks>
/// Published as a name and a value with no list of either, and answered one entry per series in the order the
/// series arrive - so it stays two strings rather than becoming a unit this library has to keep a catalogue of.
/// </remarks>
public sealed class RadarUnit
{
    /// <summary>What the unit is called.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>What it measures.</summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"{Name ?? "?"}={Value ?? "?"}";
}

/// <summary>How far a radar answer rests on data Cloudflare is confident about.</summary>
/// <remarks>
/// <para>
/// The annotations here are the same objects <c>GET /radar/annotations</c> answers, with fewer of their fields
/// filled in - a confidence report names the outages and pipeline events that touched the period rather than
/// describing them - so they are read as <see cref="RadarAnnotation"/> rather than as a second type that would
/// have to be kept in step with the first.
/// </para>
/// <para>
/// <see cref="Level"/> is a <see langword="double"/> because Cloudflare publishes it as a number with no range:
/// an <see langword="int"/> would read <c>85</c> and fail on <c>0.85</c>, and a field that fails to read takes
/// the whole meta object with it.
/// </para>
/// </remarks>
public sealed class RadarConfidenceInfo
{
    /// <summary>The annotations Cloudflare attached to this period.</summary>
    [JsonPropertyName("annotations")]
    public List<RadarAnnotation>? Annotations { get; set; }

    /// <summary>How confident Cloudflare is in the numbers, on a scale it does not publish.</summary>
    [JsonPropertyName("level")]
    public double? Level { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"level {Level?.ToString(CultureInfo.InvariantCulture) ?? "?"}" +
        (Annotations is { Count: > 0 } annotations ? $", {annotations.Count} annotation(s)" : string.Empty);
}

/// <summary>The meta every radar timeseries endpoint answers beside its series.</summary>
/// <remarks>
/// One type for all of them, because they all answer the same six fields: the endpoints differ in the series
/// beside the meta, not in the meta. Anything an endpoint adds arrives in <see cref="AdditionalData"/>.
/// </remarks>
public sealed class RadarSeriesMeta
{
    /// <summary>
    /// How widely Cloudflare aggregated the series, in its own spelling - one of
    /// <see cref="RadarAggregationIntervals"/> when it is one this library knows.
    /// </summary>
    [JsonPropertyName("aggInterval")]
    public string? AggInterval { get; set; }

    /// <summary>How far the numbers rest on data Cloudflare is confident about.</summary>
    [JsonPropertyName("confidenceInfo")]
    public RadarConfidenceInfo? ConfidenceInfo { get; set; }

    /// <summary>The periods the answer covers - one per series, in the order they were asked for.</summary>
    [JsonPropertyName("dateRange")]
    public List<RadarDateRange>? DateRange { get; set; }

    /// <summary>When Cloudflare last updated the data, as it spells it.</summary>
    [JsonPropertyName("lastUpdated")]
    public string? LastUpdated { get; set; }

    /// <summary>
    /// How the numbers were scaled, in Cloudflare's own spelling - one of <see cref="RadarNormalizations"/>
    /// when it is one this library knows.
    /// </summary>
    [JsonPropertyName("normalization")]
    public string? Normalization { get; set; }

    /// <summary>What the numbers mean, one entry per series.</summary>
    [JsonPropertyName("units")]
    public List<RadarUnit>? Units { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"{AggInterval ?? "?"} {Normalization ?? "?"}" +
        (DateRange is { Count: > 0 } ranges ? $" over {string.Join(", ", ranges)}" : string.Empty) +
        (ConfidenceInfo is { } confidence ? $" ({confidence})" : string.Empty);
}

/// <summary>The label Cloudflare gives one series of a top list.</summary>
/// <remarks>
/// A top list answers one series per date or per name the caller asked for, and labels each of them here -
/// <c>meta.top_0</c> for the first, <c>meta.top_1</c> for the second, and so on. <see cref="ServiceCategory"/>
/// is answered by the internet-service rankings and not by the domain ones, so it stays nullable.
/// </remarks>
public sealed class RadarSeriesLabel
{
    /// <summary>The date this series is for, as Cloudflare spells it.</summary>
    [JsonPropertyName("date")]
    public string? Date { get; set; }

    /// <summary>The internet-service category this series is for, when the endpoint ranks those.</summary>
    [JsonPropertyName("serviceCategory")]
    public string? ServiceCategory { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        (Date ?? "?") + (ServiceCategory is { Length: > 0 } category ? $" ({category})" : string.Empty);
}

/// <summary>
/// The meta a radar summary, top list or ranking answers: what the numbers cover, how they were scaled, and
/// how far Cloudflare trusts them.
/// </summary>
/// <remarks>
/// The timeseries meta without its aggregation interval - a summary is not a series over time - and the base
/// of <see cref="RadarTopListMeta"/>, which is this plus the label of its first series.
/// <see cref="ConfidenceInfo"/> is nullable here where it is not in <see cref="RadarSeriesMeta"/>: Cloudflare
/// publishes it as nullable for the summaries and top lists.
/// </remarks>
public class RadarSummaryMeta
{
    /// <summary>How far the numbers rest on data Cloudflare is confident about.</summary>
    [JsonPropertyName("confidenceInfo")]
    public RadarConfidenceInfo? ConfidenceInfo { get; set; }

    /// <summary>The periods the answer covers.</summary>
    [JsonPropertyName("dateRange")]
    public List<RadarDateRange>? DateRange { get; set; }

    /// <summary>When Cloudflare last updated the data, as it spells it.</summary>
    [JsonPropertyName("lastUpdated")]
    public string? LastUpdated { get; set; }

    /// <summary>
    /// How the numbers were scaled, in Cloudflare's own spelling - one of <see cref="RadarNormalizations"/>
    /// when it is one this library knows.
    /// </summary>
    [JsonPropertyName("normalization")]
    public string? Normalization { get; set; }

    /// <summary>What the numbers mean, one entry per series.</summary>
    [JsonPropertyName("units")]
    public List<RadarUnit>? Units { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>
    /// The period and the update time as one string, starting with a space so a derived type can put a label
    /// in front of it, and empty when the meta carries neither.
    /// </summary>
    /// <returns>The period and the update time.</returns>
    protected string Describe() =>
        (DateRange is { Count: > 0 } ranges ? $" over {string.Join(", ", ranges)}" : string.Empty) +
        (LastUpdated is { Length: > 0 } updated ? $", updated {updated}" : string.Empty);

    /// <inheritdoc />
    public override string ToString()
    {
        var described = Describe().Trim(' ', ',');
        return described.Length > 0 ? described : "?";
    }
}

/// <summary>The meta a radar top list answers: a summary's meta, plus the label of its first series.</summary>
public sealed class RadarTopListMeta : RadarSummaryMeta
{
    /// <summary>The label of the first series - the date it is for, and its category when it has one.</summary>
    [JsonPropertyName("top_0")]
    public RadarSeriesLabel? Top0 { get; set; }

    /// <inheritdoc />
    public override string ToString() => (Top0?.ToString() ?? "?") + Describe();
}

/// <summary>One group of a radar timeseries answer: its timestamps and the values beside them.</summary>
/// <remarks>
/// <para>
/// Cloudflare publishes this as <c>timestamps</c> plus an open set of keys, each an array of numbers or of
/// strings, one entry per timestamp. The names are the caller's own - they come from the <c>name</c> filter, or
/// from the domain or service the series is about - so they cannot be typed, and they arrive in
/// <see cref="AdditionalData"/> with <see cref="SeriesNames"/> listing them.
/// </para>
/// <para>
/// The values are left as JSON rather than read into <see langword="double"/>: some series answer counts and
/// some answer strings, and one array of the wrong shape would fail the whole group.
/// </para>
/// </remarks>
public sealed class RadarTimeseriesGroup
{
    /// <summary>When each value was measured, as Cloudflare spells it, in the order the values arrive.</summary>
    [JsonPropertyName("timestamps")]
    public List<string>? Timestamps { get; set; }

    /// <summary>
    /// The value series beside the timestamps, one entry per name, each an array with one value per timestamp.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The names of the value series beside the timestamps.</summary>
    [JsonIgnore]
    public IEnumerable<string> SeriesNames =>
        AdditionalData is { Count: > 0 } series
            ? series.Keys
            : Enumerable.Empty<string>();

    /// <summary>How many timestamps the group carries.</summary>
    [JsonIgnore]
    public int Count => Timestamps?.Count ?? 0;

    /// <summary>
    /// Reads one group out of the JSON an answer's untyped keys hold.
    /// </summary>
    /// <remarks>
    /// For the series this library has no published name for - <c>serie_1</c>, or the whole answer of an
    /// endpoint whose groups are all named after the caller's own filters. A group that turns out not to be
    /// shaped like the others answers <see langword="null"/> rather than throwing: the JSON is still in the
    /// dictionary it came from, and one odd series is not worth losing the answer over.
    /// </remarks>
    /// <param name="element">The JSON the group arrived as.</param>
    /// <returns>The group, or <see langword="null"/> when it is not shaped like one.</returns>
    public static RadarTimeseriesGroup? FromJson(JsonElement element)
    {
        try
        {
            return element.ValueKind == JsonValueKind.Object
                ? element.Deserialize<RadarTimeseriesGroup>(CloudflareJson.Default)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"{Count} timestamp(s), {AdditionalData?.Count ?? 0} series" +
        (AdditionalData is { Count: > 0 } series ? $": {string.Join(", ", series.Keys)}" : string.Empty);
}

/// <summary>
/// The result every radar <c>timeseries_groups</c> endpoint answers: a meta and its first group of series.
/// </summary>
/// <remarks>
/// Cloudflare numbers the groups by the order the caller asked for them - <c>serie_0</c>, then <c>serie_1</c>
/// for a second date or name - and publishes only the first, so only the first is typed. The rest arrive in
/// <see cref="AdditionalData"/>, and <see cref="SeriesNames"/> says which ones did.
/// </remarks>
public sealed class RadarTimeseriesGroups
{
    /// <summary>How the series were aggregated, normalized and dated.</summary>
    [JsonPropertyName("meta")]
    public RadarSeriesMeta? Meta { get; set; }

    /// <summary>The first group of series.</summary>
    [JsonPropertyName("serie_0")]
    public RadarTimeseriesGroup? Serie0 { get; set; }

    /// <summary>
    /// Captures any field Cloudflare adds in the future - including <c>serie_1</c> and the groups after it.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The names of the groups after the first, when Cloudflare answered more than one.</summary>
    [JsonIgnore]
    public IEnumerable<string> SeriesNames =>
        AdditionalData is { Count: > 0 } extras
            ? extras.Keys.Where(key => key.StartsWith("serie_", StringComparison.Ordinal))
            : Enumerable.Empty<string>();

    /// <inheritdoc />
    public override string ToString() =>
        (Serie0?.ToString() ?? "no series") +
        (Meta is { } meta ? $", {meta}" : string.Empty);
}

/// <summary>The result of a radar <c>summary/{dimension}</c> endpoint.</summary>
/// <remarks>
/// <para>
/// A summary is not a list and not a series: it is one number per value of the dimension in the path - a
/// percentage of traffic per bot, per client type, per response status - and Cloudflare publishes it as an open
/// dictionary of string to string, because the keys are the dimension's own values and there is no list of
/// them. So it is read as the dictionary it is, and <see cref="SeriesNames"/> says whether a second one
/// arrived beside it.
/// </para>
/// </remarks>
public sealed class RadarSummary
{
    /// <summary>What the numbers cover and how they were scaled.</summary>
    [JsonPropertyName("meta")]
    public RadarSummaryMeta? Meta { get; set; }

    /// <summary>The first summary: one entry per value of the dimension, as Cloudflare spells both.</summary>
    [JsonPropertyName("summary_0")]
    public Dictionary<string, string>? Summary0 { get; set; }

    /// <summary>
    /// Captures any field Cloudflare adds in the future - including <c>summary_1</c> and the summaries after
    /// it, which arrive when a caller asks for more than one period or label.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The names of the summaries after the first, when Cloudflare answered more than one.</summary>
    [JsonIgnore]
    public IEnumerable<string> SeriesNames =>
        AdditionalData is { Count: > 0 } extras
            ? extras.Keys.Where(key => key.StartsWith("summary_", StringComparison.Ordinal))
            : Enumerable.Empty<string>();

    /// <inheritdoc />
    public override string ToString() =>
        $"{Summary0?.Count ?? 0} value(s)" +
        (Meta is { } meta ? $", {meta}" : string.Empty) +
        (SeriesNames.Any() ? $", and {string.Join(", ", SeriesNames)}" : string.Empty);
}

/// <summary>
/// The result of a radar <c>timeseries</c> endpoint with no dimension in its path: a meta and one group per
/// name, none of them published.
/// </summary>
/// <remarks>
/// <para>
/// Cloudflare publishes this result as <c>meta</c> plus an open set of keys, each a group of series named after
/// whatever the caller filtered by - a bot, an ASN, a location - so there is no <c>serie_0</c> to type and
/// every group arrives in <see cref="AdditionalData"/>. <see cref="GroupNames"/> lists them and
/// <see cref="Group"/> reads one, which is the difference between an answer a caller can use and one they have
/// to hand-parse.
/// </para>
/// </remarks>
public sealed class RadarNamedTimeseries
{
    /// <summary>How the groups were aggregated, normalized and dated.</summary>
    [JsonPropertyName("meta")]
    public RadarSeriesMeta? Meta { get; set; }

    /// <summary>Every group in the answer, by the name Cloudflare gave it.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The names of the groups in the answer.</summary>
    [JsonIgnore]
    public IEnumerable<string> GroupNames =>
        AdditionalData is { Count: > 0 } extras
            ? extras.Keys.Where(key => !string.Equals(key, "meta", StringComparison.Ordinal))
            : Enumerable.Empty<string>();

    /// <summary>
    /// Reads one group by the name Cloudflare gave it.
    /// </summary>
    /// <param name="name">The group's name, as <see cref="GroupNames"/> lists it.</param>
    /// <returns>The group, or <see langword="null"/> when there is none of that name or it is not shaped like
    /// one.</returns>
    public RadarTimeseriesGroup? Group(string name) =>
        AdditionalData is { } extras && extras.TryGetValue(name, out var element)
            ? RadarTimeseriesGroup.FromJson(element)
            : null;

    /// <inheritdoc />
    public override string ToString() =>
        $"{AdditionalData?.Count ?? 0} group(s)" +
        (GroupNames.Any() ? $": {string.Join(", ", GroupNames.Take(5))}" : string.Empty) +
        (Meta is { } meta ? $", {meta}" : string.Empty);
}

/// <summary>
/// The result of a radar <c>top</c> endpoint whose meta carries no label for its series: the first series, and
/// what the numbers cover.
/// </summary>
/// <typeparam name="TItem">What one entry of the ranking is.</typeparam>
/// <remarks>
/// The same numbered-series shape as <see cref="RadarRankingTop"/>, which is a ranking of domains and answers a
/// <c>meta.top_0</c> label beside its list; the endpoints this serves answer the list without the label, so
/// their meta is <see cref="RadarSummaryMeta"/> rather than <see cref="RadarTopListMeta"/>.
/// </remarks>
public sealed class RadarTopList<TItem>
{
    /// <summary>What the ranking covers and how it was scaled.</summary>
    [JsonPropertyName("meta")]
    public RadarSummaryMeta? Meta { get; set; }

    /// <summary>The first series: the ranked entries, best first.</summary>
    [JsonPropertyName("top_0")]
    public List<TItem>? Top0 { get; set; }

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
        $"{Top0?.Count ?? 0} entry(ies)" +
        (Top0 is { Count: > 0 } entries ? $" [{string.Join(", ", entries.Take(3))}]" : string.Empty) +
        (Meta is { } meta ? $", {meta}" : string.Empty) +
        (SeriesNames.Any() ? $", and {string.Join(", ", SeriesNames)}" : string.Empty);
}

/// <summary>
/// The query-string spellings radar's filters share, in one place because the families that use them are about
/// to outnumber the ones that do not.
/// </summary>
internal static class RadarQuery
{
    /// <summary>Sends a list of days, as the <c>yyyy-MM-dd</c> Cloudflare ranks and summarises by.</summary>
    /// <param name="query">The query string to add to.</param>
    /// <param name="name">The parameter's name.</param>
    /// <param name="dates">The days to send.</param>
    internal static void Days(QueryStringBuilder query, string name, List<DateTimeOffset>? dates) =>
        query.AddMany(
            name,
            dates?.Select(date => date.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

    /// <summary>Sends a list of timestamps, in the form Cloudflare's own <c>dateStart</c> and <c>dateEnd</c>
    /// take.</summary>
    /// <param name="query">The query string to add to.</param>
    /// <param name="name">The parameter's name.</param>
    /// <param name="values">The timestamps to send.</param>
    internal static void Timestamps(QueryStringBuilder query, string name, List<DateTimeOffset>? values) =>
        query.AddMany(name, values?.Select(value => CloudflareUrl.Format(value)));

    /// <summary>Sends a list of ranges written in Cloudflare's own grammar.</summary>
    /// <param name="query">The query string to add to.</param>
    /// <param name="name">The parameter's name.</param>
    /// <param name="ranges">The ranges to send.</param>
    internal static void Ranges(QueryStringBuilder query, string name, List<string>? ranges) =>
        query.AddMany(name, ranges);

    /// <summary>Sends a list of enums, one parameter per value, spelled as Cloudflare publishes them.</summary>
    /// <typeparam name="TEnum">The enum to spell.</typeparam>
    /// <param name="query">The query string to add to.</param>
    /// <param name="name">The parameter's name.</param>
    /// <param name="values">The values to send.</param>
    internal static void Enums<TEnum>(QueryStringBuilder query, string name, List<TEnum>? values)
        where TEnum : struct, Enum
    {
        foreach (var value in values ?? new List<TEnum>())
        {
            query.Add(name, CloudflareEnumConverter<TEnum>.ToWire(value));
        }
    }

    /// <summary>Records a per-group limit Cloudflare would refuse.</summary>
    /// <remarks>
    /// The minimum is two, and that is not a published number being guessed at locally: a live run sent
    /// <c>limitPerGroup=1</c> to all four endpoints that take it and every one of them answered
    /// <c>400 [2001] Too small</c>, while <c>limit=1</c> on the rankings beside them read fine. Saying so
    /// here turns that 400 into a sentence that names the reason.
    /// </remarks>
    /// <param name="problems">The problems found so far, to add to.</param>
    /// <param name="limitPerGroup">The limit that was asked for.</param>
    internal static void CheckLimitPerGroup(List<string> problems, int? limitPerGroup)
    {
        if (limitPerGroup is < 2)
        {
            problems.Add(
                $"a radar limit per group has to ask for at least two entries, because Cloudflare answers " +
                $"400 [2001] 'Too small' to one, and this one asks for {limitPerGroup}.");
        }
    }

    /// <summary>Records every value of a filter this version of the library has no spelling for.</summary>
    /// <typeparam name="TEnum">The enum to check.</typeparam>
    /// <param name="problems">The problems found so far, to add to.</param>
    /// <param name="what">What the filter is, in words a caller will recognise.</param>
    /// <param name="values">The values that were asked for.</param>
    internal static void CheckEnums<TEnum>(List<string> problems, string what, List<TEnum>? values)
        where TEnum : struct, Enum
    {
        foreach (var value in values ?? new List<TEnum>())
        {
            if (!CloudflareEnumConverter<TEnum>.TryToWire(value, out _))
            {
                problems.Add(
                    $"'{value}' is not a {what} this version of the library knows about, so it cannot be sent " +
                    "as a filter.");
            }
        }
    }
}
