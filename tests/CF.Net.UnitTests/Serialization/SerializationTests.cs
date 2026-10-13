using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Models.Common;
using CFNet.Models.D1;
using CFNet.Models.Dns;
using CFNet.Models.R2;
using CFNet.Models.Zones;
using CFNet.Serialization;
using Xunit;

namespace CFNet.UnitTests.Serialization;

/// <summary>A local enum used to pin down the converter contract independently of the shipping enums.</summary>
[JsonConverter(typeof(CloudflareEnumConverter<SampleState>))]
internal enum SampleState
{
    /// <summary>Sentinel absorbing wire values the library does not know yet. Deliberately unattributed.</summary>
    Unknown = 0,

    [CloudflareValue("in_progress")]
    InProgress = 1,

    [CloudflareValue("Done")]
    Done = 2,
}

public class CloudflareEnumConverterTests
{
    [Fact]
    public void ToWire_uses_the_attribute_value_verbatim_including_case()
    {
        Assert.Equal("in_progress", CloudflareEnumConverter<SampleState>.ToWire(SampleState.InProgress));
        Assert.Equal("Done", CloudflareEnumConverter<SampleState>.ToWire(SampleState.Done));
    }

    [Fact]
    public void KnownValues_skips_members_without_an_attribute()
    {
        var known = CloudflareEnumConverter<SampleState>.KnownValues;

        Assert.Equal(2, known.Count);
        Assert.Contains("in_progress", known);
        Assert.Contains("Done", known);
        Assert.DoesNotContain("Unknown", known);
    }

