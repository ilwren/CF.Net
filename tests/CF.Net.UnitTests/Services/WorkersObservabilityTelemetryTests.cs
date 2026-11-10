using System.Net;
using System.Text.Json;
using CFNet.Exceptions;
using CFNet.Models.Workers;
using CFNet.Serialization;
using CFNet.UnitTests.Testing;
using Xunit;

namespace CFNet.UnitTests.Services;

/// <summary>
/// Workers Observability telemetry: which keys exist, which values they take, and preparing a live tail. The
/// parts worth pinning are the ones that surprise - two of these lists are posted rather than got, their
/// filters are a union whose arm arrives as <c>kind</c>, and the field names are camelCase.
/// </summary>
public class WorkersObservabilityTelemetryTests
{
    [Fact]
    public async Task ListTelemetryKeysAsync_posts_an_empty_body_when_nothing_is_asked_for()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """[{"key":"outcome","lastSeenAt":1762412345678,"type":"string"}]""");

        var keys = await harness.Client.WorkersObservability.ListTelemetryKeysAsync();

        // A list, but a POST: what narrows it is a filter tree, which is a body and not a query string.
        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/workers/observability/telemetry/keys",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Equal("{}", harness.LastRequest.Body);

        var key = Assert.Single(keys);
        Assert.Equal("outcome", key.Key);
        Assert.Equal(1762412345678, key.LastSeenAt);
        Assert.Equal(ObservabilityTelemetryValueTypes.String, key.Type);
        Assert.Equal("outcome (string)", key.ToString());
    }

    [Fact]
    public async Task ListTelemetryKeysAsync_sends_a_leaf_filter_with_its_kind()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");

        await harness.Client.WorkersObservability.ListTelemetryKeysAsync(
            new ObservabilityTelemetryKeysRequest
            {
                Datasets = new List<string> { "workers_logs" },
                Limit = 10,
                Filters = new List<ObservabilityTelemetryFilterNode>
                {
                    ObservabilityTelemetryFilter.Of(
                        "outcome",
                        ObservabilityFilterOperations.Equal,
                        ObservabilityTelemetryValueTypes.String,
                        "ok"),
                },
            });

        // The discriminator is written by the serializer, not set by the caller, and a number or a boolean
        // value reaches Cloudflare unquoted because Value is an object rather than a string.
        Assert.Equal(
            """{"datasets":["workers_logs"],"filters":[{"kind":"filter","key":"outcome","operation":"eq","type":"string","value":"ok"}],"limit":10}""",
            harness.LastRequest.Body);
    }

    [Fact]
    public async Task A_filter_group_nests_and_carries_its_combination()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");

        await harness.Client.WorkersObservability.ListTelemetryKeysAsync(
            new ObservabilityTelemetryKeysRequest
            {
                Filters = new List<ObservabilityTelemetryFilterNode>
                {
                    ObservabilityTelemetryFilterGroup.And(
                        ObservabilityTelemetryFilter.Of(
                            "scriptName",
                            ObservabilityFilterOperations.Includes,
                            ObservabilityTelemetryValueTypes.String,
                            "cfnet"),
                        ObservabilityTelemetryFilterGroup.Or(
                            ObservabilityTelemetryFilter.Of(
                                "duration",
                                ObservabilityFilterOperations.GreaterThanOperator,
                                ObservabilityTelemetryValueTypes.Number,
                                100),
                            ObservabilityTelemetryFilter.Of(
                                "isError",
                                ObservabilityFilterOperations.Equal,
                                ObservabilityTelemetryValueTypes.Boolean,
                                true))),
                },
            });

        var sent = JsonSerializer.Deserialize<ObservabilityTelemetryKeysRequest>(
            harness.LastRequest.Body!,
            CloudflareJson.Default)!;

        var group = Assert.IsType<ObservabilityTelemetryFilterGroup>(Assert.Single(sent.Filters!));
        Assert.Equal(ObservabilityFilterCombinations.And, group.FilterCombination);
        Assert.Equal(2, group.Filters!.Count);

        var leaf = Assert.IsType<ObservabilityTelemetryFilter>(group.Filters[0]);
        Assert.Equal("scriptName", leaf.Key);

        // A value declared as object comes back as the raw JSON it was, whichever kind it holds.
        Assert.Equal("cfnet", Assert.IsType<JsonElement>(leaf.Value).GetString());

        var inner = Assert.IsType<ObservabilityTelemetryFilterGroup>(group.Filters[1]);
        Assert.Equal(ObservabilityFilterCombinations.Or, inner.FilterCombination);

        // A number and a boolean are written as themselves, so they come back as themselves.
        var duration = Assert.IsType<ObservabilityTelemetryFilter>(inner.Filters![0]);
        Assert.Equal(JsonValueKind.Number, Assert.IsType<JsonElement>(duration.Value).ValueKind);
        Assert.Equal(100, ((JsonElement)duration.Value!).GetInt32());

        var isError = Assert.IsType<ObservabilityTelemetryFilter>(inner.Filters[1]);
        Assert.Equal(JsonValueKind.True, Assert.IsType<JsonElement>(isError.Value).ValueKind);
    }

    [Fact]
    public async Task ListTelemetryKeysAsync_sends_both_search_terms()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");

        await harness.Client.WorkersObservability.ListTelemetryKeysAsync(
            new ObservabilityTelemetryKeysRequest
            {
                From = 1762400000000,
                To = 1762412345678,
                Needle = ObservabilityTelemetryNeedle.Literal("err"),
                KeyNeedle = ObservabilityTelemetryNeedle.Regex("^http", matchCase: true),
            });

        Assert.Equal(
            """{"from":1762400000000,"to":1762412345678,"needle":{"value":"err"},"keyNeedle":{"value":"^http","isRegex":true,"matchCase":true}}""",
            harness.LastRequest.Body);
    }

    [Fact]
    public async Task ListTelemetryKeysAsync_refuses_a_filter_cloudflare_would_refuse()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.WorkersObservability.ListTelemetryKeysAsync(
                new ObservabilityTelemetryKeysRequest
                {
                    Limit = 0,
                    Filters = new List<ObservabilityTelemetryFilterNode>
                    {
                        // No key, no operation, no type, and a group that does not say how it combines.
                        new ObservabilityTelemetryFilter(),
                        new ObservabilityTelemetryFilterGroup(),
                    },
                }));

        Assert.Equal(6, exception.Problems.Count);
        Assert.Contains("Key", exception.Problems[0], StringComparison.Ordinal);
        Assert.Contains("Operation", exception.Problems[1], StringComparison.Ordinal);
        Assert.Contains("Type", exception.Problems[2], StringComparison.Ordinal);
        Assert.Contains("FilterCombination", exception.Problems[3], StringComparison.Ordinal);
        Assert.Contains("Filters", exception.Problems[4], StringComparison.Ordinal);
        Assert.Contains("Limit", exception.Problems[5], StringComparison.Ordinal);

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task A_filter_tree_deeper_than_four_is_refused_before_it_is_sent()
    {
        using var harness = CloudflareHarness.Create();

        // The published schema caps group nesting at four, so four groups around a leaf are fine and a fifth
        // is the one that would be refused.
        ObservabilityTelemetryFilterNode tree = ObservabilityTelemetryFilter.Of(
            "outcome",
            ObservabilityFilterOperations.Exists,
            ObservabilityTelemetryValueTypes.String);
        for (var depth = 0; depth <= ObservabilityTelemetryFilterNode.MaximumNestingDepth; depth++)
        {
            tree = ObservabilityTelemetryFilterGroup.And(tree);
        }

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.WorkersObservability.ListTelemetryKeysAsync(
                new ObservabilityTelemetryKeysRequest
                {
                    Filters = new List<ObservabilityTelemetryFilterNode> { tree },
                }));

        Assert.Contains("nest", Assert.Single(exception.Problems), StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task An_existence_filter_needs_no_value()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");

        await harness.Client.WorkersObservability.ListTelemetryKeysAsync(
            new ObservabilityTelemetryKeysRequest
            {
                Filters = new List<ObservabilityTelemetryFilterNode>
                {
                    ObservabilityTelemetryFilter.Of(
                        "error",
                        ObservabilityFilterOperations.Exists,
                        ObservabilityTelemetryValueTypes.Boolean),
                },
            });

        // No value, and no null written for one either.
        Assert.Equal(
            """{"filters":[{"kind":"filter","key":"error","operation":"exists","type":"boolean"}]}""",
            harness.LastRequest.Body);
    }

    [Fact]
    public async Task ListTelemetryValuesAsync_sends_the_key_its_type_and_a_timeframe()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """[{"dataset":"workers_logs","key":"outcome","type":"string","value":"ok"},""" +
            """{"dataset":"workers_logs","key":"outcome","type":"string","value":404},""" +
            """{"dataset":"workers_logs","key":"outcome","type":"string","value":false}]""");

        var values = await harness.Client.WorkersObservability.ListTelemetryValuesAsync(
            new ObservabilityTelemetryValuesRequest
            {
                Datasets = new List<string>(),
                Key = "outcome",
                Type = ObservabilityTelemetryValueTypes.String,
                Timeframe = ObservabilityTelemetryTimeframe.Between(1762400000000, 1762412345678),
                Limit = 25,
            });

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/workers/observability/telemetry/values",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Equal(
            """{"datasets":[],"key":"outcome","timeframe":{"from":1762400000000,"to":1762412345678},"type":"string","limit":25}""",
            harness.LastRequest.Body);

        // The value is a string, a number or a boolean and the schema does not narrow it, so it arrives raw
        // and reads back as text whichever it was.
        Assert.Equal(3, values.Count);
        Assert.Equal("ok", values[0].ValueText);
        Assert.Equal("404", values[1].ValueText);
        Assert.Equal("false", values[2].ValueText);
        Assert.Equal("outcome=ok (workers_logs)", values[0].ToString());
    }

    [Fact]
    public async Task ListTelemetryValuesAsync_refuses_a_request_missing_what_the_schema_requires()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.WorkersObservability.ListTelemetryValuesAsync(
                new ObservabilityTelemetryValuesRequest()));

        // Key, Datasets - required to be there even when empty - Timeframe and Type.
        Assert.Equal(4, exception.Problems.Count);
        Assert.Contains("Key", exception.Problems[0], StringComparison.Ordinal);
        Assert.Contains("Datasets", exception.Problems[1], StringComparison.Ordinal);
        Assert.Contains("Timeframe", exception.Problems[2], StringComparison.Ordinal);
        Assert.Contains("Type", exception.Problems[3], StringComparison.Ordinal);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => harness.Client.WorkersObservability.ListTelemetryValuesAsync(null!));

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task A_timeframe_that_ends_before_it_starts_is_refused()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.WorkersObservability.ListTelemetryValuesAsync(
                new ObservabilityTelemetryValuesRequest
                {
                    Datasets = new List<string>(),
                    Key = "outcome",
                    Type = ObservabilityTelemetryValueTypes.String,
                    Timeframe = ObservabilityTelemetryTimeframe.Between(2000, 1000),
                }));

        Assert.Contains("empty span", Assert.Single(exception.Problems), StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task PrepareLiveTailAsync_posts_and_reads_the_websocket_address()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"wsUrl":"wss://live-tail.example.workers.dev/session-1"}""");

        var tail = await harness.Client.WorkersObservability.PrepareLiveTailAsync();

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/workers/observability/telemetry/live-tail",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Equal("{}", harness.LastRequest.Body);

        Assert.Equal("wss://live-tail.example.workers.dev/session-1", tail.WsUrl);
        Assert.Equal("wss://live-tail.example.workers.dev/session-1", tail.ToString());
    }

    [Fact]
    public async Task PrepareLiveTailAsync_can_restrict_the_tail_to_one_worker()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"wsUrl":"wss://live-tail/session-2"}""");

        await harness.Client.WorkersObservability.PrepareLiveTailAsync(
            new ObservabilityLiveTailRequest
            {
                ScriptId = "script-1",
                FilterCombination = ObservabilityFilterCombinations.Or,
                Filters = new List<ObservabilityTelemetryFilterNode>
                {
                    ObservabilityTelemetryFilter.Of(
                        "outcome",
                        ObservabilityFilterOperations.NotEqual,
                        ObservabilityTelemetryValueTypes.String,
                        "ok"),
                },
            });

        Assert.Equal(
            """{"scriptId":"script-1","filterCombination":"or","filters":[{"kind":"filter","key":"outcome","operation":"neq","type":"string","value":"ok"}]}""",
            harness.LastRequest.Body);
    }

    [Fact]
    public async Task SendLiveTailHeartbeatAsync_posts_the_session_it_is_keeping_alive()
    {
        using var harness = CloudflareHarness.Create();

        // Two heartbeats, so two responses: the stub answers from a queue and runs out rather than guessing.
        harness.Respond("{\"success\":true,\"errors\":[],\"messages\":[]}");
        harness.Respond("{\"success\":true,\"errors\":[],\"messages\":[]}");

        await harness.Client.WorkersObservability.SendLiveTailHeartbeatAsync("script-1");

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/workers/observability/telemetry/live-tail/heartbeat",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Equal("""{"scriptId":"script-1"}""", harness.LastRequest.Body);

        // The published response type is "unknown", so there is nothing to read back and nothing to assert
        // beyond the call having been accepted.
        await harness.Client.WorkersObservability.SendLiveTailHeartbeatAsync();
        Assert.Equal("{}", harness.LastRequest.Body);
        Assert.Equal(2, harness.RequestCount);
    }

    [Fact]
    public void A_span_of_dates_is_sent_in_the_milliseconds_the_endpoint_counts_in()
    {
        var from = new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 11, 7, 0, 0, 0, TimeSpan.Zero);

        var timeframe = ObservabilityTelemetryTimeframe.Between(from, to);

        Assert.Equal(86_400_000, timeframe.To - timeframe.From);
        Assert.Equal(from, DateTimeOffset.UnixEpoch.AddMilliseconds(timeframe.From!.Value));
        Assert.Equal(to, DateTimeOffset.UnixEpoch.AddMilliseconds(timeframe.To!.Value));

        // The same conversion on the request that takes a bare span rather than a timeframe.
        var request = ObservabilityTelemetryKeysRequest.OverSpan(from, to);
        Assert.Equal(timeframe.From, request.From);
        Assert.Equal(timeframe.To, request.To);
    }

    [Fact]
    public void A_key_reads_its_last_seen_as_the_date_it_counts_milliseconds_towards()
    {
        // This is a live value, and the date it reads as is the day it was reported: the magnitude is what
        // settled the unit the published schema leaves unstated.
        var key = JsonSerializer.Deserialize<ObservabilityTelemetryKey>(
            """{"key":"$metadata.message","lastSeenAt":1793943698985,"type":"string"}""",
            CloudflareJson.Default)!;

        Assert.Equal(1793943698985, key.LastSeenAt);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMilliseconds(1793943698985), key.LastSeen);
        Assert.Equal(2026, key.LastSeen!.Value.Year);
        Assert.Equal("$metadata.message (string)", key.ToString());

        Assert.Null(new ObservabilityTelemetryKey().LastSeen);
    }

    [Fact]
    public void Telemetry_keys_and_values_keep_any_field_they_do_not_model()
    {
        var key = JsonSerializer.Deserialize<ObservabilityTelemetryKey>(
            """{"key":"outcome","lastSeenAt":1,"type":"string","dataset":"workers_logs"}""",
            CloudflareJson.Default)!;
        Assert.Contains("dataset", key.AdditionalData!.Keys, StringComparer.Ordinal);

        var value = JsonSerializer.Deserialize<ObservabilityTelemetryValue>(
            """{"dataset":"d","key":"k","type":"number","value":7,"count":3}""",
            CloudflareJson.Default)!;
        Assert.Contains("count", value.AdditionalData!.Keys, StringComparer.Ordinal);
        Assert.Equal("7", value.ValueText);
    }
}
