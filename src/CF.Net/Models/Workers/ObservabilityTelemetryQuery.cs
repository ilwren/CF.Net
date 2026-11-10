using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Models.Workers;

/// <summary>
/// The answer to a telemetry query: the run that executed it, what that run cost, and whichever of the six
/// result shapes the view that was asked for fills in.
/// </summary>
/// <remarks>
/// <para>
/// Answered by <c>POST /accounts/{account_id}/workers/observability/telemetry/query</c>. Only
/// <see cref="Run"/> and <see cref="Statistics"/> always arrive; the rest are there when the request asked
/// for them. <c>view</c> decides which: <c>events</c> fills <see cref="Events"/>, <c>invocations</c> fills
/// <see cref="Invocations"/> (the same events, grouped by request id), <c>traces</c> fills
/// <see cref="Traces"/>, <c>agents</c> fills <see cref="Agents"/>, and <c>calculations</c> fills
/// <see cref="Calculations"/> - or <see cref="Distribution"/> instead when the chart type asked for a
/// histogram. Asking to compare adds <see cref="Compare"/>, which is the previous period of equal length in
/// the same shape as <see cref="Calculations"/>.
/// </para>
/// <para>
/// Because which fields are populated moves with the view, the properties whose names start with
/// <c>All</c> read whatever arrived as one list, so a caller can print a result without first working out
/// what it asked for.
/// </para>
/// </remarks>
public sealed class ObservabilityTelemetryQueryResult
{
    /// <summary>The run that executed the query, with the query as Cloudflare stored it.</summary>
    [JsonPropertyName("run")]
    public ObservabilityTelemetryRun? Run { get; set; }

    /// <summary>What the query cost the database: rows scanned, bytes read and seconds elapsed.</summary>
    [JsonPropertyName("statistics")]
    public ObservabilityTelemetryStatistics? Statistics { get; set; }

    /// <summary>One summary per agent run, filled by the <c>agents</c> view.</summary>
    [JsonPropertyName("agents")]
    public List<ObservabilityTelemetryAgent>? Agents { get; set; }

    /// <summary>Aggregated metrics - a count, an average, a percentile - filled by the <c>calculations</c> view.</summary>
    [JsonPropertyName("calculations")]
    public List<ObservabilityTelemetryCalculationResult>? Calculations { get; set; }

    /// <summary>The same calculations over the previous period of equal length, when the query compared.</summary>
    [JsonPropertyName("compare")]
    public List<ObservabilityTelemetryCalculationResult>? Compare { get; set; }

    /// <summary>A bucketed histogram of one numeric field over time, when the chart type asked for one.</summary>
    [JsonPropertyName("distribution")]
    public ObservabilityTelemetryDistribution? Distribution { get; set; }

    /// <summary>The matching log lines, spans and metric points, filled by the <c>events</c> view.</summary>
    [JsonPropertyName("events")]
    public ObservabilityTelemetryEvents? Events { get; set; }

    /// <summary>
    /// The matching events grouped by invocation, filled by the <c>invocations</c> view. Each key is a
    /// request id and each value is every event from that one invocation.
    /// </summary>
    [JsonPropertyName("invocations")]
    public IDictionary<string, List<ObservabilityTelemetryEvent>>? Invocations { get; set; }

    /// <summary>One summary per distributed trace, filled by the <c>traces</c> view.</summary>
    [JsonPropertyName("traces")]
    public List<ObservabilityTelemetryTrace>? Traces { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Whether the run finished, rather than only having started.</summary>
    [JsonIgnore]
    public bool IsComplete =>
        string.Equals(Run?.Status, ObservabilityTelemetryRunStatuses.Completed, StringComparison.Ordinal);

    /// <summary>Every event the <c>events</c> view returned, or nothing when it was not the view asked for.</summary>
    [JsonIgnore]
    public IReadOnlyList<ObservabilityTelemetryEvent> AllEvents =>
        (IReadOnlyList<ObservabilityTelemetryEvent>?)Events?.Events ??
        Array.Empty<ObservabilityTelemetryEvent>();

    /// <summary>
    /// Every event the <c>invocations</c> view returned, taken out of its request-id groups, or nothing when
    /// it was not the view asked for.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<ObservabilityTelemetryEvent> AllInvocations
    {
        get
        {
            if (Invocations is null)
            {
                return Array.Empty<ObservabilityTelemetryEvent>();
            }

            var flattened = new List<ObservabilityTelemetryEvent>();
            foreach (var events in Invocations.Values)
            {
                if (events is not null)
                {
                    flattened.AddRange(events);
                }
            }

            return flattened;
        }
    }

    /// <summary>Every trace summary returned, or nothing when the view was not <c>traces</c>.</summary>
    [JsonIgnore]
    public IReadOnlyList<ObservabilityTelemetryTrace> AllTraces =>
        (IReadOnlyList<ObservabilityTelemetryTrace>?)Traces ?? Array.Empty<ObservabilityTelemetryTrace>();

    /// <summary>Every agent run summary returned, or nothing when the view was not <c>agents</c>.</summary>
    [JsonIgnore]
    public IReadOnlyList<ObservabilityTelemetryAgent> AllAgents =>
        (IReadOnlyList<ObservabilityTelemetryAgent>?)Agents ?? Array.Empty<ObservabilityTelemetryAgent>();

    /// <summary>Every calculation returned, or nothing when the view was not <c>calculations</c>.</summary>
    [JsonIgnore]
    public IReadOnlyList<ObservabilityTelemetryCalculationResult> AllCalculations =>
        (IReadOnlyList<ObservabilityTelemetryCalculationResult>?)Calculations ??
        Array.Empty<ObservabilityTelemetryCalculationResult>();

    /// <summary>Status, row count and how many results arrived, on one line.</summary>
    /// <returns>The result described in one line.</returns>
    public override string ToString() =>
        $"{Run?.Status ?? "?"} in {Statistics?.Elapsed?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? "?"}s " +
        $"over {Statistics?.RowsRead?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"} rows";
}

/// <summary>One execution of a telemetry query: what ran, whether it finished, and what it covered.</summary>
public sealed class ObservabilityTelemetryRun
{
    /// <summary>The run's id.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>The account whose telemetry was queried.</summary>
    [JsonPropertyName("accountId")]
    public string? AccountId { get; set; }

    /// <summary>Whether the run was a dry one, which executes without persisting its results.</summary>
    [JsonPropertyName("dry")]
    public bool? Dry { get; set; }

    /// <summary>How many time-series buckets the run used.</summary>
    [JsonPropertyName("granularity")]
    public int? Granularity { get; set; }

    /// <summary>The query as Cloudflare holds it, including the parameters it ran with.</summary>
    [JsonPropertyName("query")]
    public ObservabilityTelemetryRunQuery? Query { get; set; }

    /// <summary>How far the run got: one of <c>ObservabilityTelemetryRunStatuses</c>.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>The span the run covered, in epoch milliseconds.</summary>
    [JsonPropertyName("timeframe")]
    public ObservabilityTelemetryTimeframe? Timeframe { get; set; }

    /// <summary>The user the run was executed as.</summary>
    [JsonPropertyName("userId")]
    public string? UserId { get; set; }

    /// <summary>When the run was created.</summary>
    [JsonPropertyName("created")]
    public string? Created { get; set; }

    /// <summary>What the run cost the database, when Cloudflare reports it per run.</summary>
    [JsonPropertyName("statistics")]
    public ObservabilityTelemetryStatistics? Statistics { get; set; }

    /// <summary>When the run was last updated.</summary>
    [JsonPropertyName("updated")]
    public string? Updated { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Whether this run finished, rather than only having started.</summary>
    [JsonIgnore]
    public bool IsComplete =>
        string.Equals(Status, ObservabilityTelemetryRunStatuses.Completed, StringComparison.Ordinal);

    /// <summary>Start of the span the run covered, as a date.</summary>
    [JsonIgnore]
    public DateTimeOffset? StartedAt => Timeframe?.FromUtc;

    /// <summary>End of the span the run covered, as a date.</summary>
    [JsonIgnore]
    public DateTimeOffset? EndedAt => Timeframe?.ToUtc;

    /// <summary>Id and status, on one line.</summary>
    /// <returns>The run described in one line.</returns>
    public override string ToString() => $"{Id} {Status}";
}

/// <summary>The query a run executed, as Cloudflare stored it.</summary>
/// <remarks>
/// An ad-hoc query is stored too, which is why this comes back: <see cref="Adhoc"/> says whether it was one,
/// and <see cref="Parameters"/> is the echo of what was sent. A saved query can then be re-run by its id
/// alone, sending no parameters.
/// </remarks>
public sealed class ObservabilityTelemetryRunQuery
{
    /// <summary>The query's id, which is the one the request named.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Whether the query was ad-hoc rather than one saved beforehand.</summary>
    [JsonPropertyName("adhoc")]
    public bool? Adhoc { get; set; }

    /// <summary>When the query was created.</summary>
    [JsonPropertyName("created")]
    public string? Created { get; set; }

    /// <summary>Who created it.</summary>
    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; set; }

    /// <summary>The query's description, which Cloudflare holds as null when it has none.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>The query's name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>The parameters it ran with.</summary>
    [JsonPropertyName("parameters")]
    public ObservabilityTelemetryRunQueryParameters? Parameters { get; set; }

    /// <summary>When the query was last updated.</summary>
    [JsonPropertyName("updated")]
    public string? Updated { get; set; }

