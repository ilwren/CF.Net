using System.Text.Json;
using CFNet.Exceptions;
using CFNet.Models.Workers;
using CFNet.Serialization;
using CFNet.UnitTests.Testing;
using Xunit;

namespace CFNet.UnitTests.Services;

/// <summary>
/// The telemetry query endpoint: one POST whose answer's shape moves with the view it was asked for. The
/// parts worth pinning are the request it sends (camelCase, with the filter tree's <c>kind</c>
/// discriminator), what it refuses locally rather than letting Cloudflare answer 400, and that each of the
/// six views reads back - including the echoed query, whose filters can arrive without the discriminator the
/// request always writes.
/// </summary>
public class WorkersObservabilityTelemetryQueryTests
{
    private const string RunAndStatistics =
        """{"run":{"id":"run-1","status":"COMPLETED","timeframe":{"from":1,"to":2}},"statistics":{"bytes_read":0,"elapsed":0,"rows_read":0}""";

    [Fact]
    public async Task QueryTelemetryAsync_posts_the_query_body_to_the_telemetry_path()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            RunAndStatistics +
            ""","events":{"count":0,"events":[]}}""");

        var result = await harness.Client.WorkersObservability.QueryTelemetryAsync(
            new ObservabilityTelemetryQueryRequest
            {
                QueryId = "q-1",
                Timeframe = ObservabilityTelemetryTimeframe.Between(1793857298985, 1793943698985),
            });

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/workers/observability/telemetry/query",
            harness.LastRequest.Path,
            StringComparison.Ordinal);

        // Only what was set is sent: no view means Cloudflare's own default, and no parameters means a query
        // it already holds under that id.
        Assert.Equal(
            """{"queryId":"q-1","timeframe":{"from":1793857298985,"to":1793943698985}}""",
            harness.LastRequest.Body);

        Assert.True(result.IsComplete);
        Assert.Equal("run-1", result.Run!.Id);
        Assert.Equal(ObservabilityTelemetryRunStatuses.Completed, result.Run.Status);
        Assert.Empty(result.AllEvents);
    }

    [Fact]
    public async Task An_ad_hoc_query_names_itself_and_counts_its_span_in_milliseconds()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(RunAndStatistics + "}");

        var from = new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);
        var to = from.AddHours(1);

        await harness.Client.WorkersObservability.QueryTelemetryAsync(
            ObservabilityTelemetryQueryRequest.OverSpan(ObservabilityTelemetryViews.Events, from, to));

        var sent = JsonSerializer.Deserialize<ObservabilityTelemetryQueryRequest>(
            harness.LastRequest.Body!,
            CloudflareJson.Default)!;

        // The id is generated, recognisable as this library's, and different each time.
        Assert.StartsWith(
            ObservabilityTelemetryQueryRequest.AdHocQueryIdPrefix,
            sent.QueryId,
            StringComparison.Ordinal);
        Assert.Equal(ObservabilityTelemetryViews.Events, sent.View);

        // The dates went out as the epoch milliseconds the endpoint's own schema asks for.
        Assert.Equal(1793955600000, sent.Timeframe!.From);
        Assert.Equal(1793959200000, sent.Timeframe.To);
        Assert.Equal(from, sent.Timeframe.FromUtc);
        Assert.Equal(to, sent.Timeframe.ToUtc);
    }

    [Fact]
    public async Task A_query_sends_its_filters_calculations_and_group_bys_in_camel_case()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(RunAndStatistics + "}");

        await harness.Client.WorkersObservability.QueryTelemetryAsync(
            new ObservabilityTelemetryQueryRequest
            {
                QueryId = "cfnet-saved-1",
                Timeframe = ObservabilityTelemetryTimeframe.Between(1793857298985, 1793943698985),
                View = ObservabilityTelemetryViews.Calculations,
                Chart = true,
                Granularity = 12,
                Limit = 10,
                Parameters = new ObservabilityTelemetryQueryParameters
                {
                    Datasets = new List<string> { "cloudflare-workers" },
                    FilterCombination = ObservabilityFilterCombinations.And,
                    Filters = new List<ObservabilityTelemetryFilterNode>
                    {
                        ObservabilityTelemetryFilter.Of(
                            "$metadata.service",
                            ObservabilityFilterOperations.Equal,
                            ObservabilityTelemetryValueTypes.String,
                            "cfnet-demo"),
                    },
                    Calculations = new List<ObservabilityTelemetryCalculation>
                    {
                        ObservabilityTelemetryCalculation.Count("hits"),
                        ObservabilityTelemetryCalculation.Of(
                            "$metadata.duration",
                            ObservabilityTelemetryCalculationOperators.P99,
                            "slowest",
                            ObservabilityTelemetryValueTypes.Number),
                    },
                    GroupBys = new List<ObservabilityTelemetryGroupBy>
                    {
                        ObservabilityTelemetryGroupBy.On(
                            "$metadata.service",
                            ObservabilityTelemetryValueTypes.String),
                    },
                    Havings = new List<ObservabilityTelemetryHaving>
                    {
                        ObservabilityTelemetryHaving.Of(
                            "hits",
                            ObservabilityFilterOperations.GreaterThan,
                            100),
                    },
                    Limit = 10,
                    Needle = ObservabilityTelemetryNeedle.Literal("boom"),
                    OrderBy = ObservabilityTelemetryOrderBy.Descending("hits"),
                },
            });

        // Declaration order is the wire order, the discriminator on a filter leaf is written by the
        // serializer, and a count carries no key because it has nothing to count over.
        Assert.Equal(
            """{"queryId":"cfnet-saved-1","timeframe":{"from":1793857298985,"to":1793943698985},"view":"calculations","parameters":{"calculations":[{"operator":"count","alias":"hits"},{"operator":"p99","key":"$metadata.duration","alias":"slowest","keyType":"number"}],"datasets":["cloudflare-workers"],"filterCombination":"and","filters":[{"kind":"filter","key":"$metadata.service","operation":"eq","type":"string","value":"cfnet-demo"}],"groupBys":[{"value":"$metadata.service","type":"string"}],"havings":[{"key":"hits","operation":"gt","value":100}],"limit":10,"needle":{"value":"boom"},"orderBy":{"value":"hits","order":"desc"}},"chart":true,"granularity":12,"limit":10}""",
            harness.LastRequest.Body);
    }

    [Fact]
    public async Task The_chart_paging_and_preview_fields_are_sent_only_when_set()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(RunAndStatistics + "}");

        await harness.Client.WorkersObservability.QueryTelemetryAsync(
            new ObservabilityTelemetryQueryRequest
            {
                QueryId = "q-2",
                Timeframe = ObservabilityTelemetryTimeframe.Between(1, 2),
                View = ObservabilityTelemetryViews.Events,
                Chart = true,
                ChartType = ObservabilityTelemetryChartTypes.Timeseries,
                Compare = false,
                DistributionScale = ObservabilityTelemetryDistributionScales.Linear,
                Dry = true,
                Granularity = 30,
                IgnoreSeries = false,
                Limit = 25,
                Offset = "evt-99",
                OffsetBy = 50,
                OffsetDirection = ObservabilityTelemetryOffsetDirections.Next,
            });

        // A false that was set is a false that goes out: only nulls are dropped.
        Assert.Equal(
            """{"queryId":"q-2","timeframe":{"from":1,"to":2},"view":"events","chart":true,"chartType":"timeseries","compare":false,"distributionScale":"linear","dry":true,"granularity":30,"ignoreSeries":false,"limit":25,"offset":"evt-99","offsetBy":50,"offsetDirection":"next"}""",
            harness.LastRequest.Body);
    }

    [Fact]
    public async Task QueryTelemetryAsync_refuses_a_request_missing_what_the_schema_requires()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.WorkersObservability.QueryTelemetryAsync(
                new ObservabilityTelemetryQueryRequest()));

        Assert.Equal(2, exception.Problems.Count);
        Assert.Contains("QueryId", exception.Problems[0], StringComparison.Ordinal);
        Assert.Contains("Timeframe", exception.Problems[1], StringComparison.Ordinal);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => harness.Client.WorkersObservability.QueryTelemetryAsync(null!));

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task A_query_span_that_ends_before_it_starts_is_refused()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.WorkersObservability.QueryTelemetryAsync(
                new ObservabilityTelemetryQueryRequest
                {
                    QueryId = "q-3",
                    Timeframe = ObservabilityTelemetryTimeframe.Between(2000, 1000),
                }));

        Assert.Contains("empty span", Assert.Single(exception.Problems), StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task A_query_that_gets_everything_wrong_is_refused_field_by_field()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.WorkersObservability.QueryTelemetryAsync(
                new ObservabilityTelemetryQueryRequest
                {
                    View = "logs",
                    ChartType = "histogram",
                    DistributionScale = "exponential",
                    Granularity = 0,
                    Limit = 0,
                    OffsetBy = -1,
                    Parameters = new ObservabilityTelemetryQueryParameters
                    {
                        FilterCombination = "xor",
                        Limit = 0,
                        Needle = new ObservabilityTelemetryNeedle(),
                        Calculations = new List<ObservabilityTelemetryCalculation>
                        {
                            new(),
                        },
                        GroupBys = new List<ObservabilityTelemetryGroupBy>
                        {
                            new(),
                        },
                        Havings = new List<ObservabilityTelemetryHaving>
                        {
                            new(),
                        },
                        OrderBy = new ObservabilityTelemetryOrderBy(),
                    },
                }));

        // Everything the request itself gets wrong, then everything its parameters get wrong, in the order
        // they are checked: a caller fixing one message should not have to guess which field it names.
        Assert.Equal(18, exception.Problems.Count);
        Assert.Contains("QueryId", exception.Problems[0], StringComparison.Ordinal);
        Assert.Contains("Timeframe", exception.Problems[1], StringComparison.Ordinal);
        Assert.Contains("View", exception.Problems[2], StringComparison.Ordinal);
        Assert.Contains("ChartType", exception.Problems[3], StringComparison.Ordinal);
        Assert.Contains("DistributionScale", exception.Problems[4], StringComparison.Ordinal);
        Assert.Contains("Granularity", exception.Problems[5], StringComparison.Ordinal);
        Assert.Contains("Limit", exception.Problems[6], StringComparison.Ordinal);
        Assert.Contains("OffsetBy", exception.Problems[7], StringComparison.Ordinal);
        Assert.Contains("FilterCombination", exception.Problems[8], StringComparison.Ordinal);
        Assert.Contains("Needle", exception.Problems[10], StringComparison.Ordinal);
        Assert.Contains("Operator", exception.Problems[11], StringComparison.Ordinal);
        Assert.Contains("group-by", exception.Problems[12], StringComparison.Ordinal);
        Assert.Contains("having", exception.Problems[14], StringComparison.Ordinal);
        Assert.Contains("OrderBy", exception.Problems[17], StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task A_distribution_chart_that_also_compares_is_refused_before_it_is_sent()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.WorkersObservability.QueryTelemetryAsync(
                new ObservabilityTelemetryQueryRequest
                {
                    QueryId = "q-4",
                    Timeframe = ObservabilityTelemetryTimeframe.Between(1, 2),
                    View = ObservabilityTelemetryViews.Calculations,
                    ChartType = ObservabilityTelemetryChartTypes.Distribution,
                    Compare = true,
                }));

        // The published schema says this pair is answered with a 400, so it is refused here instead.
        var problem = Assert.Single(exception.Problems);
        Assert.Contains("400", problem, StringComparison.Ordinal);
        Assert.Contains("Compare", problem, StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task A_keyed_calculation_needs_a_key_and_a_count_does_not()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(RunAndStatistics + "}");

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.WorkersObservability.QueryTelemetryAsync(
                new ObservabilityTelemetryQueryRequest
                {
                    QueryId = "q-5",
                    Timeframe = ObservabilityTelemetryTimeframe.Between(1, 2),
                    Parameters = new ObservabilityTelemetryQueryParameters
                    {
                        Calculations = new List<ObservabilityTelemetryCalculation>
                        {
                            ObservabilityTelemetryCalculation.Count(),
                            new ObservabilityTelemetryCalculation
                            {
                                Operator = ObservabilityTelemetryCalculationOperators.P95,
                            },
                            ObservabilityTelemetryCalculation.Of(
                                "$metadata.duration",
                                ObservabilityTelemetryCalculationOperators.Average,
                                keyType: "milliseconds"),
                        },
                    },
                }));

        // The count is fine, the percentile with no key is not, and neither is a key type outside the three.
        Assert.Equal(2, exception.Problems.Count);
        Assert.Contains("p95", exception.Problems[0], StringComparison.Ordinal);
        Assert.Contains("KeyType", exception.Problems[1], StringComparison.Ordinal);

        // A count on its own is a whole query, and the operator spellings are not policed.
        await harness.Client.WorkersObservability.QueryTelemetryAsync(
            new ObservabilityTelemetryQueryRequest
            {
                QueryId = "q-6",
                Timeframe = ObservabilityTelemetryTimeframe.Between(1, 2),
                Parameters = new ObservabilityTelemetryQueryParameters
                {
                    Calculations = new List<ObservabilityTelemetryCalculation>
                    {
                        ObservabilityTelemetryCalculation.Of(
                            "$metadata.duration",
                            ObservabilityTelemetryCalculationOperators.P99Upper),
                    },
                },
            });

        Assert.Contains(
            """{"operator":"P99","key":"$metadata.duration"}""",
            harness.LastRequest.Body,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Filters_that_nest_deeper_than_the_schema_allows_are_refused()
    {
        using var harness = CloudflareHarness.Create();

        ObservabilityTelemetryFilterNode tree = ObservabilityTelemetryFilter.Of(
            "$metadata.service",
            ObservabilityFilterOperations.Equal,
            ObservabilityTelemetryValueTypes.String,
            "cfnet-demo");
        for (var depth = 0; depth <= ObservabilityTelemetryFilterNode.MaximumNestingDepth; depth++)
        {
            tree = ObservabilityTelemetryFilterGroup.And(tree);
        }

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.WorkersObservability.QueryTelemetryAsync(
                new ObservabilityTelemetryQueryRequest
                {
                    QueryId = "q-7",
                    Timeframe = ObservabilityTelemetryTimeframe.Between(1, 2),
                    Parameters = new ObservabilityTelemetryQueryParameters
                    {
                        Filters = new List<ObservabilityTelemetryFilterNode> { tree },
                    },
                }));

        // The same cap the other telemetry endpoints carry: four nested groups, not five.
        Assert.Contains("nest", Assert.Single(exception.Problems), StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task An_events_answer_reads_its_metadata_its_workers_context_and_its_source()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """{"run":{"id":"run-e","accountId":"acct-1234","dry":true,"granularity":60,"status":"COMPLETED","timeframe":{"from":1793955600000,"to":1793959200000},"userId":"u-1","created":"2026-11-06T09:00:00Z","statistics":{"bytes_read":2048,"elapsed":0.043,"rows_read":2},"query":{"id":"cfnet-q","adhoc":true,"created":"2026-11-06T09:00:00Z","createdBy":"u-1","description":null,"name":"cfnet-q","parameters":{"datasets":["cloudflare-workers"],"filters":[{"key":"$metadata.service","operation":"eq","type":"string","value":"cfnet-demo"}],"limit":10},"updated":"2026-11-06T09:00:00Z","updatedBy":"u-1"}},"statistics":{"bytes_read":1048576,"elapsed":0.043,"rows_read":1200,"abr_level":1},"events":{"count":2,"events":[{"$metadata":{"id":"evt-1","account":"acct-1234","service":"cfnet-demo","level":"error","message":"boom","statusCode":500,"duration":12.5,"timestampNs":"1793955600000000000","spanStatus":"error","spanId":"span-1","traceId":"trace-1","requestId":"req-1","rayId":"ray-1","region":"LAX","type":"cf-worker-log"},"dataset":"cloudflare-workers","source":"boom: it broke","timestamp":1793955600000,"$workers":{"eventType":"fetch","scriptName":"cfnet-demo","outcome":"ok","cpuTimeMs":1.5,"wallTimeMs":12.5,"executionModel":"stateless","requestId":"req-1","scriptVersion":{"id":"v-1","message":"first","tag":"latest"},"truncated":false,"diagnosticsChannelEvents":[{"channel":"worker:durable_object","message":"attached","timestamp":1793955600000}]}},{"$metadata":{"id":"evt-2","service":"cfnet-demo","level":"info","message":"handled"},"dataset":"cloudflare-workers","source":{"msg":"handled","n":2},"timestamp":1793955601000,"$containers":{"containerId":"c-1","image":"cfnet:latest"}}],"fields":[{"key":"$metadata.message","type":"string"},{"key":"$metadata.statusCode","type":"number"}],"series":[{"time":"2026-11-06T09:00:00Z","data":[{"aggregates":{"_count":2,"_interval":1,"_firstSeen":"2026-11-06T09:00:00Z","_lastSeen":"2026-11-06T09:00:01Z"},"count":2,"interval":1,"sampleInterval":1,"errors":1,"groups":{"$metadata.service":"cfnet-demo"}}]}]}}""");

        var result = await harness.Client.WorkersObservability.QueryTelemetryAsync(
            ObservabilityTelemetryQueryRequest.AdHoc(
                ObservabilityTelemetryViews.Events,
                ObservabilityTelemetryTimeframe.Between(1793955600000, 1793959200000)));

        // The run says what executed, and the span reads back as the dates it was sent as.
        Assert.True(result.IsComplete);
        Assert.True(result.Run!.Dry);
        Assert.Equal(60, result.Run.Granularity);
        Assert.Equal(new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero), result.Run.StartedAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 6, 10, 0, 0, TimeSpan.Zero), result.Run.EndedAt);

        // The snake_case statistics, which are the only fields in the answer Cloudflare spells that way.
        Assert.Equal(1048576, result.Statistics!.BytesRead);
        Assert.Equal(0.043, result.Statistics.Elapsed);
        Assert.Equal(1200, result.Statistics.RowsRead);
        Assert.Equal(1, result.Statistics.AbrLevel);
        Assert.Equal(2048, result.Run.Statistics!.BytesRead);

        var events = result.Events!;
        Assert.Equal(2, events.Count);
        Assert.Equal(2, result.AllEvents.Count);
        Assert.Equal("2 of 2", events.ToString());

        var first = result.AllEvents[0];
        Assert.Equal("evt-1", first.Metadata!.Id);
        Assert.Equal("error", first.Metadata.Level);
        Assert.Equal("boom", first.Metadata.Message);
        Assert.Equal(500, first.Metadata.StatusCode);
        Assert.Equal(12.5, first.Metadata.Duration);
        Assert.Equal("1793955600000000000", first.Metadata.TimestampNs);
        Assert.Equal("cf-worker-log", first.Metadata.Type);
        Assert.Equal(new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero), first.TimestampUtc);

        // A source emitted as a string reads back as that string, unquoted.
        Assert.Equal(JsonValueKind.String, first.Source!.Value.ValueKind);
        Assert.Equal("boom: it broke", first.SourceText);

        var workers = first.Workers!;
        Assert.Equal("fetch", workers.EventType);
        Assert.Equal("cfnet-demo", workers.ScriptName);
        Assert.Equal(1.5, workers.CpuTimeMs);
        Assert.Equal("stateless", workers.ExecutionModel);
        Assert.Equal("latest", workers.ScriptVersion!.Tag);
        Assert.False(workers.Truncated);
        Assert.Equal("worker:durable_object", Assert.Single(workers.DiagnosticsChannelEvents!).Channel);
        Assert.Equal("cfnet-demo fetch ok", workers.ToString());

        // A source emitted as an object keeps its JSON, and the containers dictionary keeps its keys.
        var second = result.AllEvents[1];
        Assert.Equal(JsonValueKind.Object, second.Source!.Value.ValueKind);
        Assert.Equal("""{"msg":"handled","n":2}""", second.SourceText);
        Assert.Equal(2, second.Containers!.Count);
        Assert.Equal("c-1", second.Containers["containerId"].GetString());

        // The fields a dynamic view would be built from, and the series behind the buckets.
        Assert.Equal("$metadata.message", events.Fields![0].Key);
        Assert.Equal(ObservabilityTelemetryValueTypes.Number, events.Fields[1].Type);

        var series = Assert.Single(events.Series!);
        Assert.Equal(new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero), series.TimeUtc);
        var point = Assert.Single(series.Data!);
        Assert.Equal(2d, point.Count);
        Assert.Equal(1d, point.Errors);
        Assert.Equal(2d, point.Aggregates!.Count);
        Assert.Equal("2026-11-06T09:00:01Z", point.Aggregates.LastSeen);
        Assert.Equal("cfnet-demo", point.Groups!["$metadata.service"].GetString());
    }

    [Fact]
    public async Task The_echoed_query_keeps_its_filters_as_the_json_cloudflare_stored()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """{"run":{"id":"run-q","status":"COMPLETED","timeframe":{"from":1,"to":2},"query":{"id":"cfnet-q","adhoc":true,"parameters":{"filterCombination":"and","filters":[{"key":"$metadata.level","operation":"eq","type":"string","value":"error"},{"filterCombination":"or","filters":[{"key":"$metadata.service","operation":"includes","type":"string","value":"cfnet"}],"kind":"group"}],"calculations":[{"operator":"count","alias":"hits"}],"needle":{"value":"boom","isRegex":false},"groupBys":[{"value":"$metadata.service","type":"string"}],"havings":[{"key":"hits","operation":"gt","value":100}],"orderBy":{"value":"hits","order":"desc"},"limit":10}}},"statistics":{"bytes_read":1,"elapsed":0.1,"rows_read":1}}""");

        var result = await harness.Client.WorkersObservability.QueryTelemetryAsync(
            ObservabilityTelemetryQueryRequest.AdHoc(
                ObservabilityTelemetryViews.Calculations,
                ObservabilityTelemetryTimeframe.Between(1, 2)));

        var parameters = result.Run!.Query!.Parameters!;

        // A leaf the echo carries with no "kind" at all - which the request would never send, and which
        // reading into the polymorphic filter tree would fail on.
        Assert.Equal(2, parameters.Filters!.Count);
        var leaf = parameters.Filters[0];
        Assert.Equal("$metadata.level", leaf.GetProperty("key").GetString());
        Assert.False(leaf.TryGetProperty("kind", out _));

        var group = parameters.Filters[1];
        Assert.Equal("group", group.GetProperty("kind").GetString());
        Assert.Equal("or", group.GetProperty("filterCombination").GetString());

        // Everything else in the echo shares the request's own types.
        Assert.Equal(ObservabilityFilterCombinations.And, parameters.FilterCombination);
        var calculation = Assert.Single(parameters.Calculations!);
        Assert.Equal(ObservabilityTelemetryCalculationOperators.Count, calculation.Operator);
        Assert.Equal("hits", calculation.Alias);
        Assert.Equal("boom", Assert.IsType<JsonElement>(parameters.Needle!.Value).GetString());
        Assert.False(parameters.Needle.IsRegex);
        Assert.Equal("$metadata.service", Assert.Single(parameters.GroupBys!).Value);
        Assert.Equal(100d, Assert.Single(parameters.Havings!).Value);
        Assert.Equal(ObservabilityTelemetrySortOrders.Descending, parameters.OrderBy!.Order);
        Assert.Equal(10, parameters.Limit);
    }

    [Fact]
    public async Task A_calculations_answer_reads_its_aggregates_its_groups_and_its_series()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """{"run":{"id":"run-c","status":"COMPLETED","timeframe":{"from":1,"to":2}},"statistics":{"bytes_read":10,"elapsed":0.5,"rows_read":3},"calculations":[{"calculation":"hits","alias":"hits","aggregates":[{"count":120,"interval":1,"sampleInterval":1,"value":120},{"count":28,"interval":4,"sampleInterval":4,"value":28,"groups":[{"key":"$metadata.service","value":"cfnet-demo"}]},{"count":7,"interval":1,"sampleInterval":1,"value":7,"groups":[{"key":"$metadata.statusCode","value":500}]}],"series":[{"time":"2026-11-06T09:00:00Z","data":[{"count":60,"interval":1,"sampleInterval":1,"value":60,"firstSeen":"2026-11-06T09:00:00Z","lastSeen":"2026-11-06T09:00:30Z","groups":[{"key":"$metadata.service","value":"cfnet-demo"}]}]},{"time":"2026-11-06T09:01:00Z","data":[{"count":60,"interval":1,"sampleInterval":1,"value":60}]}]},{"calculation":"p99","aggregates":[{"count":120,"interval":1,"sampleInterval":1,"value":480.5}],"series":[]}],"compare":[{"calculation":"hits","aggregates":[{"count":90,"interval":1,"sampleInterval":1,"value":90}],"series":[]}]}""");

        var result = await harness.Client.WorkersObservability.QueryTelemetryAsync(
            ObservabilityTelemetryQueryRequest.AdHoc(
                ObservabilityTelemetryViews.Calculations,
                ObservabilityTelemetryTimeframe.Between(1, 2)));

        Assert.Equal(2, result.AllCalculations.Count);

        var hits = result.AllCalculations[0];
        Assert.Equal("hits", hits.Alias);
        Assert.Equal("hits=120", hits.ToString());
        Assert.Equal(120d, hits.Total!.Value);
        Assert.False(hits.Total.IsSampled);
        Assert.Equal(3, hits.Aggregates!.Count);

        // A sampled group says so, because its numbers are estimates rather than counts.
        var sampled = hits.Aggregates[1];
        Assert.True(sampled.IsSampled);
        Assert.Equal(4d, sampled.SampleInterval);
        Assert.Equal("cfnet-demo", Assert.Single(sampled.Groups!).ValueText);

        // A group's value keeps the kind it was sent as.
        var statusGroup = Assert.Single(hits.Aggregates[2].Groups!);
        Assert.Equal("$metadata.statusCode", statusGroup.Key);
        Assert.Equal(JsonValueKind.Number, statusGroup.Value!.Value.ValueKind);
        Assert.Equal("500", statusGroup.ValueText);

        Assert.Equal(2, hits.Series!.Count);
        var bucket = hits.Series[0];
        Assert.Equal(new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero), bucket.TimeUtc);
        var point = Assert.Single(bucket.Data!);
        Assert.Equal(60d, point.Value);
        Assert.Equal("2026-11-06T09:00:00Z", point.FirstSeen);
        Assert.Equal("2026-11-06T09:00:30Z", point.LastSeen);
        Assert.Equal("cfnet-demo", Assert.Single(point.Groups!).ValueText);

        // The second calculation, and the comparison period in the same shape.
        Assert.Equal(480.5, result.AllCalculations[1].Total!.Value);
        var compared = Assert.Single(result.Compare!);
        Assert.Equal(90d, compared.Total!.Value);
        Assert.Empty(result.AllEvents);
    }

    [Fact]
    public async Task An_invocations_answer_reads_the_same_events_grouped_by_request_id()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """{"run":{"id":"run-i","status":"COMPLETED","timeframe":{"from":1,"to":2}},"statistics":{"bytes_read":1,"elapsed":0.1,"rows_read":3},"invocations":{"req-1":[{"$metadata":{"id":"e1","requestId":"req-1","message":"first"},"dataset":"cloudflare-workers","source":"first","timestamp":1793955600000}],"req-2":[{"$metadata":{"id":"e2","requestId":"req-2","message":"second"},"dataset":"cloudflare-workers","source":"second","timestamp":1793955601000},{"$metadata":{"id":"e3","requestId":"req-2","message":"third"},"dataset":"cloudflare-workers","source":"third","timestamp":1793955601100}]}}""");

        var result = await harness.Client.WorkersObservability.QueryTelemetryAsync(
            ObservabilityTelemetryQueryRequest.AdHoc(
                ObservabilityTelemetryViews.Invocations,
                ObservabilityTelemetryTimeframe.Between(1, 2)));

        // The groups are keyed by request id, and an invocation is the same type as an event.
        Assert.Equal(2, result.Invocations!.Count);
        Assert.Equal("e1", Assert.Single(result.Invocations["req-1"]).Metadata!.Id);
        Assert.Equal(2, result.Invocations["req-2"].Count);

        // Flattened, the three events are the same type they are in the events view. Which group comes
        // first out of a dictionary is not this library's business, so the ids are looked for rather than
        // indexed.
        Assert.Equal(3, result.AllInvocations.Count);
        Assert.Contains(result.AllInvocations, e => e.Metadata!.Id == "e3");
        Assert.All(result.AllInvocations, e => Assert.Equal("cloudflare-workers", e.Dataset));
        Assert.Empty(result.AllEvents);
    }

    [Fact]
    public async Task A_traces_answer_reads_its_spans_services_and_duration()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """{"run":{"id":"run-t","status":"COMPLETED","timeframe":{"from":1,"to":2}},"statistics":{"bytes_read":1,"elapsed":0.2,"rows_read":1},"traces":[{"traceId":"t-1","rootSpanName":"fetch","rootTransactionName":"GET /","service":["cfnet-demo","cfnet-other"],"spans":7,"traceDurationMs":120.5,"traceStartMs":1793955600000,"traceEndMs":1793984400120,"errors":["boom"]}]}""");

        var result = await harness.Client.WorkersObservability.QueryTelemetryAsync(
            ObservabilityTelemetryQueryRequest.AdHoc(
                ObservabilityTelemetryViews.Traces,
                ObservabilityTelemetryTimeframe.Between(1, 2)));

        var trace = Assert.Single(result.AllTraces);
        Assert.Equal("t-1", trace.TraceId);
        Assert.Equal("GET /", trace.RootTransactionName);
        Assert.Equal(7, trace.Spans);
        Assert.Equal(120.5, trace.TraceDurationMs);
        Assert.Equal("cfnet-demo,cfnet-other", string.Join(",", trace.Service!));
        Assert.Equal("boom", Assert.Single(trace.Errors!));

        var span = trace.Span!.Value;
        Assert.Equal(new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero), span.Start);
        Assert.Equal(120, (span.End - span.Start).TotalMilliseconds);
        Assert.Equal("GET / (7 spans, 120.5ms)", trace.ToString());
    }

    [Fact]
    public async Task An_agents_answer_reads_its_models_tokens_and_cursor()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """{"run":{"id":"run-a","status":"COMPLETED","timeframe":{"from":1,"to":2}},"statistics":{"bytes_read":1,"elapsed":0.3,"rows_read":1},"agents":[{"id":"agent-cursor-1","traceId":"t-9","status":"completed","spans":4,"models":["llama-3.3-70b"],"providers":["workers-ai"],"services":["cfnet-agent"],"errors":[],"traceStartMs":1793955600000,"traceEndMs":1793955601000,"traceDurationMs":1000,"agentId":"a-1","agentName":"greeter","conversationId":"c-1","inputTokens":42,"outputTokens":7}]}""");

        var result = await harness.Client.WorkersObservability.QueryTelemetryAsync(
            ObservabilityTelemetryQueryRequest.AdHoc(
                ObservabilityTelemetryViews.Agents,
                ObservabilityTelemetryTimeframe.Between(1, 2)));

        var agent = Assert.Single(result.AllAgents);

        // The id is the cursor a next page is asked for with.
        Assert.Equal("agent-cursor-1", agent.Id);
        Assert.Equal("completed", agent.Status);
        Assert.Equal("greeter", agent.AgentName);
        Assert.Equal("llama-3.3-70b", Assert.Single(agent.Models!));
        Assert.Equal(42, agent.InputTokens);
        Assert.Equal(7, agent.OutputTokens);
        Assert.Empty(agent.Errors!);
        Assert.Equal(1000d, agent.TraceDurationMs);
        Assert.Equal(1000, (agent.Span!.Value.End - agent.Span.Value.Start).TotalMilliseconds);
        Assert.Equal("greeter completed (4 spans)", agent.ToString());
    }

    [Fact]
    public async Task A_distribution_answer_reads_its_matrix_bucket_by_bucket()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """{"run":{"id":"run-d","status":"COMPLETED","timeframe":{"from":1,"to":2}},"statistics":{"bytes_read":1,"elapsed":0.4,"rows_read":9},"distribution":{"bins":["2026-11-06T09:00:00Z","2026-11-06T09:01:00Z"],"bucketBoundaries":[0,50,100],"bucketMode":"log","buckets":["0-50ms","50-100ms"],"matrix":[[3,4],[1,1]]}}""");

        var result = await harness.Client.WorkersObservability.QueryTelemetryAsync(
            new ObservabilityTelemetryQueryRequest
            {
                QueryId = "q-8",
                Timeframe = ObservabilityTelemetryTimeframe.Between(1, 2),
                View = ObservabilityTelemetryViews.Calculations,
                ChartType = ObservabilityTelemetryChartTypes.Distribution,
                DistributionScale = ObservabilityTelemetryDistributionScales.Log,
            });

        var distribution = result.Distribution!;
        Assert.Equal(ObservabilityTelemetryDistributionScales.Log, distribution.BucketMode);
        Assert.Equal(2, distribution.Bins!.Count);
        Assert.Equal(2, distribution.Buckets!.Count);

        // One boundary more than there are buckets, and the matrix indexed value bucket first.
        Assert.Equal(3, distribution.BucketBoundaries!.Count);
        Assert.Equal(50d, distribution.BucketBoundaries[1]);
        Assert.Equal(4d, distribution.Matrix![0][1]);
        Assert.Equal(1d, distribution.Matrix[1][0]);
        Assert.Equal("2x2 log", distribution.ToString());
        Assert.Empty(result.AllCalculations);
    }

    [Fact]
    public async Task An_answer_that_adds_a_field_keeps_it()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """{"run":{"id":"run-x","status":"STARTED","timeframe":{"from":1,"to":2},"futureRunField":7},"statistics":{"bytes_read":0,"elapsed":0,"rows_read":0,"future_stat":true},"futureShape":[{"a":1}]}""");

        var result = await harness.Client.WorkersObservability.QueryTelemetryAsync(
            ObservabilityTelemetryQueryRequest.AdHoc(
                ObservabilityTelemetryViews.Requests,
                ObservabilityTelemetryTimeframe.Between(1, 2)));

        Assert.False(result.IsComplete);
        Assert.False(result.Run!.IsComplete);
        Assert.Equal(ObservabilityTelemetryRunStatuses.Started, result.Run.Status);
        Assert.True(result.AdditionalData!.ContainsKey("futureShape"));
        Assert.True(result.Run.AdditionalData!.ContainsKey("futureRunField"));
        Assert.True(result.Statistics!.AdditionalData!.ContainsKey("future_stat"));

        // Nothing arrived for any view, and every accessor says so rather than throwing.
        Assert.Empty(result.AllEvents);
        Assert.Empty(result.AllInvocations);
        Assert.Empty(result.AllTraces);
        Assert.Empty(result.AllAgents);
        Assert.Empty(result.AllCalculations);
        Assert.Null(result.Events);
    }
}
