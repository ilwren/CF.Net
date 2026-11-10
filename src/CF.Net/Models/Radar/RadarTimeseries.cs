using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

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

/// <summary>The meta a radar top list answers.</summary>
/// <remarks>
/// The timeseries meta without its aggregation interval - a top list is not a series over time - and with the
/// label of its first series. <see cref="ConfidenceInfo"/> is nullable here where it is not in
/// <see cref="RadarSeriesMeta"/>: Cloudflare publishes it as nullable for the top lists, and a run has yet to
/// see one answer it.
/// </remarks>
public sealed class RadarTopListMeta
{
    /// <summary>How far the ranking rests on data Cloudflare is confident about.</summary>
    [JsonPropertyName("confidenceInfo")]
    public RadarConfidenceInfo? ConfidenceInfo { get; set; }

    /// <summary>The periods the answer covers.</summary>
    [JsonPropertyName("dateRange")]
    public List<RadarDateRange>? DateRange { get; set; }

    /// <summary>When Cloudflare last updated the ranking, as it spells it.</summary>
    [JsonPropertyName("lastUpdated")]
    public string? LastUpdated { get; set; }

    /// <summary>How the numbers were scaled, in Cloudflare's own spelling.</summary>
    [JsonPropertyName("normalization")]
    public string? Normalization { get; set; }

    /// <summary>The label of the first series - the date it is for, and its category when it has one.</summary>
    [JsonPropertyName("top_0")]
    public RadarSeriesLabel? Top0 { get; set; }

    /// <summary>What the numbers mean, one entry per series.</summary>
    [JsonPropertyName("units")]
    public List<RadarUnit>? Units { get; set; }

    /// <summary>
    /// Captures any field Cloudflare adds in the future - including <c>top_1</c> and the labels after it, which
    /// arrive when a caller asks for more than one series.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        (Top0?.ToString() ?? "?") +
        (DateRange is { Count: > 0 } ranges ? $" over {string.Join(", ", ranges)}" : string.Empty) +
        (LastUpdated is { Length: > 0 } updated ? $", updated {updated}" : string.Empty);
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