    /// <summary>Who last updated it.</summary>
    [JsonPropertyName("updatedBy")]
    public string? UpdatedBy { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Name and id, on one line.</summary>
    /// <returns>The query described in one line.</returns>
    public override string ToString() => $"{Name} ({Id})";
}

/// <summary>The parameters a run reports having executed with.</summary>
/// <remarks>
/// This is the echo of <c>ObservabilityTelemetryQueryParameters</c>, and it is its own type for one reason:
/// the request writes each filter node with the <c>kind</c> discriminator that says whether it is a leaf or
/// a group, but the published schema makes that field optional on a leaf - so an echo can carry a filter
/// with no <c>kind</c> at all, and reading that into the polymorphic tree would fail on a field Cloudflare
/// merely left out. The echo therefore keeps its filters as the raw JSON Cloudflare stored, and everything
/// else is shared with the request type.
/// </remarks>
public sealed class ObservabilityTelemetryRunQueryParameters
{
    /// <summary>The calculations the query asked for.</summary>
    [JsonPropertyName("calculations")]
    public List<ObservabilityTelemetryCalculation>? Calculations { get; set; }

    /// <summary>The datasets it queried.</summary>
    [JsonPropertyName("datasets")]
    public List<string>? Datasets { get; set; }

    /// <summary>How its top-level filters combined.</summary>
    [JsonPropertyName("filterCombination")]
    public string? FilterCombination { get; set; }

    /// <summary>
    /// Its filters, as the JSON Cloudflare stored them. See the remarks on this type for why these are not
    /// read into the filter tree.
    /// </summary>
    [JsonPropertyName("filters")]
    public List<JsonElement>? Filters { get; set; }

    /// <summary>The fields it grouped by.</summary>
    [JsonPropertyName("groupBys")]
    public List<ObservabilityTelemetryGroupBy>? GroupBys { get; set; }

    /// <summary>The thresholds it filtered groups by after aggregating.</summary>
    [JsonPropertyName("havings")]
    public List<ObservabilityTelemetryHaving>? Havings { get; set; }

    /// <summary>How many group-by rows it asked for.</summary>
    [JsonPropertyName("limit")]
    public int? Limit { get; set; }

    /// <summary>The full-text term it searched for.</summary>
    [JsonPropertyName("needle")]
    public ObservabilityTelemetryNeedle? Needle { get; set; }

    /// <summary>How it ordered grouped results.</summary>
    [JsonPropertyName("orderBy")]
    public ObservabilityTelemetryOrderBy? OrderBy { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>What a telemetry query cost the database behind it.</summary>
/// <remarks>
/// These are the only fields in the whole answer that Cloudflare spells in snake_case rather than camelCase,
/// which is why each carries its wire name explicitly. They cover the query itself and not the network
/// round trip.
/// </remarks>
public sealed class ObservabilityTelemetryStatistics
{
    /// <summary>Uncompressed bytes read from the table.</summary>
    [JsonPropertyName("bytes_read")]
    public long? BytesRead { get; set; }

    /// <summary>Seconds the query took to run.</summary>
    [JsonPropertyName("elapsed")]
    public double? Elapsed { get; set; }

    /// <summary>Rows scanned from the table.</summary>
    [JsonPropertyName("rows_read")]
    public long? RowsRead { get; set; }

    /// <summary>
    /// The level of adaptive bit rate sampling the query used. Absent means level 1, which is no sampling.
    /// </summary>
    [JsonPropertyName("abr_level")]
    public int? AbrLevel { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Rows, bytes and seconds, on one line.</summary>
    /// <returns>The statistics described in one line.</returns>
    public override string ToString() =>
        $"{RowsRead?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"} rows, " +
        $"{BytesRead?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"} bytes, " +
        $"{Elapsed?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? "?"}s";
}

/// <summary>One agent run: a trace that contained at least one agent invocation.</summary>
/// <remarks>
/// Filled by the <c>agents</c> view. <see cref="Id"/> is a stable cursor, so it is what a next page is asked
/// for with the request's <c>offset</c>. The token counts are informational and are not billing data.
/// </remarks>
public sealed class ObservabilityTelemetryAgent
{
    /// <summary>A stable pagination cursor for this agent run.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>The distinct errors the run's spans reported.</summary>
    [JsonPropertyName("errors")]
    public List<string>? Errors { get; set; }

    /// <summary>The distinct models its chat spans reported.</summary>
    [JsonPropertyName("models")]
    public List<string>? Models { get; set; }

    /// <summary>The distinct generative-AI providers its chat spans reported.</summary>
    [JsonPropertyName("providers")]
    public List<string>? Providers { get; set; }

    /// <summary>The Worker services represented in the run's trace.</summary>
    [JsonPropertyName("services")]
    public List<string>? Services { get; set; }

    /// <summary>How many spans the run's trace holds.</summary>
    [JsonPropertyName("spans")]
    public long? Spans { get; set; }

    /// <summary>The observed run status: <c>completed</c> or <c>error</c>.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Total duration of the run's trace, in milliseconds.</summary>
    [JsonPropertyName("traceDurationMs")]
    public double? TraceDurationMs { get; set; }

    /// <summary>End of the run's trace, as epoch milliseconds.</summary>
    [JsonPropertyName("traceEndMs")]
    public long? TraceEndMs { get; set; }

    /// <summary>The trace this agent run belongs to.</summary>
    [JsonPropertyName("traceId")]
    public string? TraceId { get; set; }

    /// <summary>Start of the run's trace, as epoch milliseconds.</summary>
    [JsonPropertyName("traceStartMs")]
    public long? TraceStartMs { get; set; }

    /// <summary>The agent id from the earliest invocation in the run that has one.</summary>
    [JsonPropertyName("agentId")]
    public string? AgentId { get; set; }

    /// <summary>The agent name from the earliest invocation in the run that has one.</summary>
    [JsonPropertyName("agentName")]
    public string? AgentName { get; set; }

    /// <summary>The conversation id from the earliest invocation in the run that has one.</summary>
    [JsonPropertyName("conversationId")]
    public string? ConversationId { get; set; }

    /// <summary>Input tokens summed across the run's chat spans.</summary>
    [JsonPropertyName("inputTokens")]
    public long? InputTokens { get; set; }

    /// <summary>Output tokens summed across the run's chat spans.</summary>
    [JsonPropertyName("outputTokens")]
    public long? OutputTokens { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Start and end of the run's trace, as dates.</summary>
    [JsonIgnore]
    public (DateTimeOffset Start, DateTimeOffset End)? Span
    {
        get
        {
            if (TraceStartMs is not { } start || TraceEndMs is not { } end)
            {
                return null;
            }

            return (
                DateTimeOffset.FromUnixTimeMilliseconds(start),
                DateTimeOffset.FromUnixTimeMilliseconds(end));
        }
    }

    /// <summary>Agent, status and span count, on one line.</summary>
    /// <returns>The agent run described in one line.</returns>
    public override string ToString() => $"{AgentName ?? AgentId ?? Id} {Status} ({Spans} spans)";
}

/// <summary>One aggregated calculation: its overall value, and its value over time.</summary>
/// <remarks>
/// Filled by the <c>calculations</c> view, and by <c>compare</c> too - the two are the same shape, and the
/// comparison one covers the previous period of equal length. <see cref="Calculation"/> names what was
/// computed, matching the alias the request gave it or the operator it used.
/// </remarks>
public sealed class ObservabilityTelemetryCalculationResult
{
    /// <summary>The aggregate over the whole span, one entry per group when the query grouped.</summary>
    [JsonPropertyName("aggregates")]
    public List<ObservabilityTelemetryAggregate>? Aggregates { get; set; }

    /// <summary>What was calculated: the request's alias for it, or the operator.</summary>
    [JsonPropertyName("calculation")]
    public string? Calculation { get; set; }

    /// <summary>The same numbers bucketed over time, unless the query asked to ignore series.</summary>
    [JsonPropertyName("series")]
    public List<ObservabilityTelemetrySeries>? Series { get; set; }