    [Fact]
    public void ToWire_throws_for_the_unknown_sentinel()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CloudflareEnumConverter<SampleState>.ToWire(SampleState.Unknown));

        Assert.Contains("Unknown", exception.Message, StringComparison.Ordinal);
        Assert.Contains("in_progress", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryToWire_reports_failure_instead_of_throwing()
    {
        Assert.True(CloudflareEnumConverter<SampleState>.TryToWire(SampleState.Done, out var wire));
        Assert.Equal("Done", wire);

        Assert.False(CloudflareEnumConverter<SampleState>.TryToWire(SampleState.Unknown, out var missing));
        Assert.Null(missing);
    }

    [Fact]
    public void FromWire_matches_exactly_first_and_tolerates_casing_drift()
    {
        Assert.Equal(SampleState.Done, CloudflareEnumConverter<SampleState>.FromWire("Done"));
        Assert.Equal(SampleState.InProgress, CloudflareEnumConverter<SampleState>.FromWire("in_progress"));

        // Reading is forgiving on purpose: an exact match always wins, and a differently cased value
        // still binds rather than collapsing onto the sentinel. Writing stays strict, so a wrong
        // literal can never be sent back to Cloudflare.
        Assert.Equal(SampleState.Done, CloudflareEnumConverter<SampleState>.FromWire("done"));
        Assert.Equal(SampleState.InProgress, CloudflareEnumConverter<SampleState>.FromWire("IN_PROGRESS"));
    }

    [Fact]
    public void FromWire_maps_unrecognised_values_onto_the_sentinel()
    {
        Assert.Equal(SampleState.Unknown, CloudflareEnumConverter<SampleState>.FromWire("brand_new_state"));
        Assert.Equal(SampleState.Unknown, CloudflareEnumConverter<SampleState>.FromWire(string.Empty));
        Assert.Equal(SampleState.Unknown, CloudflareEnumConverter<SampleState>.FromWire(null));
    }

    [Fact]
    public void Json_round_trip_uses_the_wire_names()
    {
        var json = JsonSerializer.Serialize(new Holder { State = SampleState.InProgress }, CloudflareJson.Default);

        Assert.Equal("{\"state\":\"in_progress\"}", json);

        var back = JsonSerializer.Deserialize<Holder>("{\"state\":\"Done\"}", CloudflareJson.Default);

        Assert.Equal(SampleState.Done, back!.State);
    }

    [Fact]
    public void Json_read_of_an_unknown_literal_yields_the_sentinel()
    {
        var holder = JsonSerializer.Deserialize<Holder>("{\"state\":\"something_else\"}", CloudflareJson.Default);

        Assert.Equal(SampleState.Unknown, holder!.State);
    }

    [Fact]
    public void Json_write_of_the_sentinel_throws_instead_of_emitting_junk()
    {
        // The converter throws JsonException, which System.Text.Json surfaces as-is.
        var exception = Assert.Throws<JsonException>(
            () => JsonSerializer.Serialize(new Holder { State = SampleState.Unknown }, CloudflareJson.Default));

        Assert.Contains("SampleState", exception.Message, StringComparison.Ordinal);
        Assert.Contains("in_progress", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Standard", R2StorageClass.Standard)]
    [InlineData("InfrequentAccess", R2StorageClass.InfrequentAccess)]
    public void R2_storage_class_uses_pascal_case_literals(string wire, R2StorageClass expected)
    {
        Assert.Equal(expected, CloudflareEnumConverter<R2StorageClass>.FromWire(wire));
        Assert.Equal(wire, CloudflareEnumConverter<R2StorageClass>.ToWire(expected));
    }

    [Theory]
    [InlineData("wnam", R2LocationHint.Wnam)]
    [InlineData("enam", R2LocationHint.Enam)]
    [InlineData("weur", R2LocationHint.Weur)]
    [InlineData("eeur", R2LocationHint.Eeur)]
    [InlineData("apac", R2LocationHint.Apac)]
    [InlineData("oc", R2LocationHint.Oc)]
    public void R2_location_hint_uses_lowercase_literals(string wire, R2LocationHint expected)
    {
        Assert.Equal(expected, CloudflareEnumConverter<R2LocationHint>.FromWire(wire));
        Assert.Equal(wire, CloudflareEnumConverter<R2LocationHint>.ToWire(expected));
    }

    [Theory]
    [InlineData("default", R2Jurisdiction.Default)]
    [InlineData("eu", R2Jurisdiction.Eu)]
    [InlineData("us", R2Jurisdiction.Us)]
    [InlineData("fedramp", R2Jurisdiction.Fedramp)]
    [InlineData("fedramp-high", R2Jurisdiction.FedrampHigh)]
    public void R2_jurisdiction_maps_the_documented_literals(string wire, R2Jurisdiction expected)
    {
        Assert.Equal(expected, CloudflareEnumConverter<R2Jurisdiction>.FromWire(wire));
        Assert.Equal(wire, CloudflareEnumConverter<R2Jurisdiction>.ToWire(expected));
    }

    [Fact]
    public void Every_r2_enum_keeps_a_non_serialisable_sentinel()
    {
        // The sentinel exists only so an unknown literal does not blow up; it must never be written.
        Assert.False(CloudflareEnumConverter<R2StorageClass>.TryToWire(R2StorageClass.Unknown, out _));
        Assert.False(CloudflareEnumConverter<R2LocationHint>.TryToWire(R2LocationHint.Unknown, out _));
        Assert.False(CloudflareEnumConverter<R2Jurisdiction>.TryToWire(R2Jurisdiction.Unknown, out _));
        Assert.Equal(0, (int)R2StorageClass.Unknown);
        Assert.Equal(0, (int)R2LocationHint.Unknown);
        Assert.Equal(0, (int)R2Jurisdiction.Unknown);
    }

    [Fact]
    public void R2_bucket_serialises_its_enums_as_strings()
    {
        var bucket = new R2Bucket
        {
            Name = "assets",
            Location = R2LocationHint.Eeur,
            StorageClass = R2StorageClass.InfrequentAccess,
            Jurisdiction = R2Jurisdiction.Eu,
        };

        var json = JsonSerializer.Serialize(bucket, CloudflareJson.Default);

        Assert.Contains("\"location\":\"eeur\"", json, StringComparison.Ordinal);
        Assert.Contains("\"storage_class\":\"InfrequentAccess\"", json, StringComparison.Ordinal);
        Assert.Contains("\"jurisdiction\":\"eu\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void R2_bucket_tolerates_a_location_hint_added_after_this_release()
    {
        var bucket = JsonSerializer.Deserialize<R2Bucket>(
            "{\"name\":\"assets\",\"location\":\"antarctica\",\"storage_class\":\"ColdArchive\"}",
            CloudflareJson.Default);

        Assert.NotNull(bucket);
        Assert.Equal("assets", bucket!.Name);
        Assert.Equal(R2LocationHint.Unknown, bucket.Location);
        Assert.Equal(R2StorageClass.Unknown, bucket.StorageClass);
        Assert.Null(bucket.Jurisdiction);
    }

    [Fact]
    public void Query_enum_helpers_produce_the_query_string_words()
    {
        Assert.Equal("asc", QueryEnums.ToWire(SortDirection.Ascending));
        Assert.Equal("desc", QueryEnums.ToWire(SortDirection.Descending));
        Assert.Equal("all", QueryEnums.ToWire(FilterMatchMode.All));
        Assert.Equal("any", QueryEnums.ToWire(FilterMatchMode.Any));
    }

    [Fact]
    public void Query_enums_have_no_sentinel_so_every_member_round_trips()
    {
        Assert.Equal(2, CloudflareEnumConverter<SortDirection>.KnownValues.Count);
        Assert.Equal(2, CloudflareEnumConverter<FilterMatchMode>.KnownValues.Count);
    }

    private sealed class Holder
    {
        public SampleState State { get; set; }
    }
}

public class CloudflareJsonTests
{
    private sealed class Probe
    {
        public string? ZoneName { get; set; }

        public int PerPage { get; set; }

        public DateTimeOffset? ActivatedOn { get; set; }

        public List<string>? Nameservers { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JsonElement>? AdditionalData { get; set; }
    }

    [Fact]
    public void Default_writes_lower_snake_case()
    {
        var json = JsonSerializer.Serialize(new Probe { ZoneName = "example.com", PerPage = 20 }, CloudflareJson.Default);

        Assert.Contains("\"zone_name\":\"example.com\"", json, StringComparison.Ordinal);
        Assert.Contains("\"per_page\":20", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Default_omits_nulls_but_keeps_default_numbers()
    {
        var json = JsonSerializer.Serialize(new Probe { PerPage = 0 }, CloudflareJson.Default);

        Assert.DoesNotContain("activated_on", json, StringComparison.Ordinal);
        Assert.DoesNotContain("nameservers", json, StringComparison.Ordinal);
        Assert.DoesNotContain("zone_name", json, StringComparison.Ordinal);
        Assert.Contains("\"per_page\":0", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Default_reads_snake_case_in_any_casing_but_not_camel_case()
    {
        // The naming policy decides the wire name once, so "zone_name" binds whatever its casing.
        Assert.Equal("a.test", JsonSerializer.Deserialize<Probe>("{\"zone_name\":\"a.test\"}", CloudflareJson.Default)!.ZoneName);
        Assert.Equal("a.test", JsonSerializer.Deserialize<Probe>("{\"ZONE_NAME\":\"a.test\"}", CloudflareJson.Default)!.ZoneName);

        // camelCase is a different name, not a different casing: it lands in the extension data
        // instead, which is what keeps an unmodelled field from being dropped.
        var fromCamel = JsonSerializer.Deserialize<Probe>("{\"zoneName\":\"b.test\"}", CloudflareJson.Default)!;
        Assert.Null(fromCamel.ZoneName);
        Assert.True(fromCamel.AdditionalData!.ContainsKey("zoneName"));
    }

    [Fact]
    public void Default_accepts_numbers_written_as_strings()
    {
        // Some Cloudflare endpoints stringify counts; AllowReadingFromString keeps that from throwing.
        var probe = JsonSerializer.Deserialize<Probe>("{\"per_page\":\"50\"}", CloudflareJson.Default);

        Assert.Equal(50, probe!.PerPage);
    }

    [Fact]
    public void Default_captures_unknown_fields_in_extension_data()
    {
        var probe = JsonSerializer.Deserialize<Probe>(
            "{\"zone_name\":\"a.test\",\"brand_new_field\":{\"nested\":true}}",
            CloudflareJson.Default);

        Assert.NotNull(probe!.AdditionalData);
        Assert.True(probe.AdditionalData!.ContainsKey("brand_new_field"));
        Assert.True(probe.AdditionalData["brand_new_field"].GetProperty("nested").GetBoolean());
    }

    [Fact]
    public void Default_does_not_escape_forward_slashes()
    {
        // Escaping '/' as \u002F makes the gallery's JSON pane unreadable and breaks D1 queries
        // whose SQL contains paths.
        var json = JsonSerializer.Serialize(new Probe { ZoneName = "a/b" }, CloudflareJson.Default);

        Assert.Contains("a/b", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u002F", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Default_tolerates_comments_and_trailing_commas()
    {
        var probe = JsonSerializer.Deserialize<Probe>("{ /* note */ \"zone_name\": \"a.test\", }", CloudflareJson.Default);

        Assert.Equal("a.test", probe!.ZoneName);
    }

    [Fact]
    public void CreateOptions_returns_an_independent_copy_of_default()
    {
        var copy = CloudflareJson.CreateOptions();

        Assert.NotSame(CloudflareJson.Default, copy);
        Assert.Equal(CloudflareJson.Default.PropertyNamingPolicy, copy.PropertyNamingPolicy);
        Assert.Equal(CloudflareJson.Default.DefaultIgnoreCondition, copy.DefaultIgnoreCondition);

        copy.WriteIndented = true;
        Assert.False(CloudflareJson.Default.WriteIndented);
    }

    [Fact]
    public void ToIndentedJson_pretty_prints_a_value()
    {
        var json = CloudflareJson.ToIndentedJson(new Probe { ZoneName = "example.com" });

        Assert.Contains(Environment.NewLine, json, StringComparison.Ordinal);
        Assert.Contains("\"zone_name\": \"example.com\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ToIndentedJson_of_null_is_the_literal_null()
    {
        Assert.Equal("null", CloudflareJson.ToIndentedJson<Probe>(null));
    }

    [Fact]
    public void PrettyPrint_indents_raw_json_and_returns_garbage_untouched()
    {
        var pretty = CloudflareJson.PrettyPrint("{\"a\":1,\"b\":[1,2]}");

        Assert.Contains(Environment.NewLine, pretty, StringComparison.Ordinal);
        Assert.Contains("\"a\": 1", pretty, StringComparison.Ordinal);

        Assert.Equal("not json", CloudflareJson.PrettyPrint("not json"));
        Assert.Equal(string.Empty, CloudflareJson.PrettyPrint(string.Empty));
        Assert.Equal(string.Empty, CloudflareJson.PrettyPrint(null));
    }

    [Fact]
    public void Zone_deserialises_from_a_realistic_payload()
    {
        const string json = """
        {
          "id": "023e105f4ecef8ad9ca31a8372d0c353",
          "name": "example.com",
          "development_mode": 0,
          "owner": { "id": "7ea6c192c9e9c4d0e0e0e0e0e0e0e0e0", "email": "owner@example.com", "type": "user" },
          "account": { "id": "01a7362d577a6c3019a474fd6f485823", "name": "Demo Account" },
          "meta": { "step": 2, "wildcard_proxiable": false, "custom_certificate_quota": 0, "dns_only": false },
          "name_servers": [ "amy.ns.cloudflare.com", "bob.ns.cloudflare.com" ],
          "original_name_servers": [ "ns1.olddns.com", "ns2.olddns.com" ],
          "original_registrar": "GoDaddy.com, Inc.",
          "original_dnshost": "ns1.olddns.com",
          "created_on": "2014-01-01T05:20:00.12345Z",
          "modified_on": "2014-01-01T05:20:00.12345Z",
          "activated_on": "2014-01-02T00:01:00.12345Z",
          "plan": { "id": "e592fd9519420ba7405e1307bff33214", "name": "Pro Plan", "price": 20, "currency": "USD", "frequency": "monthly", "is_subscribed": true, "can_subscribe": true, "legacy_id": "pro", "legacy_discount": false, "externally_managed": false },
          "status": "active",
          "paused": false,
          "type": "full",
          "verification_errors": [],
          "vanity_name_servers": [],
          "some_future_field": { "hello": "world" }
        }
        """;

        var zone = JsonSerializer.Deserialize<Zone>(json, CloudflareJson.Default);

        Assert.NotNull(zone);
        Assert.Equal("023e105f4ecef8ad9ca31a8372d0c353", zone!.Id);
        Assert.Equal("example.com", zone.Name);
        Assert.Equal(ZoneStatusValues.Active, zone.Status);
        Assert.True(zone.IsActive);
        Assert.Equal(ZoneTypeValues.Full, zone.Type);
        Assert.False(zone.Paused);
        Assert.Equal("01a7362d577a6c3019a474fd6f485823", zone.Account?.Id);
        Assert.Equal("Demo Account", zone.Account?.Name);
        Assert.Equal(2, zone.NameServers?.Count);
        Assert.Equal("amy.ns.cloudflare.com", zone.NameServers![0]);
        Assert.Equal(2, zone.OriginalNameServers?.Count);
        Assert.Equal(2014, zone.CreatedOn?.Year);
        Assert.Equal(20m, zone.Plan?.Price);
        Assert.Equal("USD", zone.Plan?.Currency);
        Assert.Equal("monthly", zone.Plan?.Frequency);
        Assert.True(zone.Plan?.IsSubscribed);
        Assert.Equal(2, zone.Meta?.Step);
        Assert.False(zone.Meta?.WildcardProxiable);
        Assert.NotNull(zone.AdditionalData);
        Assert.True(zone.AdditionalData!.ContainsKey("some_future_field"));

        Assert.Equal("ns1.olddns.com", zone.OriginalDnsHost);
        Assert.Equal("amy.ns.cloudflare.com", zone.NameServers![0]);

        // Fields with no first-class property still have to survive.
        Assert.True(zone.AdditionalData.ContainsKey("verification_errors"));
        Assert.True(zone.Meta!.AdditionalData!.ContainsKey("dns_only"));

        // The owner's e-mail has no typed member on the reference object but is still reachable.
        Assert.Equal("owner@example.com", zone.Owner!.AdditionalData!["email"].GetString());
    }

    [Fact]
    public void Zone_serialises_back_to_snake_case_wire_names()
    {
        var zone = new Zone
        {
            Id = "z-1",
            Name = "example.com",
            Status = ZoneStatusValues.Pending,
            Type = ZoneTypeValues.Partial,
            Paused = true,
            NameServers = new List<string> { "amy.ns.cloudflare.com" },
        };

        var json = JsonSerializer.Serialize(zone, CloudflareJson.Default);

        Assert.Contains("\"name\":\"example.com\"", json, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"pending\"", json, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"partial\"", json, StringComparison.Ordinal);
        Assert.Contains("\"paused\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"name_servers\":[\"amy.ns.cloudflare.com\"]", json, StringComparison.Ordinal);

        // Computed members are read-only helpers and must never be sent back to the API.
        Assert.DoesNotContain("is_active", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Zone_status_literals_are_the_documented_ones()
    {
        Assert.Equal("active", ZoneStatusValues.Active);
        Assert.Equal("pending", ZoneStatusValues.Pending);
        Assert.Equal("initializing", ZoneStatusValues.Initializing);
        Assert.Equal("moved", ZoneStatusValues.Moved);
        Assert.Equal("deleted", ZoneStatusValues.Deleted);
        Assert.Equal("deactivated", ZoneStatusValues.Deactivated);
        Assert.Equal("full", ZoneTypeValues.Full);
        Assert.Equal("partial", ZoneTypeValues.Partial);
    }

    [Fact]
    public void Dns_record_round_trips_through_the_wire_shape()
    {
        const string json = """
        {
          "id": "372e67954025e0ba6aaa6d586b9e0b59",
          "zone_id": "023e105f4ecef8ad9ca31a8372d0c353",
          "zone_name": "example.com",
          "name": "example.com",
          "type": "A",
          "content": "198.51.100.4",
          "proxiable": true,
          "proxied": false,
          "ttl": 120,
          "locked": false,
          "comment": "Primary web server",
          "created_on": "2014-01-01T05:20:00.12345Z",
          "modified_on": "2014-01-01T05:20:00.12345Z",
          "meta": { "auto_added": false, "source": "primary" },
          "tags": [ "owner:test", "service:web" ]
        }
        """;

        var record = JsonSerializer.Deserialize<DnsRecord>(json, CloudflareJson.Default);

        Assert.NotNull(record);
        Assert.Equal(DnsRecordTypes.A, record!.Type);
        Assert.Equal("198.51.100.4", record.Content);
        Assert.True(record.Proxiable);
        Assert.False(record.Proxied);
        Assert.Equal(120, record.Ttl);
        Assert.Equal("Primary web server", record.Comment);
        Assert.False(record.Meta?.AutoAdded);
        Assert.Equal("primary", record.Meta?.Source);
        Assert.Equal(2, record.Tags?.Count);
        Assert.Equal("A example.com -> 198.51.100.4", record.ToString());

        // "locked" is not a typed member; it must still be captured.
        Assert.NotNull(record.AdditionalData);
        Assert.True(record.AdditionalData!.ContainsKey("locked"));

        var back = JsonSerializer.Serialize(record, CloudflareJson.Default);

        Assert.Contains("\"type\":\"A\"", back, StringComparison.Ordinal);
        Assert.Contains("\"zone_id\":\"023e105f4ecef8ad9ca31a8372d0c353\"", back, StringComparison.Ordinal);
        Assert.Contains("\"ttl\":120", back, StringComparison.Ordinal);
    }

    [Fact]
    public void Srv_record_data_round_trips()
    {
        var record = new DnsRecord
        {
            Name = "_xmpp-server._tcp.example.com",
            Type = DnsRecordTypes.Srv,
            Ttl = 1,
            Data = new DnsRecordSrvData
            {
                Service = "XMPP",
                Proto = "tcp",
                Priority = 10,
                Weight = 60,
                Port = 5222,
                Target = "server.example.com",
            },
        };

        var json = JsonSerializer.Serialize(record, CloudflareJson.Default);

        Assert.Contains("\"service\":\"XMPP\"", json, StringComparison.Ordinal);
        Assert.Contains("\"proto\":\"tcp\"", json, StringComparison.Ordinal);
        Assert.Contains("\"target\":\"server.example.com\"", json, StringComparison.Ordinal);
        Assert.Contains("\"priority\":10", json, StringComparison.Ordinal);
        Assert.Contains("\"weight\":60", json, StringComparison.Ordinal);
        Assert.Contains("\"port\":5222", json, StringComparison.Ordinal);

        var back = JsonSerializer.Deserialize<DnsRecord>(json, CloudflareJson.Default);
        var data = back!.GetData<DnsRecordSrvData>();

        Assert.NotNull(data);
        Assert.Equal("XMPP", data!.Service);
        Assert.Equal(5222, data.Port);
        Assert.Equal("server.example.com", data.Target);
    }

    [Fact]
    public void GetData_returns_null_when_the_record_has_no_structured_data()
    {
        var record = new DnsRecord { Name = "example.com", Type = DnsRecordTypes.A, Content = "198.51.100.4" };

        Assert.Null(record.GetData<DnsRecordSrvData>());
    }

    [Fact]
    public void Dns_record_types_are_the_literals_cloudflare_expects()
    {
        Assert.Equal("A", DnsRecordTypes.A);
        Assert.Equal("AAAA", DnsRecordTypes.Aaaa);
        Assert.Equal("CNAME", DnsRecordTypes.Cname);
        Assert.Equal("TXT", DnsRecordTypes.Txt);
        Assert.Equal("MX", DnsRecordTypes.Mx);
        Assert.Equal("SRV", DnsRecordTypes.Srv);
        Assert.Equal("CAA", DnsRecordTypes.Caa);
        Assert.Equal("HTTPS", DnsRecordTypes.Https);
        Assert.Equal("SVCB", DnsRecordTypes.Svcb);
        Assert.Equal("LOC", DnsRecordTypes.Loc);
        Assert.Equal("NS", DnsRecordTypes.Ns);
    }

    [Fact]
    public void D1_query_result_projects_rows_onto_strongly_typed_objects()
    {
        const string json = """
        {
          "results": [
            { "id": 1, "name": "alpha" },
            { "id": 2, "name": "beta" }
          ],
          "success": true,
          "meta": {
            "served_by": "v3-prod",
            "duration": 0.512,
            "changes": 0,
            "last_row_id": 0,
            "rows_read": 2,
            "rows_written": 0,
            "size_after": 20480,
            "changed_db": false,
            "served_by_colo": "LHR",
            "served_by_primary": false,
            "served_by_region": "EEUR",
            "timings": { "sql_duration_ms": 8.2 }
          }
        }
        """;

        var result = JsonSerializer.Deserialize<D1QueryResult>(json, CloudflareJson.Default);

        Assert.NotNull(result);
        Assert.Equal(2, result!.RowCount);
        Assert.True(result.Success);

        var rows = result.Rows<Row>().ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal("alpha", rows[0].Name);
        Assert.Equal(2, rows[1].Id);

        Assert.Equal(1, result.FirstColumn<int>("id"));
        Assert.Equal(0.512, result.Meta!.Duration);
        Assert.Equal(20480, result.Meta.SizeAfter);
        Assert.Equal("LHR", result.Meta.ServedByColo);
        Assert.Equal("EEUR", result.Meta.ServedByRegion);
        Assert.False(result.Meta.ServedByPrimary);
        Assert.False(result.Meta.ChangedDb);
        Assert.Equal(8.2, result.Meta.Timings!.SqlDurationMs);
    }

    [Fact]
    public void D1_query_result_without_rows_reports_zero_and_projects_nothing()
    {
        var result = JsonSerializer.Deserialize<D1QueryResult>("{\"success\":true,\"meta\":{\"changes\":1}}", CloudflareJson.Default);

        Assert.NotNull(result);
        Assert.Equal(0, result!.RowCount);
        Assert.Empty(result.Rows<Row>());
        Assert.Equal(1, result.Meta!.Changes);
    }

    [Fact]
    public void D1_query_request_serialises_single_and_batch_shapes()
    {
        var single = D1QueryRequest.Single("SELECT * FROM t WHERE id = ?", new object?[] { 7 });
        var singleJson = JsonSerializer.Serialize(single, CloudflareJson.Default);

        Assert.Contains("\"sql\":\"SELECT * FROM t WHERE id = ?\"", singleJson, StringComparison.Ordinal);
        Assert.Contains("\"params\":[7]", singleJson, StringComparison.Ordinal);
        Assert.DoesNotContain("batch", singleJson, StringComparison.Ordinal);

        var batch = D1QueryRequest.BatchOf(new[]
        {
            new D1Query("INSERT INTO t VALUES (1)"),
            new D1Query("INSERT INTO t VALUES (?)", new object?[] { 2 }),
        });
        var batchJson = JsonSerializer.Serialize(batch, CloudflareJson.Default);

        Assert.StartsWith("{\"batch\":[", batchJson, StringComparison.Ordinal);
    }

    [Fact]
    public void D1_query_request_rejects_both_shapes_at_once()
    {
        var invalid = new D1QueryRequest
        {
            Sql = "SELECT 1",
            Batch = new List<D1Query> { new("SELECT 2") },
        };

        var exception = Assert.ThrowsAny<Exception>(invalid.Validate);
        Assert.Contains("either", exception.Message, StringComparison.OrdinalIgnoreCase);

        var empty = new D1QueryRequest();
        Assert.ThrowsAny<Exception>(empty.Validate);

        D1QueryRequest.Single("SELECT 1").Validate();
        D1QueryRequest.BatchOf(new[] { new D1Query("SELECT 1") }).Validate();
    }

    private sealed class Row
    {
        public int Id { get; set; }

        public string? Name { get; set; }
    }
}

/// <summary>
/// Cloudflare spells "no value" as JSON <c>null</c> most of the time and as <c>""</c> some of the time, and
/// one blank field used to cost the whole response: the envelope failed to parse and the caller got a null
/// that looked like "not found". These pin that a blank string now reads as null for nullable value types,
/// that real values are untouched, and that the deliberately untyped fields keep their blank strings.
/// </summary>
public class BlankStringAsNullTests
{
    /// <summary>
    /// The types the converter covers: every one of them is a string in JSON, so a blank string cannot be
    /// a value, only the absence of one.
    /// </summary>
    private sealed class StringValuedNullables
    {
        public DateTimeOffset? ModifiedOn { get; set; }
        public DateTime? CreatedOn { get; set; }
        public TimeSpan? Duration { get; set; }
        public DateOnly? Day { get; set; }
        public TimeOnly? Clock { get; set; }
        public Guid? ExternalId { get; set; }
    }

    /// <summary>
    /// The types the converter deliberately does not cover, because Cloudflare sends some of them as
    /// strings and JsonNumberHandling.AllowReadingFromString is what reads those.
    /// </summary>
    private sealed class NumericNullables
    {
        public int? Count { get; set; }
        public long? Total { get; set; }
        public decimal? Price { get; set; }
        public bool? Enabled { get; set; }
    }

    [Fact]
    public void A_blank_date_reads_as_null_and_the_rest_of_the_object_survives()
    {
        var zone = JsonSerializer.Deserialize<Zone>(
            """{"id":"z-1","name":"a.test","activated_on":"","modified_on":null,"created_on":"2024-05-01T12:00:00Z"}""",
            CloudflareJson.Default);

        Assert.NotNull(zone);
        Assert.Equal("z-1", zone!.Id);
        Assert.Null(zone.ActivatedOn);
        Assert.Null(zone.ModifiedOn);
        Assert.Equal(2024, zone.CreatedOn!.Value.Year);
    }

    [Fact]
    public void Every_string_valued_nullable_treats_a_blank_string_as_null()
    {
        var parsed = JsonSerializer.Deserialize<StringValuedNullables>(
            """{"modified_on":"","created_on":"  ","duration":"","day":"","clock":"","external_id":""}""",
            CloudflareJson.Default);

        Assert.NotNull(parsed);
        Assert.Null(parsed!.ModifiedOn);
        Assert.Null(parsed.CreatedOn);
        Assert.Null(parsed.Duration);
        Assert.Null(parsed.Day);
        Assert.Null(parsed.Clock);
        Assert.Null(parsed.ExternalId);
    }

    [Fact]
    public void Real_values_still_parse_the_way_they_did()
    {
        var parsed = JsonSerializer.Deserialize<StringValuedNullables>(
            """{"modified_on":"2024-05-01T12:00:00Z","created_on":"2024-05-01T12:00:00","duration":"00:05:00","day":"2024-05-01","clock":"12:30:00","external_id":"0f8fad5b-d9cb-469f-a165-70867728950e"}""",
            CloudflareJson.Default);

        Assert.NotNull(parsed);
        Assert.Equal(2024, parsed!.ModifiedOn!.Value.Year);
        Assert.Equal(5, parsed.Duration!.Value.TotalMinutes);
        Assert.Equal(1, parsed.Day!.Value.Day);
        Assert.Equal(30, parsed.Clock!.Value.Minute);
        Assert.Equal(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"), parsed.ExternalId);
    }

    [Fact]
    public void A_number_sent_as_a_string_still_parses()
    {
        // This is the regression the first version of the converter caused, and the reason it does not
        // cover numeric types: installing a custom converter for long? made the built-in string handling
        // stop applying, and "42" threw "Cannot get the value of a token type 'String' as a number".
        var parsed = JsonSerializer.Deserialize<NumericNullables>(
            """{"count":3,"total":"42","price":"1.25","enabled":true}""",
            CloudflareJson.Default);

        Assert.NotNull(parsed);
        Assert.Equal(3, parsed!.Count);
        Assert.Equal(42L, parsed.Total);
        Assert.Equal(1.25m, parsed.Price);
        Assert.True(parsed.Enabled);
    }

    [Fact]
    public void A_blank_string_for_a_number_is_left_to_fail_where_it_can_be_reported()
    {
        // Deliberate, and the other half of the same trade: absorbing "" for numbers would mean giving up
        // AllowReadingFromString for them. Failing here is not silent - the transport turns it into an
        // exception naming the JSON path, which beats a null that reads as "no such zone".
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<NumericNullables>("""{"count":""}""", CloudflareJson.Default));
    }


    [Fact]
    public void A_field_left_deliberately_untyped_keeps_its_blank_string()
    {
        // ZoneSetting.Value is raw JSON on purpose, so a blank string there is data about the setting
        // rather than an absent value, and turning it into null would lose the distinction.
        var setting = JsonSerializer.Deserialize<ZoneSetting>(
            """{"id":"always_online","value":""}""",
            CloudflareJson.Default);

        Assert.NotNull(setting);
        Assert.True(setting!.Value.HasValue);
        Assert.Equal(JsonValueKind.String, setting.Value!.Value.ValueKind);
        Assert.Equal(string.Empty, setting.Value.Value.GetString());
    }

    [Fact]
    public void Writing_is_unchanged_so_a_null_is_still_omitted_rather_than_sent()
    {
        // Several endpoints 400 on an explicit null, which is why the options ignore nulls when writing.
        // A converter that reads blank strings must not start emitting them.
        var json = JsonSerializer.Serialize(
            new Zone { Id = "z-1", ActivatedOn = null, Paused = false },
            CloudflareJson.Default);

        Assert.DoesNotContain("activated_on", json, StringComparison.Ordinal);
        Assert.Contains("\"paused\":false", json, StringComparison.Ordinal);

        var withDate = JsonSerializer.Serialize(
            new Zone { Id = "z-1", ActivatedOn = new DateTimeOffset(2024, 5, 1, 12, 0, 0, TimeSpan.Zero) },
            CloudflareJson.Default);

        Assert.Contains("2024-05-01T12:00:00", withDate, StringComparison.Ordinal);
    }
}

/// <summary>
/// Cloudflare's published 200 example for <c>GET /zones/{zone_id}</c>, fed through the real models. A live
/// run found what offline stubs could not: the example carries <c>tenant</c> and <c>tenant_unit</c> as
/// <b>objects</b>, and modelling them as id strings meant one field of one zone threw the whole envelope
/// away - which surfaced as "no such zone" for a zone that answered 200 with 22 fields.
/// </summary>
public class ZonePublishedExampleTests
{
    private const string Example = """
    {
      "id": "023e105f4ecef8ad9ca31a8372d0c353",
      "account": { "id": "023e105f4ecef8ad9ca31a8372d0c353", "name": "Example Account Name" },
      "activated_on": "2014-01-02T00:01:00.12345Z",
      "created_on": "2014-01-01T05:20:00.12345Z",
      "development_mode": 7200,
      "meta": {
        "cdn_only": true, "custom_certificate_quota": 1, "dns_only": true, "foundation_dns": true,
        "page_rule_quota": 100, "phishing_detected": false, "step": 2
      },
      "modified_on": "2014-01-01T05:20:00.12345Z",
      "name": "example.com",
      "name_servers": ["bob.ns.cloudflare.com", "lola.ns.cloudflare.com"],
      "original_dnshost": "NameCheap",
      "original_name_servers": ["ns1.originaldnshost.com", "ns2.originaldnshost.com"],
      "original_registrar": "GoDaddy",
      "owner": { "id": "023e105f4ecef8ad9ca31a8372d0c353", "name": "Example Org", "type": "organization" },
      "plan": {
        "id": "023e105f4ecef8ad9ca31a8372d0c353", "can_subscribe": false, "currency": "USD",
        "externally_managed": false, "frequency": "monthly", "is_subscribed": false,
        "legacy_discount": false, "legacy_id": "free", "name": "Example Org", "price": 10.99
      },
      "cname_suffix": "cdn.cloudflare.com",
      "paused": true,
      "permissions": ["#worker:read"],
      "status": "active",
      "tenant": { "id": "023e105f4ecef8ad9ca31a8372d0c353", "name": "Example Account Name" },
      "tenant_unit": { "id": "023e105f4ecef8ad9ca31a8372d0c353" },
      "type": "full",
      "vanity_name_servers": ["ns1.example.com", "ns2.example.com"],
      "verification_key": "284344499-1084221259"
    }
    """;

    [Fact]
    public void The_published_example_deserialises_without_losing_the_zone()
    {
        var zone = JsonSerializer.Deserialize<Zone>(Example, CloudflareJson.Default);

        Assert.NotNull(zone);
        Assert.Equal("example.com", zone!.Name);
        Assert.Equal("active", zone.Status);
        Assert.True(zone.IsActive);
        Assert.Equal("full", zone.Type);
    }

    [Fact]
    public void Tenant_and_tenant_unit_are_objects_carrying_an_id()
    {
        var zone = JsonSerializer.Deserialize<Zone>(Example, CloudflareJson.Default);

        Assert.NotNull(zone);
        Assert.Equal("023e105f4ecef8ad9ca31a8372d0c353", zone!.Tenant?.Id);
        Assert.Equal("Example Account Name", zone.Tenant?.Name);
        Assert.Equal("023e105f4ecef8ad9ca31a8372d0c353", zone.TenantUnit?.Id);
    }

    [Fact]
    public void The_fields_the_models_do_not_declare_survive_in_additional_data()
    {
        var zone = JsonSerializer.Deserialize<Zone>(Example, CloudflareJson.Default);

        Assert.NotNull(zone);
        Assert.Equal("cdn.cloudflare.com", zone!.CnameSuffix);
        Assert.Equal(7200, zone.DevelopmentMode);
        Assert.Equal(10.99m, zone.Plan?.Price);
        Assert.Equal(2, zone.Meta?.Step);
        Assert.Equal("NameCheap", zone.OriginalDnsHost);
        Assert.Equal(2, zone.NameServers?.Count);
        // meta carries flags this library does not declare; they are captured rather than dropped.
        Assert.NotNull(zone.Meta?.AdditionalData);
        Assert.True(zone.Meta!.AdditionalData!.ContainsKey("foundation_dns"));
    }
}