    /// <summary>The alias the request gave this calculation, when it gave one.</summary>
    [JsonPropertyName("alias")]
    public string? Alias { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>
    /// The aggregate for the whole span, which is the first one when the query did not group by anything.
    /// </summary>
    [JsonIgnore]
    public ObservabilityTelemetryAggregate? Total =>
        Aggregates is { Count: > 0 } aggregates ? aggregates[0] : null;

    /// <summary>Name and overall value, on one line.</summary>
    /// <returns>The calculation described in one line.</returns>
    public override string ToString() => $"{Calculation ?? Alias}={Total?.Value?.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>One aggregate: the result of a calculation, over the whole span or over one group.</summary>
public sealed class ObservabilityTelemetryAggregate
{
    /// <summary>
    /// Estimated number of matching events, being the sum of the sample intervals of the stored ones. It is
    /// the exact number of events when <see cref="SampleInterval"/> is 1.
    /// </summary>
    [JsonPropertyName("count")]
    public double? Count { get; set; }

    /// <summary>A deprecated alias of <see cref="SampleInterval"/>, always holding the same value.</summary>
    [JsonPropertyName("interval")]
    public double? Interval { get; set; }

    /// <summary>
    /// The average sample interval of the matched events, each of which was stored with an interval of one
    /// over the sampling rate applied when it was ingested. 1 means nothing was sampled; above 1 means
    /// <see cref="Count"/> and <see cref="Value"/> are estimates rather than exact figures.
    /// </summary>
    [JsonPropertyName("sampleInterval")]
    public double? SampleInterval { get; set; }

    /// <summary>
    /// The result of the calculation. Counts, sums, averages, medians and percentiles are each weighted by
    /// the sample interval of the events behind them.
    /// </summary>
    [JsonPropertyName("value")]
    public double? Value { get; set; }

    /// <summary>The group-by values this aggregate is for, when the query grouped.</summary>
    [JsonPropertyName("groups")]
    public List<ObservabilityTelemetryGroup>? Groups { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Whether this aggregate was estimated from sampled events rather than counted exactly.</summary>
    [JsonIgnore]
    public bool IsSampled => SampleInterval is { } interval && interval > 1;

    /// <summary>Value and event count, on one line.</summary>
    /// <returns>The aggregate described in one line.</returns>
    public override string ToString() =>
        $"{Value?.ToString(System.Globalization.CultureInfo.InvariantCulture)} over " +
        $"{Count?.ToString(System.Globalization.CultureInfo.InvariantCulture)} events";
}

/// <summary>One group-by value an aggregate or a series point is for.</summary>
public sealed class ObservabilityTelemetryGroup
{
    /// <summary>The field that was grouped by.</summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    /// <summary>The value this row of results covers: a string, a number or a boolean.</summary>
    [JsonPropertyName("value")]
    public JsonElement? Value { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The value as text, whichever kind of it Cloudflare sent.</summary>
    [JsonIgnore]
    public string? ValueText
    {
        get
        {
            if (Value is not { } value)
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
        }
    }

    /// <summary>Field and value, on one line.</summary>
    /// <returns>The group described in one line.</returns>
    public override string ToString() => $"{Key}={ValueText}";
}

/// <summary>One calculation's numbers over one bucket of time.</summary>
public sealed class ObservabilityTelemetrySeries
{
    /// <summary>The bucket's numbers, one entry per group when the query grouped.</summary>
    [JsonPropertyName("data")]
    public List<ObservabilityTelemetrySeriesPoint>? Data { get; set; }

    /// <summary>The bucket's start, as an ISO-8601 string.</summary>
    [JsonPropertyName("time")]
    public string? Time { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The bucket's start, as a date, when Cloudflare sent one this can read.</summary>
    [JsonIgnore]
    public DateTimeOffset? TimeUtc =>
        DateTimeOffset.TryParse(
            Time,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;

    /// <summary>Bucket and point count, on one line.</summary>
    /// <returns>The series bucket described in one line.</returns>
    public override string ToString() => $"{Time} ({Data?.Count ?? 0})";
}

/// <summary>One calculation's value in one bucket of time.</summary>
public sealed class ObservabilityTelemetrySeriesPoint
{
    /// <summary>Estimated number of matching events in the bucket.</summary>
    [JsonPropertyName("count")]
    public double? Count { get; set; }

    /// <summary>A deprecated alias of <see cref="SampleInterval"/>, always holding the same value.</summary>
    [JsonPropertyName("interval")]
    public double? Interval { get; set; }

    /// <summary>The average sample interval behind the bucket, where 1 means nothing was sampled.</summary>
    [JsonPropertyName("sampleInterval")]
    public double? SampleInterval { get; set; }

    /// <summary>The result of the calculation in this bucket.</summary>
    [JsonPropertyName("value")]
    public double? Value { get; set; }

    /// <summary>When the first event behind this bucket was seen.</summary>
    [JsonPropertyName("firstSeen")]
    public string? FirstSeen { get; set; }

    /// <summary>The group-by values this point is for, when the query grouped.</summary>
    [JsonPropertyName("groups")]
    public List<ObservabilityTelemetryGroup>? Groups { get; set; }

    /// <summary>When the last event behind this bucket was seen.</summary>
    [JsonPropertyName("lastSeen")]
    public string? LastSeen { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>A bucketed two-dimensional histogram of one numeric field over time.</summary>
/// <remarks>
/// Returned instead of calculations when <c>chartType</c> is <c>distribution</c>. Read it as a grid:
/// <c>Matrix[bucket][bin]</c> is the estimated number of events whose value fell in
/// <c>Buckets[bucket]</c> during the time bin <c>Bins[bin]</c>.
/// </remarks>
public sealed class ObservabilityTelemetryDistribution
{
    /// <summary>Time-bucket labels, ISO-8601 strings, one per column of <see cref="Matrix"/>.</summary>
    [JsonPropertyName("bins")]
    public List<string>? Bins { get; set; }

    /// <summary>
    /// The raw bucket edges in the value's own unit, one longer than <see cref="Buckets"/>: what the colour
    /// scale and the percentile mapping are drawn from.
    /// </summary>
    [JsonPropertyName("bucketBoundaries")]
    public List<double>? BucketBoundaries { get; set; }

    /// <summary>
    /// How the boundaries were derived: one of <c>ObservabilityTelemetryDistributionScales</c>, being the
    /// scale the request asked for.
    /// </summary>
    [JsonPropertyName("bucketMode")]
    public string? BucketMode { get; set; }

    /// <summary>Value-range labels, one per row of <see cref="Matrix"/>, such as <c>50-100ms</c>.</summary>
    [JsonPropertyName("buckets")]
    public List<string>? Buckets { get; set; }

    /// <summary>Sampling-corrected counts, indexed value bucket first and time bin second.</summary>
    [JsonPropertyName("matrix")]
    public List<List<double>>? Matrix { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Shape of the grid, on one line.</summary>
    /// <returns>The distribution described in one line.</returns>
    public override string ToString() => $"{Buckets?.Count ?? 0}x{Bins?.Count ?? 0} {BucketMode}";
}

/// <summary>The events a query matched, and the fields and series Cloudflare drew from them.</summary>
public sealed class ObservabilityTelemetryEvents
{
    /// <summary>
    /// How many events matched in total, which can be more than came back when the request limited them.
    /// </summary>
    [JsonPropertyName("count")]
    public long? Count { get; set; }

    /// <summary>The events themselves.</summary>
    [JsonPropertyName("events")]
    public List<ObservabilityTelemetryEvent>? Events { get; set; }

    /// <summary>The fields found in the matched events, which is what a dynamic view is built from.</summary>
    [JsonPropertyName("fields")]
    public List<ObservabilityTelemetryEventField>? Fields { get; set; }

    /// <summary>The matched events bucketed over time, at the query's granularity.</summary>
    [JsonPropertyName("series")]
    public List<ObservabilityTelemetryEventSeries>? Series { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>How many came back and how many matched, on one line.</summary>
    /// <returns>The event results described in one line.</returns>
    public override string ToString() => $"{Events?.Count ?? 0} of {Count}";
}

/// <summary>
/// One telemetry event: a log line, a span, or a metric data point a Worker emitted.
/// </summary>
/// <remarks>
/// <para>
/// This one type serves both the <c>events</c> view and the <c>invocations</c> view, because the published
/// schema defines the two identically - an invocation is the same event, reached through the request id that
/// groups it. Its own fields are the same in both, down to the optionality of each.
/// </para>
/// <para>
/// <see cref="Metadata"/> holds the fields that are indexed, so those are the ones a filter or a calculation
/// can name; <see cref="Source"/> is the payload as it was emitted.
/// </para>
/// </remarks>
public sealed class ObservabilityTelemetryEvent
{
    /// <summary>The structured metadata drawn out of the event, which is what filtering and aggregation use.</summary>
    [JsonPropertyName("$metadata")]
    public ObservabilityTelemetryEventMetadata? Metadata { get; set; }

    /// <summary>The dataset the event belongs to, such as <c>cloudflare-workers</c>.</summary>
    [JsonPropertyName("dataset")]
    public string? Dataset { get; set; }

    /// <summary>
    /// The raw log payload: a string or a structured object, depending on how the log was emitted.
    /// </summary>
    [JsonPropertyName("source")]
    public JsonElement? Source { get; set; }

    /// <summary>When the event happened, as epoch milliseconds.</summary>
    [JsonPropertyName("timestamp")]
    public long? Timestamp { get; set; }

    /// <summary>Cloudflare Containers information enriching the event, when it came from a container.</summary>
    [JsonPropertyName("$containers")]
    public IDictionary<string, JsonElement>? Containers { get; set; }

    /// <summary>Cloudflare Workers information enriching the event: which script, which trigger, which outcome.</summary>
    [JsonPropertyName("$workers")]
    public ObservabilityTelemetryWorkersContext? Workers { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>When the event happened, as a date.</summary>
    [JsonIgnore]
    public DateTimeOffset? TimestampUtc =>
        Timestamp is { } timestamp ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp) : null;

    /// <summary>
    /// The payload as text, whichever kind of it Cloudflare sent: a string unquoted, an object as its JSON.
    /// </summary>
    [JsonIgnore]
    public string? SourceText
    {
        get
        {
            if (Source is not { } source)
            {
                return null;
            }

            return source.ValueKind == JsonValueKind.String ? source.GetString() : source.GetRawText();
        }
    }

    /// <summary>Time, level and message, on one line.</summary>
    /// <returns>The event described in one line.</returns>
    public override string ToString() =>
        $"{Metadata?.Level ?? "?"} {Metadata?.Message ?? SourceText} ({Dataset})";
}

/// <summary>
/// The indexed fields of one telemetry event: the ones a filter, a group-by or a calculation can name.
/// </summary>
/// <remarks>
/// Every field but <see cref="Id"/> is optional, because an event carries the ones its kind has: a log line
/// has a <see cref="Message"/> and a <see cref="Level"/>, a span has ids, statuses and durations, a metric
/// point has a <see cref="MetricName"/>. The names here are what the filter keys are, prefixed with
/// <c>$metadata.</c> - which is why the key list answers keys like <c>$metadata.message</c>.
/// </remarks>
public sealed class ObservabilityTelemetryEventMetadata
{
    /// <summary>The event's id, which is the cursor a next page of events is asked for with.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>The account that produced the event.</summary>
    [JsonPropertyName("account")]
    public string? Account { get; set; }

    /// <summary>The Cloudflare product that produced it, such as <c>workers</c> or <c>pages</c>.</summary>
    [JsonPropertyName("cloudService")]
    public string? CloudService { get; set; }

    /// <summary>Whether the invocation started a cold isolate.</summary>
    [JsonPropertyName("coldStart")]
    public double? ColdStart { get; set; }

    /// <summary>Estimated cost units for the invocation.</summary>
    [JsonPropertyName("cost")]
    public double? Cost { get; set; }

    /// <summary>Duration of the span, in milliseconds.</summary>
    [JsonPropertyName("duration")]
    public double? Duration { get; set; }

    /// <summary>When the span ended, as epoch milliseconds.</summary>
    [JsonPropertyName("endTime")]
    public long? EndTime { get; set; }

    /// <summary>When the span ended, as epoch nanoseconds - too large for a number, so a string.</summary>
    [JsonPropertyName("endTimeNs")]
    public string? EndTimeNs { get; set; }

    /// <summary>The error message, when the event represents one.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>A templatized error message, which is what similar errors are grouped by.</summary>
    [JsonPropertyName("errorTemplate")]
    public string? ErrorTemplate { get; set; }

    /// <summary>Size of the stored event, in bytes.</summary>
    [JsonPropertyName("eventSize")]
    public long? EventSize { get; set; }

    /// <summary>A content-based fingerprint, which is what similar events are grouped by.</summary>
    [JsonPropertyName("fingerprint")]
    public string? Fingerprint { get; set; }

    /// <summary>The log level, such as <c>log</c>, <c>debug</c>, <c>info</c>, <c>warn</c> or <c>error</c>.</summary>
    [JsonPropertyName("level")]
    public string? Level { get; set; }

    /// <summary>The log message text.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>A templatized log message, which is what similar messages are grouped by.</summary>
    [JsonPropertyName("messageTemplate")]
    public string? MessageTemplate { get; set; }

    /// <summary>The metric name, when the event is a metric data point.</summary>
    [JsonPropertyName("metricName")]
    public string? MetricName { get; set; }

    /// <summary>Where the event came from, such as <c>fetch</c>, <c>scheduled</c> or <c>queue</c>.</summary>
    [JsonPropertyName("origin")]
    public string? Origin { get; set; }

    /// <summary>The id of the span above this one in the trace.</summary>
    [JsonPropertyName("parentSpanId")]
    public string? ParentSpanId { get; set; }

    /// <summary>The infrastructure provider behind the event.</summary>
    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    /// <summary>
    /// The Cloudflare ray id, from the <c>CF-RAY</c> header of the request that triggered the invocation.
    /// </summary>
    [JsonPropertyName("rayId")]
    public string? RayId { get; set; }

    /// <summary>The data centre or region that handled the request.</summary>
    [JsonPropertyName("region")]
    public string? Region { get; set; }

    /// <summary>The request id tying every log from one invocation together.</summary>
    [JsonPropertyName("requestId")]
    public string? RequestId { get; set; }

    /// <summary>The Worker script name that produced the event.</summary>
    [JsonPropertyName("service")]
    public string? Service { get; set; }

    /// <summary>The id of this individual unit of work within its trace.</summary>
    [JsonPropertyName("spanId")]
    public string? SpanId { get; set; }

    /// <summary>The span's human-readable name.</summary>
    [JsonPropertyName("spanName")]
    public string? SpanName { get; set; }

    /// <summary>
    /// The OpenTelemetry span status: <c>unset</c>, <c>ok</c> or <c>error</c>. Only spans have one, and
    /// <c>unset</c> covers a span that never set one.
    /// </summary>
    [JsonPropertyName("spanStatus")]
    public string? SpanStatus { get; set; }

    /// <summary>The span status message, only present on a span whose status is <c>error</c> and which set one.</summary>
    [JsonPropertyName("spanStatusMessage")]
    public string? SpanStatusMessage { get; set; }

    /// <summary>The stack or deployment behind the event.</summary>
    [JsonPropertyName("stackId")]
    public string? StackId { get; set; }

    /// <summary>When the span started, as epoch milliseconds.</summary>
    [JsonPropertyName("startTime")]
    public long? StartTime { get; set; }

    /// <summary>When the span started, as epoch nanoseconds - too large for a number, so a string.</summary>
    [JsonPropertyName("startTimeNs")]
    public string? StartTimeNs { get; set; }

    /// <summary>The HTTP status code the Worker answered with.</summary>
    [JsonPropertyName("statusCode")]
    public long? StatusCode { get; set; }

    /// <summary>When the event happened, as epoch nanoseconds.</summary>
    [JsonPropertyName("timestampNs")]
    public string? TimestampNs { get; set; }

    /// <summary>Total duration of the whole trace, in milliseconds.</summary>
    [JsonPropertyName("traceDuration")]
    public double? TraceDuration { get; set; }

    /// <summary>The trace id linking this event's spans across services.</summary>
    [JsonPropertyName("traceId")]
    public string? TraceId { get; set; }

    /// <summary>The logical transaction name for the request.</summary>
    [JsonPropertyName("transactionName")]
    public string? TransactionName { get; set; }

    /// <summary>
    /// What triggered the invocation, such as <c>GET /users</c>, <c>POST /orders</c> or a queue message.
    /// </summary>
    [JsonPropertyName("trigger")]
    public string? Trigger { get; set; }

    /// <summary>The event's classifier, such as <c>cf-worker-event</c> or <c>cf-worker-log</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>The request URL that triggered the invocation.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Level, message and service, on one line.</summary>
    /// <returns>The metadata described in one line.</returns>
    public override string ToString() => $"{Level ?? Type ?? "?"} {Message ?? Error ?? ""} ({Service})";
}

/// <summary>The Workers context Cloudflare attaches to an event: which script ran it and how it went.</summary>
/// <remarks>
/// The published schema defines this as a union of two arms which differ only in which of these fields are
/// required: the fuller one adds <see cref="CpuTimeMs"/>, <see cref="WallTimeMs"/> and
/// <see cref="Outcome"/> as required and brings <see cref="DispatchNamespace"/> and
/// <see cref="DiagnosticsChannelEvents"/> with it. One type with everything optional reads either arm, which
/// is why there is not a second class here.
/// </remarks>
public sealed class ObservabilityTelemetryWorkersContext
{
    /// <summary>What the Worker was invoked as: <c>fetch</c>, <c>scheduled</c>, <c>cron</c>, <c>queue</c> and the rest.</summary>
    [JsonPropertyName("eventType")]
    public string? EventType { get; set; }

    /// <summary>The script that ran.</summary>
    [JsonPropertyName("scriptName")]
    public string? ScriptName { get; set; }

    /// <summary>CPU time the invocation used, in milliseconds.</summary>
    [JsonPropertyName("cpuTimeMs")]
    public double? CpuTimeMs { get; set; }

    /// <summary>Wall-clock time the invocation took, in milliseconds.</summary>
    [JsonPropertyName("wallTimeMs")]
    public double? WallTimeMs { get; set; }

    /// <summary>How the invocation ended.</summary>
    [JsonPropertyName("outcome")]
    public string? Outcome { get; set; }

    /// <summary>The dispatch namespace the script was reached through, when it was.</summary>
    [JsonPropertyName("dispatchNamespace")]
    public string? DispatchNamespace { get; set; }

    /// <summary>The id of the Durable Object that handled it, when one did.</summary>
    [JsonPropertyName("durableObjectId")]
    public string? DurableObjectId { get; set; }

    /// <summary>The class export that handled it, when the script has more than one.</summary>
    [JsonPropertyName("entrypoint")]
    public string? Entrypoint { get; set; }

    /// <summary>The event object the handler was passed, as Cloudflare recorded it.</summary>
    [JsonPropertyName("event")]
    public IDictionary<string, JsonElement>? Event { get; set; }

    /// <summary>Whether it ran in a Durable Object or statelessly.</summary>
    [JsonPropertyName("executionModel")]
    public string? ExecutionModel { get; set; }

    /// <summary>The preview version that served it, when a preview did.</summary>
    [JsonPropertyName("preview")]
    public ObservabilityTelemetryWorkersPreview? Preview { get; set; }

    /// <summary>The request id, which is what the <c>invocations</c> view groups by.</summary>
    [JsonPropertyName("requestId")]
    public string? RequestId { get; set; }

    /// <summary>The script version that served it.</summary>
    [JsonPropertyName("scriptVersion")]
    public ObservabilityTelemetryScriptVersion? ScriptVersion { get; set; }

    /// <summary>The span this invocation opened, when it opened one.</summary>
    [JsonPropertyName("spanId")]
    public string? SpanId { get; set; }

    /// <summary>The trace this invocation belongs to.</summary>
    [JsonPropertyName("traceId")]
    public string? TraceId { get; set; }

    /// <summary>Whether Cloudflare had to truncate what it recorded.</summary>
    [JsonPropertyName("truncated")]
    public bool? Truncated { get; set; }

    /// <summary>The diagnostics-channel events the invocation emitted.</summary>
    [JsonPropertyName("diagnosticsChannelEvents")]
    public List<ObservabilityTelemetryDiagnosticsChannelEvent>? DiagnosticsChannelEvents { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Script, event type and outcome, on one line.</summary>
    /// <returns>The Workers context described in one line.</returns>
    public override string ToString() => $"{ScriptName} {EventType} {Outcome}";
}

/// <summary>The preview version that served an invocation.</summary>
public sealed class ObservabilityTelemetryWorkersPreview
{
    /// <summary>The preview's id.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>The preview's name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>The preview's slug, which is what its URL carries.</summary>
    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>The script version that served an invocation.</summary>
public sealed class ObservabilityTelemetryScriptVersion
{
    /// <summary>The version's id.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>The message the version was deployed with.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>The version's tag.</summary>
    [JsonPropertyName("tag")]
    public string? Tag { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>One diagnostics-channel event an invocation emitted.</summary>
public sealed class ObservabilityTelemetryDiagnosticsChannelEvent
{
    /// <summary>The channel it was written to.</summary>
    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    /// <summary>What was written.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>When it was written, as epoch milliseconds.</summary>
    [JsonPropertyName("timestamp")]
    public long? Timestamp { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>One field found in the events a query matched.</summary>
/// <remarks>
/// This is what a view that builds itself from the data is built from: the names a caller can filter on and
/// what type each holds.
/// </remarks>
public sealed class ObservabilityTelemetryEventField
{
    /// <summary>The field's name, as it appears in the matched events.</summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    /// <summary>What the field holds: <c>string</c>, <c>number</c> or <c>boolean</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Name and type, on one line.</summary>
    /// <returns>The field described in one line.</returns>
    public override string ToString() => $"{Key} ({Type})";
}

/// <summary>The matched events bucketed over time.</summary>
public sealed class ObservabilityTelemetryEventSeries
{
    /// <summary>The bucket's numbers, one entry per group when the query grouped.</summary>
    [JsonPropertyName("data")]
    public List<ObservabilityTelemetryEventSeriesPoint>? Data { get; set; }

    /// <summary>The bucket's start, as an ISO-8601 string.</summary>
    [JsonPropertyName("time")]
    public string? Time { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The bucket's start, as a date, when Cloudflare sent one this can read.</summary>
    [JsonIgnore]
    public DateTimeOffset? TimeUtc =>
        DateTimeOffset.TryParse(
            Time,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;

    /// <summary>Bucket and point count, on one line.</summary>
    /// <returns>The series bucket described in one line.</returns>
    public override string ToString() => $"{Time} ({Data?.Count ?? 0})";
}

/// <summary>How many events matched in one bucket of time.</summary>
public sealed class ObservabilityTelemetryEventSeriesPoint
{
    /// <summary>Deprecated companions of the point's own numbers, which Cloudflare still sends.</summary>
    [JsonPropertyName("aggregates")]
    public ObservabilityTelemetryEventAggregates? Aggregates { get; set; }

    /// <summary>Estimated number of matching events in the bucket.</summary>
    [JsonPropertyName("count")]
    public double? Count { get; set; }

    /// <summary>A deprecated alias of <see cref="SampleInterval"/>, always holding the same value.</summary>
    [JsonPropertyName("interval")]
    public double? Interval { get; set; }

    /// <summary>The average sample interval behind the bucket, where 1 means nothing was sampled.</summary>
    [JsonPropertyName("sampleInterval")]
    public double? SampleInterval { get; set; }

    /// <summary>How many of the bucket's events were errors.</summary>
    [JsonPropertyName("errors")]
    public double? Errors { get; set; }

    /// <summary>
    /// The group-by values this point is for, when the query grouped. Keyed by field rather than listed,
    /// which is not how the calculations view spells its groups.
    /// </summary>
    [JsonPropertyName("groups")]
    public IDictionary<string, JsonElement>? Groups { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>The deprecated aggregate fields Cloudflare still sends alongside an event series point.</summary>
/// <remarks>
/// Every field here is marked deprecated in the published schema; the point's own <c>count</c> and
/// <c>sampleInterval</c> are the ones to read. They are typed rather than dropped so that nothing an answer
/// carries is silently lost.
/// </remarks>
public sealed class ObservabilityTelemetryEventAggregates
{
    /// <summary>Deprecated companion of the point's count.</summary>
    [JsonPropertyName("_count")]
    public double? Count { get; set; }

    /// <summary>Deprecated companion of the point's sample interval.</summary>
    [JsonPropertyName("_interval")]
    public double? Interval { get; set; }

    /// <summary>Deprecated: when the first event behind the point was seen.</summary>
    [JsonPropertyName("_firstSeen")]
    public string? FirstSeen { get; set; }

    /// <summary>Deprecated: when the last event behind the point was seen.</summary>
    [JsonPropertyName("_lastSeen")]
    public string? LastSeen { get; set; }

    /// <summary>Deprecated: the bin the point falls in, of a shape the schema leaves open.</summary>
    [JsonPropertyName("bin")]
    public JsonElement? Bin { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>One distributed trace a query matched, summarised rather than span by span.</summary>
public sealed class ObservabilityTelemetryTrace
{
    /// <summary>The name of the root span that started the trace.</summary>
    [JsonPropertyName("rootSpanName")]
    public string? RootSpanName { get; set; }

    /// <summary>The logical transaction name of the root span.</summary>
    [JsonPropertyName("rootTransactionName")]
    public string? RootTransactionName { get; set; }

    /// <summary>The Worker services the trace passed through.</summary>
    [JsonPropertyName("service")]
    public List<string>? Service { get; set; }

    /// <summary>How many spans the trace holds.</summary>
    [JsonPropertyName("spans")]
    public long? Spans { get; set; }

    /// <summary>Total duration of the trace, in milliseconds.</summary>
    [JsonPropertyName("traceDurationMs")]
    public double? TraceDurationMs { get; set; }

    /// <summary>When the trace ended, as epoch milliseconds.</summary>
    [JsonPropertyName("traceEndMs")]
    public long? TraceEndMs { get; set; }

    /// <summary>The trace's id.</summary>
    [JsonPropertyName("traceId")]
    public string? TraceId { get; set; }

    /// <summary>When the trace started, as epoch milliseconds.</summary>
    [JsonPropertyName("traceStartMs")]
    public long? TraceStartMs { get; set; }

    /// <summary>The error messages the trace ran into, when it ran into any.</summary>
    [JsonPropertyName("errors")]
    public List<string>? Errors { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Start and end of the trace, as dates.</summary>
    [JsonIgnore]
    public (DateTimeOffset Start, DateTimeOffset End)? Span
    {
        get
        {
            if (TraceStartMs is not { } start || TraceEndMs is not { } end)
            {
                return null;
            }

            return (
                DateTimeOffset.FromUnixTimeMilliseconds(start),
                DateTimeOffset.FromUnixTimeMilliseconds(end));
        }
    }

    /// <summary>Transaction, span count and duration, on one line.</summary>
    /// <returns>The trace described in one line.</returns>
    public override string ToString() =>
        $"{RootTransactionName ?? RootSpanName} ({Spans} spans, " +
        $"{TraceDurationMs?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}ms)";
}

/// <summary>
/// Asks Cloudflare to run a query over the account's telemetry.
/// </summary>
/// <remarks>
/// <para>
/// The body of <c>POST /accounts/{account_id}/workers/observability/telemetry/query</c>. Two fields are
/// required: <see cref="QueryId"/> and <see cref="Timeframe"/>, whose ends are epoch milliseconds with the
/// start earlier than the end - narrower spans answer faster and more specifically.
/// </para>
/// <para>
/// The query id does double duty. Sending <see cref="Parameters"/> makes this an ad-hoc query, and the id is
/// then any identifier - <see cref="AdHoc"/> picks one. Leaving the parameters out asks Cloudflare to run a
/// query it already holds under that id, which is how a saved one is re-run. Either way the run comes back
/// with the query it executed, so an ad-hoc id can be used again later.
/// </para>
/// <para>
/// What arrives in the answer is decided by <see cref="View"/>, and the paging fields work differently
/// depending on it: <see cref="Offset"/> is a cursor for the event, trace, invocation and agent views - the
/// id of the last event, the trace cursor, or the agent run id - while <see cref="OffsetBy"/> is a numeric
/// offset for grouped and top-N results and is used with <see cref="Limit"/> instead.
/// </para>
/// </remarks>
public sealed class ObservabilityTelemetryQueryRequest
{
    /// <summary>The query id this run is recorded under, and the one a saved query is re-run by.</summary>
    [JsonPropertyName("queryId")]
    public string? QueryId { get; set; }

    /// <summary>The span to query, in epoch milliseconds.</summary>
    [JsonPropertyName("timeframe")]
    public ObservabilityTelemetryTimeframe? Timeframe { get; set; }

    /// <summary>What shape the answer takes: one of <c>ObservabilityTelemetryViews</c>.</summary>
    [JsonPropertyName("view")]
    public string? View { get; set; }

    /// <summary>What to retrieve - the filters, calculations, group-bys and ordering.</summary>
    [JsonPropertyName("parameters")]
    public ObservabilityTelemetryQueryParameters? Parameters { get; set; }

    /// <summary>Whether to include time-series data in the answer.</summary>
    [JsonPropertyName("chart")]
    public bool? Chart { get; set; }

    /// <summary>What to compute for the <c>calculations</c> view: one of <c>ObservabilityTelemetryChartTypes</c>.</summary>
    [JsonPropertyName("chartType")]
    public string? ChartType { get; set; }

    /// <summary>Whether to add the previous period of equal length as a comparison.</summary>
    [JsonPropertyName("compare")]
    public bool? Compare { get; set; }

    /// <summary>
    /// How the value axis of a distribution is bucketed: one of <c>ObservabilityTelemetryDistributionScales</c>.
    /// </summary>
    [JsonPropertyName("distributionScale")]
    public string? DistributionScale { get; set; }

    /// <summary>Whether to run the query without persisting its results, to validate or preview it.</summary>
    [JsonPropertyName("dry")]
    public bool? Dry { get; set; }

    /// <summary>
    /// How many time-series buckets to use, for the <c>calculations</c> view only. Left out, Cloudflare
    /// picks a granularity.
    /// </summary>
    [JsonPropertyName("granularity")]
    public int? Granularity { get; set; }

    /// <summary>Whether to leave the time series out and answer with aggregates only, which is a smaller result.</summary>
    [JsonPropertyName("ignoreSeries")]
    public bool? IgnoreSeries { get; set; }

    /// <summary>
    /// How many events to answer with for the <c>events</c> view, and how many group-by rows for the
    /// <c>calculations</c> one.
    /// </summary>
    [JsonPropertyName("limit")]
    public int? Limit { get; set; }

    /// <summary>The cursor for the next page of events, traces, invocations or agent runs.</summary>
    [JsonPropertyName("offset")]
    public string? Offset { get; set; }

    /// <summary>A numeric offset into grouped or top-N results, used with <see cref="Limit"/>.</summary>
    [JsonPropertyName("offsetBy")]
    public int? OffsetBy { get; set; }

    /// <summary>Which way to page: one of <c>ObservabilityTelemetryOffsetDirections</c>.</summary>
    [JsonPropertyName("offsetDirection")]
    public string? OffsetDirection { get; set; }

    /// <summary>The prefix an ad-hoc query id from this library starts with.</summary>
    public const string AdHocQueryIdPrefix = "cfnet-";

    /// <summary>Builds an ad-hoc query: one that carries its own parameters rather than naming a saved query.</summary>
    /// <param name="view">The view to ask for, one of <c>ObservabilityTelemetryViews</c>.</param>
    /// <param name="timeframe">The span to query.</param>
    /// <returns>A request with a generated query id.</returns>
    public static ObservabilityTelemetryQueryRequest AdHoc(
        string view,
        ObservabilityTelemetryTimeframe timeframe) =>
        new()
        {
            QueryId = NewAdHocQueryId(),
            View = view,
            Timeframe = timeframe,
        };

    /// <summary>Builds an ad-hoc query over a span of dates, converted to the milliseconds these endpoints count in.</summary>
    /// <param name="view">The view to ask for, one of <c>ObservabilityTelemetryViews</c>.</param>
    /// <param name="from">Start of the span.</param>
    /// <param name="to">End of the span.</param>
    /// <returns>A request with a generated query id and a timeframe in milliseconds.</returns>
    public static ObservabilityTelemetryQueryRequest OverSpan(
        string view,
        DateTimeOffset from,
        DateTimeOffset to) =>
        AdHoc(view, ObservabilityTelemetryTimeframe.Between(from, to));

    /// <summary>Generates the id an ad-hoc query is recorded under.</summary>
    /// <returns>An id starting with <see cref="AdHocQueryIdPrefix"/>.</returns>
    internal static string NewAdHocQueryId() => $"{AdHocQueryIdPrefix}{Guid.NewGuid():n}";

    /// <summary>
    /// Checks the request against what Cloudflare accepts, including the one combination the published
    /// schema says it answers with a 400.
    /// </summary>
    /// <exception cref="Exceptions.CloudflareConfigurationException">Something would be refused.</exception>
    public void EnsureValid()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(QueryId))
        {
            problems.Add(
                "QueryId is required: an ad-hoc query still names itself, and AdHoc() or OverSpan() pick an " +
                "id when there is no saved query to run.");
        }

        if (Timeframe is null)
        {
            problems.Add(
                "Timeframe is required: a query covers some span, in the epoch milliseconds these endpoints " +
                "count in.");
        }
        else
        {
            Timeframe.Validate(problems);
        }

        if (View is not null && !ObservabilityTelemetryViews.All.Contains(View))
        {
            problems.Add(
                $"View must be one of {string.Join(", ", ObservabilityTelemetryViews.All.Select(view => $"'{view}'"))}, " +
                $"but was '{View}'.");
        }

        if (ChartType is not null && !ObservabilityTelemetryChartTypes.All.Contains(ChartType))
        {
            problems.Add(
                $"ChartType must be one of {string.Join(", ", ObservabilityTelemetryChartTypes.All.Select(type => $"'{type}'"))}, " +
                $"but was '{ChartType}'.");
        }

        if (DistributionScale is not null &&
            !ObservabilityTelemetryDistributionScales.All.Contains(DistributionScale))
        {
            problems.Add(
                $"DistributionScale must be '{ObservabilityTelemetryDistributionScales.Log}' or " +
                $"'{ObservabilityTelemetryDistributionScales.Linear}', but was '{DistributionScale}'.");
        }

        if (ChartType == ObservabilityTelemetryChartTypes.Distribution && Compare == true)
        {
            problems.Add(
                $"ChartType '{ObservabilityTelemetryChartTypes.Distribution}' and Compare cannot be sent " +
                "together: a histogram covers one period, and Cloudflare refuses the pair with a 400.");
        }

        if (Granularity is < 1)
        {
            problems.Add($"Granularity must be at least 1 bucket, but was {Granularity}.");
        }

        if (Limit is < 1)
        {
            problems.Add($"Limit must be at least 1, but was {Limit}.");
        }

        if (OffsetBy is < 0)
        {
            problems.Add($"OffsetBy counts rows to skip, so it cannot be negative, but was {OffsetBy}.");
        }

        Parameters?.Validate(problems);

        if (problems.Count > 0)
        {
            throw new Exceptions.CloudflareConfigurationException(problems);
        }
    }

    /// <summary>View, span and query id, on one line.</summary>
    /// <returns>The request described in one line.</returns>
    public override string ToString() => $"{View ?? "?"} {Timeframe} ({QueryId})";
}

/// <summary>What a telemetry query retrieves: the filters, calculations, group-bys and ordering.</summary>
/// <remarks>
/// <para>
/// In practice an ad-hoc query always carries these; they are only left out when re-running a query Cloudflare
/// already holds by its id. The <c>keys</c> and <c>values</c> endpoints are what to ask first, because a
/// filter needs a key name and the type its values carry.
/// </para>
/// <para>
/// <see cref="Filters"/> is the same tree the other telemetry endpoints take - leaves and groups, capped at
/// four nested groups - and <see cref="Needle"/> is the same full-text search term.
/// <see cref="GroupBys"/>, <see cref="Havings"/> and <see cref="OrderBy"/> only apply to the
/// <c>calculations</c> view: group-bys break a calculation down, havings filter the groups after
/// aggregating, and ordering only takes effect once something is grouped.
/// </para>
/// </remarks>
public sealed class ObservabilityTelemetryQueryParameters
{
    /// <summary>The aggregations to compute, such as a count, an average or a percentile.</summary>
    [JsonPropertyName("calculations")]
    public List<ObservabilityTelemetryCalculation>? Calculations { get; set; }

    /// <summary>Which datasets to query. Empty means all of them.</summary>
    [JsonPropertyName("datasets")]
    public List<string>? Datasets { get; set; }

    /// <summary>How the top-level filters combine: one of <c>ObservabilityFilterCombinations</c>, and <c>and</c> when left out.</summary>
    [JsonPropertyName("filterCombination")]
    public string? FilterCombination { get; set; }

    /// <summary>Filters narrowing what the query matches.</summary>
    [JsonPropertyName("filters")]
    public List<ObservabilityTelemetryFilterNode>? Filters { get; set; }

    /// <summary>The fields to break calculation results down by.</summary>
    [JsonPropertyName("groupBys")]
    public List<ObservabilityTelemetryGroupBy>? GroupBys { get; set; }

    /// <summary>Thresholds applied to calculation results after aggregating, such as only groups above 100.</summary>
    [JsonPropertyName("havings")]
    public List<ObservabilityTelemetryHaving>? Havings { get; set; }

    /// <summary>How many group-by rows to answer with. Ten is a sensible default.</summary>
    [JsonPropertyName("limit")]
    public int? Limit { get; set; }

    /// <summary>A full-text term matched across every field of an event.</summary>
    [JsonPropertyName("needle")]
    public ObservabilityTelemetryNeedle? Needle { get; set; }

    /// <summary>How to order grouped calculation results.</summary>
    [JsonPropertyName("orderBy")]
    public ObservabilityTelemetryOrderBy? OrderBy { get; set; }

    /// <summary>
    /// Checks the parameters: the filter tree against the shape Cloudflare accepts, each calculation,
    /// group-by, having and ordering against what it needs.
    /// </summary>
    /// <param name="problems">Collects one message per problem found.</param>
    internal void Validate(List<string> problems)
    {
        if (FilterCombination is not null &&
            !string.Equals(FilterCombination, ObservabilityFilterCombinations.And, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(FilterCombination, ObservabilityFilterCombinations.Or, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add(
                $"Parameters.FilterCombination must be '{ObservabilityFilterCombinations.And}' or " +
                $"'{ObservabilityFilterCombinations.Or}', but was '{FilterCombination}'.");
        }

        ObservabilityTelemetryFilterNode.Validate(Filters, problems);

        if (Limit is < 1)
        {
            problems.Add($"Parameters.Limit must be at least 1, but was {Limit}.");
        }

        Needle?.Validate("Parameters.Needle", problems);

        if (Calculations is not null)
        {
            foreach (var calculation in Calculations.Where(calculation => calculation is not null))
            {
                calculation.Validate(problems);
            }
        }

        if (GroupBys is not null)
        {
            foreach (var groupBy in GroupBys.Where(groupBy => groupBy is not null))
            {
                groupBy.Validate(problems);
            }
        }

        if (Havings is not null)
        {
            foreach (var having in Havings.Where(having => having is not null))
            {
                having.Validate(problems);
            }
        }

        OrderBy?.Validate(problems);
    }
}

/// <summary>One aggregation a query asks for: an operator, and the key it works over unless it is a plain count.</summary>
/// <remarks>
/// The published schema defines this as a union of a count and a keyed calculation, and the two differ only
/// in whether <see cref="Key"/> is required: a count has nothing to count over, everything else does. One
/// type covers both arms, which is why <see cref="Key"/> is optional here and checked against the operator
/// rather than against which arm of a union this is.
/// </remarks>
public sealed class ObservabilityTelemetryCalculation
{
    /// <summary>What to compute: one of <c>ObservabilityTelemetryCalculationOperators</c>.</summary>
    [JsonPropertyName("operator")]
    public string? Operator { get; set; }

    /// <summary>The key to compute over, which everything but a plain count needs.</summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    /// <summary>What to call the result, which is also what a having or an ordering names.</summary>
    [JsonPropertyName("alias")]
    public string? Alias { get; set; }

    /// <summary>What the key's values are: one of <c>ObservabilityTelemetryValueTypes</c>.</summary>
    [JsonPropertyName("keyType")]
    public string? KeyType { get; set; }

    /// <summary>Builds a count of the matching events.</summary>
    /// <param name="alias">What to call the result, or null to leave it to the operator.</param>
    /// <returns>A count calculation.</returns>
    public static ObservabilityTelemetryCalculation Count(string? alias = null) =>
        new()
        {
            Operator = ObservabilityTelemetryCalculationOperators.Count,
            Alias = alias,
        };

    /// <summary>Builds a calculation over one key.</summary>
    /// <param name="key">The key to compute over.</param>
    /// <param name="operation">One of <c>ObservabilityTelemetryCalculationOperators</c>.</param>
    /// <param name="alias">What to call the result, or null to leave it to the operator.</param>
    /// <param name="keyType">What the key's values are, or null to leave it to Cloudflare.</param>
    /// <returns>A keyed calculation.</returns>
    public static ObservabilityTelemetryCalculation Of(
        string key,
        string operation,
        string? alias = null,
        string? keyType = null) =>
        new()
        {
            Key = key,
            Operator = operation,
            Alias = alias,
            KeyType = keyType,
        };

    /// <summary>
    /// Checks the calculation: that it has an operator, that anything but a plain count has a key, and that
    /// a key type is one of the three.
    /// </summary>
    /// <param name="problems">Collects one message per problem found.</param>
    internal void Validate(List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(Operator))
        {
            problems.Add(
                "A calculation needs an Operator, one of ObservabilityTelemetryCalculationOperators - " +
                "'count', 'avg', 'p99' and the rest.");
        }
        else if (string.IsNullOrWhiteSpace(Key) &&
                 !string.Equals(
                     Operator,
                     ObservabilityTelemetryCalculationOperators.Count,
                     StringComparison.OrdinalIgnoreCase))
        {
            problems.Add(
                $"A '{Operator}' calculation needs a Key: only a plain count has nothing to count over.");
        }

        if (KeyType is not null &&
            KeyType != ObservabilityTelemetryValueTypes.String &&
            KeyType != ObservabilityTelemetryValueTypes.Number &&
            KeyType != ObservabilityTelemetryValueTypes.Boolean)
        {
            problems.Add(
                $"A calculation's KeyType must be '{ObservabilityTelemetryValueTypes.String}', " +
                $"'{ObservabilityTelemetryValueTypes.Number}' or " +
                $"'{ObservabilityTelemetryValueTypes.Boolean}', but was '{KeyType}'.");
        }
    }

    /// <summary>Operator, key and alias, on one line.</summary>
    /// <returns>The calculation described in one line.</returns>
    public override string ToString() =>
        Alias is null ? $"{Operator}({Key})" : $"{Operator}({Key}) as {Alias}";
}

/// <summary>One field a calculation's results are broken down by.</summary>
public sealed class ObservabilityTelemetryGroupBy
{
    /// <summary>
    /// The field to group by, named the way a filter names it - <c>$metadata.service</c>,
    /// <c>$metadata.statusCode</c>.
    /// </summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    /// <summary>What the field holds: one of <c>ObservabilityTelemetryValueTypes</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Builds a group-by over one field.</summary>
    /// <param name="value">The field to group by.</param>
    /// <param name="type">What the field holds, one of <c>ObservabilityTelemetryValueTypes</c>.</param>
    /// <returns>A group-by.</returns>
    public static ObservabilityTelemetryGroupBy On(string value, string type) =>
        new()
        {
            Value = value,
            Type = type,
        };

    /// <summary>
    /// Checks the group-by: a field, and one of the three types for it.
    /// </summary>
    /// <param name="problems">Collects one message per problem found.</param>
    internal void Validate(List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            problems.Add("A group-by needs a Value: the name of the field to group by.");
        }

        if (Type is null)
        {
            problems.Add(
                "A group-by needs a Type, one of ObservabilityTelemetryValueTypes, because the same field " +
                "can hold different kinds of value.");
        }
        else if (Type != ObservabilityTelemetryValueTypes.String &&
                 Type != ObservabilityTelemetryValueTypes.Number &&
                 Type != ObservabilityTelemetryValueTypes.Boolean)
        {
            problems.Add(
                $"A group-by's Type must be '{ObservabilityTelemetryValueTypes.String}', " +
                $"'{ObservabilityTelemetryValueTypes.Number}' or " +
                $"'{ObservabilityTelemetryValueTypes.Boolean}', but was '{Type}'.");
        }
    }

    /// <summary>Field and type, on one line.</summary>
    /// <returns>The group-by described in one line.</returns>
    public override string ToString() => $"{Value} ({Type})";
}

/// <summary>One threshold applied to calculation results after aggregating.</summary>
/// <remarks>
/// A having is what keeps the groups worth looking at - only the ones whose count is above 100, say. Its
/// <see cref="Key"/> names a calculation's alias, or its operator when it has no alias.
/// </remarks>
public sealed class ObservabilityTelemetryHaving
{
    /// <summary>The calculation to filter on: its alias, or its operator when it has no alias.</summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    /// <summary>How to compare: <c>eq</c>, <c>neq</c>, <c>gt</c>, <c>gte</c>, <c>lt</c> or <c>lte</c>.</summary>
    [JsonPropertyName("operation")]
    public string? Operation { get; set; }

    /// <summary>The threshold to compare the calculation's result against.</summary>
    [JsonPropertyName("value")]
    public double? Value { get; set; }

    /// <summary>Builds a threshold on one calculation.</summary>
    /// <param name="key">The calculation's alias, or its operator when it has no alias.</param>
    /// <param name="operation">How to compare, one of the six numeric spellings on <c>ObservabilityFilterOperations</c>.</param>
    /// <param name="value">The threshold.</param>
    /// <returns>A having.</returns>
    public static ObservabilityTelemetryHaving Of(string key, string operation, double value) =>
        new()
        {
            Key = key,
            Operation = operation,
            Value = value,
        };

    /// <summary>
    /// Checks the having: a key, one of the six comparisons, and a threshold.
    /// </summary>
    /// <param name="problems">Collects one message per problem found.</param>
    internal void Validate(List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            problems.Add("A having needs a Key: the alias or operator of the calculation to filter on.");
        }

        if (string.IsNullOrWhiteSpace(Operation))
        {
            problems.Add(
                "A having needs an Operation: 'eq', 'neq', 'gt', 'gte', 'lt' or 'lte', which are the six " +
                "numeric comparisons it accepts.");
        }
        else if (Operation != ObservabilityFilterOperations.Equal &&
                 Operation != ObservabilityFilterOperations.NotEqual &&
                 Operation != ObservabilityFilterOperations.GreaterThan &&
                 Operation != ObservabilityFilterOperations.GreaterThanOrEqual &&
                 Operation != ObservabilityFilterOperations.LessThan &&
                 Operation != ObservabilityFilterOperations.LessThanOrEqual)
        {
            problems.Add(
                $"A having's Operation must be one of 'eq', 'neq', 'gt', 'gte', 'lt' or 'lte', but was " +
                $"'{Operation}'. Unlike a filter's, this one is a closed set.");
        }

        if (Value is null)
        {
            problems.Add("A having needs a Value: the threshold to compare against.");
        }
    }

    /// <summary>Key, comparison and threshold, on one line.</summary>
    /// <returns>The having described in one line.</returns>
    public override string ToString() =>
        $"{Key} {Operation} {Value?.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>How grouped calculation results are ordered.</summary>
/// <remarks>
/// The value names a calculation - its alias, or its operator when it has no alias - so ordering only takes
/// effect once a query groups by something.
/// </remarks>
public sealed class ObservabilityTelemetryOrderBy
{
    /// <summary>The calculation to order by: its alias, or its operator when it has no alias.</summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    /// <summary>Which way round: one of <c>ObservabilityTelemetrySortOrders</c>.</summary>
    [JsonPropertyName("order")]
    public string? Order { get; set; }

    /// <summary>Builds an ordering over one calculation.</summary>
    /// <param name="value">The calculation's alias, or its operator when it has no alias.</param>
    /// <param name="order">Which way round, or null for Cloudflare's default.</param>
    /// <returns>An ordering.</returns>
    public static ObservabilityTelemetryOrderBy On(string value, string? order = null) =>
        new()
        {
            Value = value,
            Order = order,
        };

    /// <summary>Builds an ordering that puts the largest results first.</summary>
    /// <param name="value">The calculation's alias, or its operator when it has no alias.</param>
    /// <returns>A descending ordering.</returns>
    public static ObservabilityTelemetryOrderBy Descending(string value) =>
        new()
        {
            Value = value,
            Order = ObservabilityTelemetrySortOrders.Descending,
        };

    /// <summary>
    /// Checks the ordering: something to order by, and a direction this endpoint spells.
    /// </summary>
    /// <param name="problems">Collects one message per problem found.</param>
    internal void Validate(List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            problems.Add("An OrderBy needs a Value: the alias or operator of the calculation to order by.");
        }

        if (Order is not null && !ObservabilityTelemetrySortOrders.All.Contains(Order))
        {
            problems.Add(
                $"An OrderBy's Order must be '{ObservabilityTelemetrySortOrders.Ascending}' or " +
                $"'{ObservabilityTelemetrySortOrders.Descending}', but was '{Order}'.");
        }
    }

    /// <summary>Calculation and direction, on one line.</summary>
    /// <returns>The ordering described in one line.</returns>
    public override string ToString() => $"{Value} {Order}";
}

/// <summary>The views a telemetry query can ask for, each of which fills a different part of the answer.</summary>
/// <remarks>
/// <c>events</c> answers the matching log lines, <c>calculations</c> aggregated metrics, <c>invocations</c>
/// those same events grouped by request id, <c>traces</c> distributed trace summaries and <c>agents</c> the
/// agent runs among them. The schema lists a sixth, <c>requests</c>, which its own description of the field
/// does not explain; it is published here rather than refused, and an account that gets an answer for it
/// reads that answer through the same result type.
/// </remarks>
public static class ObservabilityTelemetryViews
{
    /// <summary>Individual events: the matching log lines, spans and metric points.</summary>
    public const string Events = "events";

    /// <summary>Aggregated metrics, with optional group-by breakdowns and a time series.</summary>
    public const string Calculations = "calculations";

    /// <summary>Events grouped by the request id of the invocation they belong to.</summary>
    public const string Invocations = "invocations";

    /// <summary>Summaries of the matching distributed traces.</summary>
    public const string Traces = "traces";

    /// <summary>Summaries of the traces that contain at least one agent invocation.</summary>
    public const string Agents = "agents";

    /// <summary>A sixth view the schema lists without describing.</summary>
    public const string Requests = "requests";

    /// <summary>Every view this library knows.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Events,
        Calculations,
        Invocations,
        Traces,
        Agents,
        Requests,
    };
}

/// <summary>What a query on the <c>calculations</c> view computes, which is also what shape its answer takes.</summary>
/// <remarks>
/// Left out, or <c>timeseries_and_aggregate</c>, is what Cloudflare has always done: both the time series and
/// the aggregate. <c>distribution</c> answers a bucketed histogram in <c>distribution</c> instead of
/// <c>calculations</c>, and is not compatible with comparing - the pair is refused with a 400, which
/// <c>ObservabilityTelemetryQueryRequest.EnsureValid</c> catches locally.
/// </remarks>
public static class ObservabilityTelemetryChartTypes
{
    /// <summary>Both the time series and the aggregate, which is the default.</summary>
    public const string TimeseriesAndAggregate = "timeseries_and_aggregate";

    /// <summary>The time series only.</summary>
    public const string Timeseries = "timeseries";

    /// <summary>The aggregate only.</summary>
    public const string Aggregate = "aggregate";

    /// <summary>A bucketed two-dimensional histogram: value buckets across time bins.</summary>
    public const string Distribution = "distribution";

    /// <summary>Every chart type the schema lists.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        TimeseriesAndAggregate,
        Timeseries,
        Aggregate,
        Distribution,
    };
}

/// <summary>How the value axis of a distribution is bucketed.</summary>
/// <remarks>
/// Logarithmic buckets are geometric and suit heavy-tailed latencies; linear ones are fixed width and read
/// more clearly over a narrow or additive range. The scale is ignored for any other chart type, and the
/// answer echoes the one used in <c>distribution.bucketMode</c>.
/// </remarks>
public static class ObservabilityTelemetryDistributionScales
{
    /// <summary>Geometric buckets, which is the default and suits heavy-tailed values.</summary>
    public const string Log = "log";

    /// <summary>Fixed-width buckets, clearer over a narrow range.</summary>
    public const string Linear = "linear";

    /// <summary>Every scale the schema lists.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Log, Linear };
}

/// <summary>How far a query run got.</summary>
public static class ObservabilityTelemetryRunStatuses
{
    /// <summary>The run has begun and has not finished.</summary>
    public const string Started = "STARTED";

    /// <summary>The run finished, and its results are the ones in the answer.</summary>
    public const string Completed = "COMPLETED";

    /// <summary>Every status the schema lists, spelled as Cloudflare spells them.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Started, Completed };
}

/// <summary>The directions an ordering or a page walk can go in.</summary>
public static class ObservabilityTelemetrySortOrders
{
    /// <summary>Smallest first.</summary>
    public const string Ascending = "asc";

    /// <summary>Largest first.</summary>
    public const string Descending = "desc";

    /// <summary>Every direction the schema lists.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Ascending, Descending };
}

/// <summary>The directions a paged query can walk in.</summary>
/// <remarks>
/// The schema types <c>offsetDirection</c> as an open string rather than as these two, so a request is not
/// validated against them and any other spelling is passed through.
/// </remarks>
public static class ObservabilityTelemetryOffsetDirections
{
    /// <summary>Forward, which is the direction a first page walks in.</summary>
    public const string Next = "next";

    /// <summary>Backward.</summary>
    public const string Previous = "prev";
}

/// <summary>The operators a calculation accepts.</summary>
/// <remarks>
/// The published schema spells all of these, in three families: the lowercase words, the uppercase ones the
/// dashboard grew, and the percentiles in both cases. <c>uniq</c> and <c>COUNT_DISTINCT</c> are the same
/// count of distinct values in the two cases. An operator is not validated against this list, for the reason
/// a filter's operation is not: refusing one Cloudflare accepts would be worse than passing it through.
/// </remarks>
public static class ObservabilityTelemetryCalculationOperators
{
    /// <summary>How many events matched, which is the only operator that needs no key.</summary>
    public const string Count = "count";

    /// <summary>The uppercase spelling of a count.</summary>
    public const string CountUpper = "COUNT";

    /// <summary>How many distinct values a key took.</summary>
    public const string CountDistinct = "COUNT_DISTINCT";

    /// <summary>How many distinct values a key took, lowercase.</summary>
    public const string Unique = "uniq";

    /// <summary>The largest value.</summary>
    public const string Max = "max";

    /// <summary>The uppercase spelling of the largest value.</summary>
    public const string MaxUpper = "MAX";

    /// <summary>The smallest value.</summary>
    public const string Min = "min";

    /// <summary>The uppercase spelling of the smallest value.</summary>
    public const string MinUpper = "MIN";

    /// <summary>The total of the values.</summary>
    public const string Sum = "sum";

    /// <summary>The uppercase spelling of the total.</summary>
    public const string SumUpper = "SUM";

    /// <summary>The mean of the values.</summary>
    public const string Average = "avg";

    /// <summary>The uppercase spelling of the mean.</summary>
    public const string AverageUpper = "AVG";

    /// <summary>The middle value.</summary>
    public const string Median = "median";

    /// <summary>The uppercase spelling of the middle value.</summary>
    public const string MedianUpper = "MEDIAN";

    /// <summary>The 0.1th percentile.</summary>
    public const string P001 = "p001";

    /// <summary>The uppercase spelling of the 0.1th percentile.</summary>
    public const string P001Upper = "P001";

    /// <summary>The 1st percentile.</summary>
    public const string P01 = "p01";

    /// <summary>The uppercase spelling of the 1st percentile.</summary>
    public const string P01Upper = "P01";

    /// <summary>The 5th percentile.</summary>
    public const string P05 = "p05";

    /// <summary>The uppercase spelling of the 5th percentile.</summary>
    public const string P05Upper = "P05";

    /// <summary>The 10th percentile.</summary>
    public const string P10 = "p10";

    /// <summary>The uppercase spelling of the 10th percentile.</summary>
    public const string P10Upper = "P10";

    /// <summary>The 25th percentile.</summary>
    public const string P25 = "p25";

    /// <summary>The uppercase spelling of the 25th percentile.</summary>
    public const string P25Upper = "P25";

    /// <summary>The 75th percentile.</summary>
    public const string P75 = "p75";

    /// <summary>The uppercase spelling of the 75th percentile.</summary>
    public const string P75Upper = "P75";

    /// <summary>The 90th percentile.</summary>
    public const string P90 = "p90";

    /// <summary>The uppercase spelling of the 90th percentile.</summary>
    public const string P90Upper = "P90";

    /// <summary>The 95th percentile.</summary>
    public const string P95 = "p95";

    /// <summary>The uppercase spelling of the 95th percentile.</summary>
    public const string P95Upper = "P95";

    /// <summary>The 99th percentile, which is the latency number to watch.</summary>
    public const string P99 = "p99";

    /// <summary>The uppercase spelling of the 99th percentile.</summary>
    public const string P99Upper = "P99";

    /// <summary>The 99.9th percentile.</summary>
    public const string P999 = "p999";

    /// <summary>The uppercase spelling of the 99.9th percentile.</summary>
    public const string P999Upper = "P999";

    /// <summary>The standard deviation of the values.</summary>
    public const string StandardDeviation = "stddev";

    /// <summary>The uppercase spelling of the standard deviation.</summary>
    public const string StandardDeviationUpper = "STDDEV";

    /// <summary>The variance of the values.</summary>
    public const string Variance = "variance";

    /// <summary>The uppercase spelling of the variance.</summary>
    public const string VarianceUpper = "VARIANCE";
}
