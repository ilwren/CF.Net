using System.Net;
using System.Text.Json;
using CFNet.Exceptions;
using CFNet.Models.Radar;
using CFNet.Serialization;
using CFNet.UnitTests.Testing;
using Xunit;

namespace CFNet.UnitTests.Services;

/// <summary>
/// Radar's reference data. Three things make these endpoints worth pinning rather than assuming they behave
/// like the rest of the API: every answer wraps its payload in a property named for it, the query parameters
/// are camelCase, and the lists page with <c>offset</c> and <c>limit</c> while answering no
/// <c>result_info</c> - so the only signal that a walk has ended is the length of the page itself.
/// </summary>
public class RadarServiceTests
{
    [Fact]
    public async Task ListLocationsAsync_unwraps_the_named_array_radar_answers_with()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"locations":[{"alpha2":"GB","continent":"EU","latitude":"54.0","longitude":"-2.0","name":"United Kingdom","region":"Europe","subregion":"Northern Europe"},{"alpha2":"US","continent":"NA","latitude":"38.0","longitude":"-97.0","name":"United States","region":"Americas","subregion":"North America"}]}}""");

        var page = await harness.Client.Radar.ListLocationsAsync();

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Equal("/client/v4/radar/entities/locations", harness.LastRequest.Path);
        Assert.Equal(string.Empty, harness.LastRequest.Query.TrimStart('?'));

        // Radar answers no result_info, so a page carries its items and nothing else to read a total off.
        Assert.Equal(2, page.Count);
        Assert.Null(page.TotalCount);
        Assert.Null(page.Info);

        var first = page.Items[0];
        Assert.Equal("GB", first.Alpha2);
        Assert.Equal("EU", first.Continent);
        Assert.Equal("United Kingdom", first.Name);
        Assert.Equal("Northern Europe", first.Subregion);
        Assert.Equal("54.0", first.Latitude);
        Assert.Null(first.ConfidenceLevel);
        Assert.Equal("GB United Kingdom (Northern Europe)", first.ToString());
        Assert.True(first.AdditionalData is not { Count: > 0 });
    }

    [Fact]
    public async Task ListLocationsAsync_sends_the_filters_and_the_offset_paging_radar_uses()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("""{"success":true,"errors":[],"messages":[],"result":{"locations":[]}}""");

        await harness.Client.Radar.ListLocationsAsync(new RadarLocationListParameters
        {
            Continent = RadarContinent.Europe,
            Region = "Europe",
            Subregion = "Northern Europe",
            Location = "GB",
            Limit = 10,
            Offset = 20,
        });

        Assert.Equal(
            "continent=EU&region=Europe&subregion=Northern%20Europe&location=GB&limit=10&offset=20",
            harness.LastRequest.Query.TrimStart('?'));

        // The rest of the v4 API pages with page and per_page; radar does not, and sending them here would
        // ask for something Cloudflare ignores.
        Assert.DoesNotContain("page", harness.LastRequest.Query);
        Assert.DoesNotContain("per_page", harness.LastRequest.Query);
    }

    [Fact]
    public async Task ListLocationsAsync_refuses_a_continent_it_has_not_met()
    {
        using var harness = CloudflareHarness.Create();

        var error = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Radar.ListLocationsAsync(
                new RadarLocationListParameters { Continent = RadarContinent.Unknown }));

        Assert.Contains("not a continent", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task ListLocationsAsync_answers_an_empty_page_when_the_wrapper_carries_no_array()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("""{"success":true,"errors":[],"messages":[],"result":{}}""");

        var page = await harness.Client.Radar.ListLocationsAsync();

        Assert.Empty(page.Items);
        Assert.Equal(0, page.Count);
    }

    [Fact]
    public async Task GetLocationAsync_reads_the_confidence_only_the_single_read_reports()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"location":{"alpha2":"GB","continent":"EU","latitude":"54.0","longitude":"-2.0","name":"United Kingdom","region":"Europe","subregion":"Northern Europe","confidenceLevel":90}}}""");

        var location = await harness.Client.Radar.GetLocationAsync("GB");

        Assert.Equal("/client/v4/radar/entities/locations/GB", harness.LastRequest.Path);
        Assert.NotNull(location);
        Assert.Equal(90, location!.ConfidenceLevel);
        Assert.Equal("United Kingdom", location.Name);
    }

    [Fact]
    public async Task GetLocationAsync_reports_a_location_radar_does_not_have_as_null()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":false,"errors":[{"code":12004,"message":"Location not found"}],"messages":[],"result":null}""",
            HttpStatusCode.NotFound);

        var location = await harness.Client.Radar.GetLocationAsync("ZZ");

        // A code nobody has is an answer, not a failure: the caller asked whether radar knows the place.
        Assert.Null(location);
        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task GetLocationAsync_escapes_the_code_it_is_given()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("""{"success":true,"errors":[],"messages":[],"result":{"location":null}}""");

        await harness.Client.Radar.GetLocationAsync("a b/c");

        // Uri.AbsolutePath decodes %2F, so the encoding is checked on the Uri itself.
        Assert.Contains(
            "/radar/entities/locations/a%20b%2Fc",
            harness.LastRequest.Uri!.OriginalString,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetLocationAsync_refuses_a_code_that_is_not_there()
    {
        using var harness = CloudflareHarness.Create();

        var error = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Radar.GetLocationAsync("  "));

        Assert.Contains("location", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task ListAsnsAsync_reads_the_subset_a_list_answers()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"asns":[{"asn":13335,"country":"US","countryName":"United States","estimatedUsers":{"estimatedUsers":24000000},"name":"CLOUDFLARE-NET-02","aka":"Cloudflare","orgName":"Cloudflare, Inc.","website":"https://www.cloudflare.com"}]}}""");

        var page = await harness.Client.Radar.ListAsnsAsync();

        Assert.Equal("/client/v4/radar/entities/asns", harness.LastRequest.Path);
        Assert.Equal(1, page.Count);

        var asn = page.Items[0];
        Assert.Equal(13335L, asn.Asn);
        Assert.Equal("US", asn.Country);
        Assert.Equal("United States", asn.CountryName);
        Assert.Equal("CLOUDFLARE-NET-02", asn.Name);
        Assert.Equal("Cloudflare", asn.Aka);
        Assert.Equal("Cloudflare, Inc.", asn.OrgName);
        Assert.Equal(24000000L, asn.EstimatedUsers!.EstimatedUsers);
        Assert.Equal("AS13335 CLOUDFLARE-NET-02 (US)", asn.ToString());

        // The list leaves these out; a null says "the list did not carry it", not "there are none".
        Assert.Null(asn.Related);
        Assert.Null(asn.ConfidenceLevel);
        Assert.Null(asn.Source);
        Assert.Null(asn.EstimatedUsers.Locations);
    }

    [Fact]
    public async Task ListAsnsAsync_sends_the_camel_case_ordering_radar_asks_for()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("""{"success":true,"errors":[],"messages":[],"result":{"asns":[]}}""");

        await harness.Client.Radar.ListAsnsAsync(new RadarAsnListParameters
        {
            Asn = "13335,15169",
            Location = "US",
            Limit = 5,
            Offset = 10,
            OrderBy = RadarAsnOrderBy.Population,
        });

        // orderBy, not order_by: the radar endpoints take camelCase query parameters.
        Assert.Equal(
            "asn=13335%2C15169&location=US&limit=5&offset=10&orderBy=POPULATION",
            harness.LastRequest.Query.TrimStart('?'));
    }

    [Fact]
    public void The_asn_filter_can_be_built_from_numbers()
    {
        var parameters = RadarAsnListParameters.OfAsns(13335, 4200000000);

        Assert.Equal("13335,4200000000", parameters.Asn);
        Assert.Null(parameters.Location);

        var moved = parameters.WithOffset(50);
        Assert.Equal("13335,4200000000", moved.Asn);
        Assert.Equal(50, moved.Offset);
    }

    [Fact]
    public async Task GetAsnAsync_reads_the_detail_a_list_leaves_out()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"asn":{"asn":13335,"confidenceLevel":95,"country":"US","countryName":"United States","estimatedUsers":{"estimatedUsers":24000000,"locations":[{"locationAlpha2":"US","locationName":"United States","estimatedUsers":9000000},{"locationAlpha2":"GB","locationName":"United Kingdom","estimatedUsers":2000000}]},"name":"CLOUDFLARE-NET-02","orgName":"Cloudflare, Inc.","related":[{"asn":15169,"name":"GOOGLE","aka":"Google","estimatedUsers":30000000}],"source":"arin","website":"https://www.cloudflare.com"}}}""");

        var asn = await harness.Client.Radar.GetAsnAsync(13335);

        Assert.Equal("/client/v4/radar/entities/asns/13335", harness.LastRequest.Path);
        Assert.NotNull(asn);
        Assert.Equal(95, asn!.ConfidenceLevel);
        Assert.Equal("arin", asn.Source);
        Assert.Equal(2, asn.EstimatedUsers!.Locations!.Count);
        Assert.Equal("GB United Kingdom: 2000000 user(s)", asn.EstimatedUsers.Locations[1].ToString());
        Assert.Equal("24000000 estimated user(s) across 2 location(s)", asn.EstimatedUsers.ToString());

        var related = Assert.Single(asn.Related!);
        Assert.Equal(15169L, related.Asn);
        Assert.Equal("GOOGLE", related.Name);
        Assert.Equal(30000000L, related.EstimatedUsers);
        Assert.Equal("AS15169 GOOGLE", related.ToString());
    }

    [Fact]
    public async Task GetAsnAsync_sends_a_four_byte_asn_without_losing_it()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("""{"success":true,"errors":[],"messages":[],"result":{"asn":null}}""");

        await harness.Client.Radar.GetAsnAsync(4200000000);

        // Past int.MaxValue, which is why the number is a long all the way down.
        Assert.Equal("/client/v4/radar/entities/asns/4200000000", harness.LastRequest.Path);
    }

    [Fact]
    public async Task GetAsnForIpAsync_sends_the_address_as_a_query_parameter()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"asn":{"asn":13335,"country":"US","countryName":"United States","name":"CLOUDFLARE-NET-02","orgName":"Cloudflare, Inc.","source":"arin","website":"https://www.cloudflare.com","confidenceLevel":95,"estimatedUsers":{"estimatedUsers":24000000},"related":[]}}}""");

        var asn = await harness.Client.Radar.GetAsnForIpAsync("1.1.1.1");

        Assert.Equal("/client/v4/radar/entities/asns/ip", harness.LastRequest.Path);
        Assert.Equal("ip=1.1.1.1", harness.LastRequest.Query.TrimStart('?'));
        Assert.Equal(13335L, asn!.Asn);
        Assert.Empty(asn.Related!);
    }

    [Fact]
    public async Task GetEntityForIpAsync_reads_every_field_as_the_string_radar_sends()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"ip":{"asn":"13335","asnLocation":"US","asnName":"Cloudflare, Inc.","asnOrgName":"Cloudflare, Inc.","ip":"1.1.1.1","ipVersion":"IPv4","location":"US","locationName":"United States"}}}""");

        var entity = await harness.Client.Radar.GetEntityForIpAsync("1.1.1.1");

        Assert.Equal("/client/v4/radar/entities/ip", harness.LastRequest.Path);
        Assert.Equal("ip=1.1.1.1", harness.LastRequest.Query.TrimStart('?'));
        Assert.NotNull(entity);

        // This endpoint answers the ASN as a string, where its neighbour answers it as a number.
        Assert.Equal("13335", entity!.Asn);
        Assert.Equal(13335L, entity.AsnNumber);
        Assert.Equal("IPv4", entity.IpVersion);
        Assert.Equal("US", entity.Location);
        Assert.Equal("United States", entity.LocationName);
        Assert.Equal("1.1.1.1 in United States: AS13335 Cloudflare, Inc.", entity.ToString());
    }

    [Fact]
    public void An_entity_whose_asn_is_not_a_number_reads_as_no_number()
    {
        var entity = new RadarIpEntity { Ip = "192.0.2.1", Asn = "unknown" };

        Assert.Null(entity.AsnNumber);
        Assert.Equal("192.0.2.1 in ?: ASunknown", entity.ToString());
    }

    [Fact]
    public async Task ListGeolocationsAsync_reads_the_tree_by_nesting()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"geolocations":[{"code":"GB-ENG","geoId":"6269131","latitude":"52.0","locale":"en","longitude":"-1.0","name":"England","parent":{"code":"GB","geoId":"2635167","latitude":"54.0","longitude":"-2.0","name":"United Kingdom","parent":{"code":"EU","geoId":"6255148","latitude":"51.0","longitude":"10.0","name":"Europe","parent":null,"type":"CONTINENT"},"type":"COUNTRY"},"type":"ADM1"}]}}""");

        var page = await harness.Client.Radar.ListGeolocationsAsync(new RadarGeolocationListParameters { Limit = 1 });

        Assert.Equal("/client/v4/radar/geolocations", harness.LastRequest.Path);
        Assert.Equal("limit=1", harness.LastRequest.Query.TrimStart('?'));

        var division = page.Items[0];
        Assert.Equal("GB-ENG", division.Code);
        Assert.Equal(RadarGeolocationType.AdministrativeDivision, division.Type);
        Assert.Equal("en", division.Locale);
        Assert.Equal("GB-ENG England (ADM1)", division.ToString());

        // The tree arrives nested, so walking up it is a property chain rather than more calls.
        Assert.Equal("United Kingdom", division.Parent!.Name);
        Assert.Equal(RadarGeolocationType.Country, division.Parent.Type);
        Assert.Equal("Europe", division.Parent.Parent!.Name);
        Assert.Equal(RadarGeolocationType.Continent, division.Parent.Parent.Type);
        Assert.Null(division.Parent.Parent.Parent);

        Assert.Equal(new[] { "United Kingdom", "Europe" }, division.Ancestors.Select(place => place.Name));

        // Every level carries the id the single-geolocation read wants, so a caller can read a division and
        // then its country by id without listing again.
        Assert.Equal("6269131", division.GeoId);
        Assert.Equal("2635167", division.Parent.GeoId);
        Assert.Equal("6255148", division.Parent.Parent.GeoId);
    }

    [Fact]
    public async Task GetGeolocationAsync_reads_one_place_and_tolerates_one_that_is_not_there()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"geolocation":{"code":"GB","geoId":"2635167","latitude":"54.0","longitude":"-2.0","name":"United Kingdom","parent":{"code":"EU","geoId":"6255148","latitude":"51.0","longitude":"10.0","name":"Europe","parent":null,"type":"CONTINENT"},"type":"COUNTRY"}}}""");
        harness.Respond(
            """{"success":false,"errors":[{"code":12004,"message":"Geolocation not found"}],"messages":[],"result":null}""",
            HttpStatusCode.NotFound);

        // By geoId, not by code: a live run proved that GET /radar/geolocations/GB is answered 400 [2001],
        // while the same path with the place's geoId reads it back.
        var found = await harness.Client.Radar.GetGeolocationAsync("2635167");
        var missing = await harness.Client.Radar.GetGeolocationAsync("0");

        Assert.Equal("/client/v4/radar/geolocations/2635167", harness.Requests[0].Path);
        Assert.NotNull(found);
        Assert.Equal(RadarGeolocationType.Country, found!.Type);
        Assert.Equal("2635167", found.GeoId);
        Assert.Equal("6255148", found.Parent!.GeoId);
        Assert.Single(found.Ancestors);
        Assert.Null(missing);
        Assert.Equal(2, harness.RequestCount);
    }

    [Fact]
    public void A_geolocation_reads_the_id_its_own_schema_does_not_list()
    {
        var place = JsonSerializer.Deserialize<RadarGeolocation>(
            """{"code":"IT","geoId":"3175395","latitude":"42.83333","longitude":"12.83333","name":"Italy","parent":null,"type":"COUNTRY"}""",
            CloudflareJson.Default)!;

        // Cloudflare's published schema for the geolocation tree mentions code and locale but not geoId, while
        // every answer carries geoId - and it is the only id GET /radar/geolocations/{geo_id} accepts. Leaving
        // it unmodelled would drop the one field a caller needs into AdditionalData, where nobody looks.
        Assert.Equal("3175395", place.GeoId);
        Assert.Equal("IT", place.Code);
        Assert.Null(place.Locale);
        Assert.Null(place.Parent);
        Assert.Equal("IT Italy (COUNTRY)", place.ToString());
        Assert.True(place.AdditionalData is not { Count: > 0 });
    }

    [Fact]
    public async Task SearchAsync_sends_the_query_and_the_categories_as_repeated_parameters()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("""{"success":true,"errors":[],"messages":[],"result":{"search":[]}}""");

        await harness.Client.Radar.SearchAsync(new RadarSearchParameters
        {
            Query = "cloudflare",
            Include = new List<RadarSearchCategory> { RadarSearchCategory.Asns, RadarSearchCategory.Locations },
            Exclude = new List<RadarSearchCategory> { RadarSearchCategory.Notebooks },
            Limit = 5,
            LimitPerGroup = 2,
        });

        Assert.Equal("/client/v4/radar/search/global", harness.LastRequest.Path);
        Assert.Equal(
            "query=cloudflare&include=ASNS&include=LOCATIONS&exclude=NOTEBOOKS&limit=5&limitPerGroup=2",
            harness.LastRequest.Query.TrimStart('?'));
    }

    [Fact]
    public async Task SearchAsync_unwraps_the_hits_and_keeps_the_type_as_the_string_radar_sent()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"search":[{"code":"13335","name":"Cloudflare, Inc.","type":"ASNS"},{"code":"GB","name":"United Kingdom","type":"LOCATIONS"},{"code":"something-new","name":"A category this library has not met","type":"SOMETHING_NEWS"}]}}""");

        var hits = await harness.Client.Radar.SearchAsync(RadarSearchParameters.For("cloudflare"));

        Assert.Equal(3, hits.Count);
        Assert.Equal("13335", hits[0].Code);
        Assert.Equal("ASNS: 13335 Cloudflare, Inc.", hits[0].ToString());

        // The type is a string on a hit, so a category added after this release arrives as itself.
        Assert.Equal("SOMETHING_NEWS", hits[2].Type);
        Assert.Equal("something-new", hits[2].Code);
    }

    [Fact]
    public async Task SearchAsync_the_convenience_overload_sends_only_the_query_and_the_limit()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("""{"success":true,"errors":[],"messages":[],"result":{"search":[]}}""");

        await harness.Client.Radar.SearchAsync("cloudflare", limit: 3);

        Assert.Equal("query=cloudflare&limit=3", harness.LastRequest.Query.TrimStart('?'));
    }

    [Fact]
    public async Task SearchAsync_refuses_a_search_with_nothing_to_search_for()
    {
        using var harness = CloudflareHarness.Create();

        var error = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Radar.SearchAsync(new RadarSearchParameters { Query = "   " }));

        Assert.Single(error.Problems);
        Assert.Contains("no query", error.Problems[0], StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task SearchAsync_refuses_a_category_it_cannot_spell()
    {
        using var harness = CloudflareHarness.Create();

        var error = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Radar.SearchAsync(new RadarSearchParameters
            {
                Query = "cloudflare",
                Include = new List<RadarSearchCategory> { RadarSearchCategory.Asns, RadarSearchCategory.Unknown },
                Exclude = new List<RadarSearchCategory> { RadarSearchCategory.Unknown },
            }));

        Assert.Equal(2, error.Problems.Count);
        Assert.Contains("cannot be sent in the include list.", error.Problems[0], StringComparison.Ordinal);
        Assert.Contains("cannot be sent in the exclude list.", error.Problems[1], StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task ListAllLocationsAsync_walks_by_offset_and_stops_on_a_short_page()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"locations":[{"alpha2":"GB","name":"United Kingdom"},{"alpha2":"US","name":"United States"}]}}""");
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"locations":[{"alpha2":"FR","name":"France"}]}}""");

        var seen = new List<string?>();
        await foreach (var location in harness.Client.Radar.ListAllLocationsAsync(pageSize: 2))
        {
            seen.Add(location.Alpha2);
        }

        Assert.Equal(new[] { "GB", "US", "FR" }, seen);
        Assert.Equal(2, harness.RequestCount);
        Assert.Equal("limit=2&offset=2", harness.LastRequest.Query.TrimStart('?'));
    }

    [Fact]
    public async Task ListAllLocationsAsync_stops_when_radar_ignores_the_limit_and_sends_everything()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"locations":[{"alpha2":"GB"},{"alpha2":"US"},{"alpha2":"FR"}]}}""");

        var seen = new List<string?>();
        await foreach (var location in harness.Client.Radar.ListAllLocationsAsync(pageSize: 2))
        {
            seen.Add(location.Alpha2);
        }

        // Three items for a limit of two means the limit was not honoured, so the first page was the whole
        // list - asking for the next one would only repeat it.
        Assert.Equal(new[] { "GB", "US", "FR" }, seen);
        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task ListAllLocationsAsync_asks_for_a_page_of_a_hundred_when_no_size_is_given()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("""{"success":true,"errors":[],"messages":[],"result":{"locations":[]}}""");

        var seen = new List<RadarLocation>();
        await foreach (var location in harness.Client.Radar.ListAllLocationsAsync())
        {
            seen.Add(location);
        }

        Assert.Empty(seen);
        Assert.Equal("limit=100&offset=0", harness.LastRequest.Query.TrimStart('?'));
        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task ListAllAsnsAsync_walks_by_offset_the_same_way()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"asns":[{"asn":13335,"name":"CLOUDFLARE-NET-02"},{"asn":15169,"name":"GOOGLE"}]}}""");
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"asns":[]}}""");

        var seen = new List<long?>();
        await foreach (var asn in harness.Client.Radar.ListAllAsnsAsync(pageSize: 2))
        {
            seen.Add(asn.Asn);
        }

        Assert.Equal(new long?[] { 13335, 15169 }, seen);
        Assert.Equal(2, harness.RequestCount);
        Assert.Equal("limit=2&offset=2", harness.LastRequest.Query.TrimStart('?'));
    }

    [Fact]
    public async Task None_of_the_radar_paths_carries_an_account_or_a_zone()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("""{"success":true,"errors":[],"messages":[],"result":{"locations":[]}}""");
        harness.Respond("""{"success":true,"errors":[],"messages":[],"result":{"asns":[]}}""");
        harness.Respond("""{"success":true,"errors":[],"messages":[],"result":{"geolocations":[]}}""");
        harness.Respond("""{"success":true,"errors":[],"messages":[],"result":{"search":[]}}""");
        harness.Respond("""{"success":true,"errors":[],"messages":[],"result":{"annotations":[]}}""");
        harness.Respond("""{"success":true,"errors":[],"messages":[],"result":{"annotations":[]}}""");
        harness.Respond("""{"success":true,"errors":[],"messages":[],"result":{"datasets":[]}}""");
        harness.Respond("""{"success":true,"errors":[],"messages":[],"result":{"ases":[],"meta":{}}}""");

        await harness.Client.Radar.ListLocationsAsync();
        await harness.Client.Radar.ListAsnsAsync();
        await harness.Client.Radar.ListGeolocationsAsync();
        await harness.Client.Radar.SearchAsync("cloudflare");
        await harness.Client.Radar.ListAnnotationsAsync();
        await harness.Client.Radar.ListOutagesAsync();
        await harness.Client.Radar.ListDatasetsAsync();
        await harness.Client.Radar.ListBotnetThreatFeedAsync();

        Assert.Equal(8, harness.RequestCount);

        // Radar is public data at the API root, beside GET /ips: no id to resolve, so no id to get wrong.
        Assert.All(harness.Requests, request => Assert.StartsWith("/client/v4/radar/", request.Path, StringComparison.Ordinal));
        Assert.All(harness.Requests, request => Assert.Contains("Authorization", request.Headers.Keys));
    }

    [Fact]
    public async Task ListAnnotationsAsync_unwraps_the_annotations_and_sends_every_filter_radar_publishes()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"annotations":[{"id":"2024-000913","asns":[13335],"asnsDetails":[{"asn":"13335","location":{"code":"US","name":"United States"},"name":"Cloudflare"}],"dataSource":"BGP","description":"A regional outage","endDate":"2024-05-24T14:00:00Z","entities":[{"entityLocation":"US","entityName":"Cloudflare","entityType":"AS","entityValue":"13335"}],"eventType":"OUTAGE","geoIds":["6252001"],"linkedUrl":"https://example.com/report","locations":["US"],"locationsDetails":[{"code":"US","name":"United States"},null],"origins":["example.com"],"originsDetails":[{"name":"Example","origin":"example.com"}],"outage":{"outageCause":"POWER_OUTAGE","outageType":"REGIONAL"},"scope":"regional","startDate":"2024-05-24T12:00:00Z","tags":["LOCATION","ASN"]}]}}""");

        var page = await harness.Client.Radar.ListAnnotationsAsync(new RadarAnnotationListParameters
        {
            Asn = 13335L,
            Bot = "GPTBot",
            Ca = "LetsEncrypt",
            DataSource = RadarAnnotationDataSource.Bgp,
            DateEnd = new DateTimeOffset(2024, 5, 24, 14, 0, 0, TimeSpan.Zero),
            DateRange = "7d",
            DateStart = new DateTimeOffset(2024, 5, 17, 12, 30, 0, TimeSpan.Zero),
            EventType = RadarAnnotationEventType.Outage,
            GeoId = "6252001",
            Limit = 5,
            Location = "US",
            Log = "Nimbus",
            Offset = 10,
            Origin = "example.com",
            OutageCause = RadarAnnotationOutageCause.PowerOutage,
            OutageType = RadarAnnotationOutageType.Regional,
            Query = "power",
            Tags = new List<string> { "LOCATION", "ASN" },
            Tld = "com",
        });

        Assert.Equal("/client/v4/radar/annotations", harness.LastRequest.Path);

        // camelCase names, the tags repeated, and the two dates as the timestamps Cloudflare wants - which is
        // the same shape the search categories are sent in, and unlike the rest of the v4 API.
        Assert.Equal(
            "asn=13335&bot=GPTBot&ca=LetsEncrypt&dataSource=BGP&dateEnd=2024-05-24T14%3A00%3A00Z&dateRange=7d" +
            "&dateStart=2024-05-17T12%3A30%3A00Z&eventType=OUTAGE&geoId=6252001&limit=5&location=US&log=Nimbus" +
            "&offset=10&origin=example.com&outageCause=POWER_OUTAGE&outageType=REGIONAL&query=power" +
            "&tags=LOCATION&tags=ASN&tld=com",
            harness.LastRequest.Query.TrimStart('?'));

        var annotation = Assert.Single(page.Items);
        Assert.Equal("2024-000913", annotation.Id);
        Assert.Equal("OUTAGE", annotation.EventType);
        Assert.Equal("BGP", annotation.DataSource);
        Assert.Equal("regional", annotation.Scope);
        Assert.Equal("2024-05-24T12:00:00Z", annotation.StartDate);
        Assert.Equal("2024-05-24T14:00:00Z", annotation.EndDate);
        Assert.Equal("https://example.com/report", annotation.LinkedUrl);
        Assert.Equal(new[] { 13335L }, annotation.Asns!);
        Assert.Equal(new[] { "6252001" }, annotation.GeoIds!);
        Assert.Equal(new[] { "LOCATION", "ASN" }, annotation.Tags!);
        Assert.Equal("2024-000913 OUTAGE from 2024-05-24T12:00:00Z (POWER_OUTAGE REGIONAL)", annotation.ToString());

        // The same ASN arrives twice in one annotation: once as a number and once, in the details, as a string.
        var detail = Assert.Single(annotation.AsnsDetails!);
        Assert.Equal("13335", detail.Asn);
        Assert.Equal(13335L, detail.AsnNumber);
        Assert.Equal("Cloudflare", detail.Name);
        Assert.Equal("US United States", detail.Location!.ToString());

        var entity = Assert.Single(annotation.Entities!);
        Assert.Equal("AS: 13335", entity.ToString());
        Assert.Equal("US", entity.EntityLocation);

        // Cloudflare publishes the location details as nullable items, so an unresolved place keeps its slot.
        Assert.Equal(2, annotation.LocationsDetails!.Count);
        Assert.Equal("US United States", annotation.LocationsDetails[0]!.ToString());
        Assert.Null(annotation.LocationsDetails[1]);

        Assert.Equal("example.com", Assert.Single(annotation.OriginsDetails!).Origin);
        Assert.Equal("POWER_OUTAGE", annotation.Outage!.OutageCause);
        Assert.Equal("REGIONAL", annotation.Outage.OutageType);
        Assert.True(annotation.AdditionalData is not { Count: > 0 });
    }

    [Fact]
    public async Task ListAnnotationsAsync_refuses_a_filter_this_library_cannot_spell()
    {
        using var harness = CloudflareHarness.Create();

        var error = await Assert.ThrowsAsync<CloudflareConfigurationException>(() =>
            harness.Client.Radar.ListAnnotationsAsync(new RadarAnnotationListParameters
            {
                EventType = RadarAnnotationEventType.Unknown,
            }));

        Assert.Contains("not a radar event type", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task ListOutagesAsync_reads_the_same_shape_from_the_outage_path()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"annotations":[{"id":"2024-000913","eventType":"OUTAGE","startDate":"2024-05-24T12:00:00Z","outage":{"outageCause":"CABLE_CUT","outageType":"NATIONWIDE"}}]}}""");

        var page = await harness.Client.Radar.ListOutagesAsync(new RadarAnnotationListParameters
        {
            OutageCause = RadarAnnotationOutageCause.CableCut,
            Limit = 5,
            Offset = 5,
        });

        Assert.Equal("/client/v4/radar/annotations/outages", harness.LastRequest.Path);
        Assert.Equal("limit=5&offset=5&outageCause=CABLE_CUT", harness.LastRequest.Query.TrimStart('?'));

        var outage = Assert.Single(page.Items);
        Assert.Equal("CABLE_CUT", outage.Outage!.OutageCause);
        Assert.Equal("2024-000913 OUTAGE from 2024-05-24T12:00:00Z (CABLE_CUT NATIONWIDE)", outage.ToString());
    }

    [Fact]
    public async Task ListOutageLocationsAsync_reads_the_countries_an_outage_reached()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"annotations":[{"clientCountryAlpha2":"US","clientCountryName":"United States","value":"0.35"},{"clientCountryAlpha2":"GB","clientCountryName":"United Kingdom","value":"0.12"}]}}""");

        var page = await harness.Client.Radar.ListOutageLocationsAsync(new RadarOutageLocationsParameters
        {
            DateRange = "7d",
            Limit = 10,
        });

        Assert.Equal("/client/v4/radar/annotations/outages/locations", harness.LastRequest.Path);

        // This endpoint publishes no offset, so a walk has nothing to move along by and the parameters do not
        // offer one.
        Assert.Equal("dateRange=7d&limit=10", harness.LastRequest.Query.TrimStart('?'));
        Assert.Equal(2, page.Items.Count);
        Assert.Equal("US United States: 0.35", page.Items[0].ToString());
        Assert.Equal("0.12", page.Items[1].Value);
    }

    [Fact]
    public async Task ListDatasetsAsync_sends_a_date_without_a_time_and_reads_the_meta_it_is_given()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"datasets":[{"id":3,"description":"The top 20000 domains globally","meta":{"rows":20000},"tags":["global"],"title":"Top bucket 20000 domains","type":"RANKING_BUCKET","alias":"ranking_top_20000"}]}}""");

        var page = await harness.Client.Radar.ListDatasetsAsync(new RadarDatasetListParameters
        {
            DatasetType = RadarDatasetType.RankingBucket,
            Date = new DateTimeOffset(2026, 2, 23, 9, 30, 0, TimeSpan.Zero),
            Limit = 5,
        });

        Assert.Equal("/client/v4/radar/datasets", harness.LastRequest.Path);

        // Published with format: date, unlike the annotation filters beside it, so no time part is sent.
        Assert.Equal("datasetType=RANKING_BUCKET&date=2026-02-23&limit=5", harness.LastRequest.Query.TrimStart('?'));

        var dataset = Assert.Single(page.Items);
        Assert.Equal(3L, dataset.Id);
        Assert.Equal("RANKING_BUCKET", dataset.Type);
        Assert.Equal(new[] { "global" }, dataset.Tags!);
        Assert.Equal("3 Top bucket 20000 domains (RANKING_BUCKET)", dataset.ToString());

        // Unpublished, like the geoId a geolocation carries: the name that identifies this dataset across
        // dates, which the numeric id does not.
        Assert.Equal("ranking_top_20000", dataset.Alias);

        // Cloudflare publishes meta as unknown and answers a different object per dataset, so it is read as the
        // JSON it arrives as rather than guessed at.
        Assert.Equal(JsonValueKind.Object, dataset.Meta!.Value.ValueKind);
        Assert.Equal(20000, dataset.Meta.Value.GetProperty("rows").GetInt32());
    }

    [Fact]
    public async Task RequestDatasetDownloadAsync_puts_the_id_in_the_body_and_the_format_in_the_query()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"dataset":{"url":"https://example.com/datasets/3.csv"}}}""");

        var download = await harness.Client.Radar.RequestDatasetDownloadAsync(3L, RadarDatasetFormat.Json);

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.Equal("/client/v4/radar/datasets/download", harness.LastRequest.Path);
        Assert.Equal("format=JSON", harness.LastRequest.Query.TrimStart('?'));
        Assert.Equal("""{"datasetId":3}""", harness.LastRequest.Body);
        Assert.Equal("application/json; charset=utf-8", harness.LastRequest.ContentType);
        Assert.Equal("https://example.com/datasets/3.csv", download!.Url);
        Assert.Equal("https://example.com/datasets/3.csv", download.ToString());
    }

    [Fact]
    public async Task RequestDatasetDownloadAsync_reads_the_address_out_of_the_text_cloudflare_answers()
    {
        using var harness = CloudflareHarness.Create();

        // A live run asked for format=CSV and was answered this: one line of text, not the envelope the
        // reference publishes and not JSON at all. It parses to nothing, so the call that worked read as one
        // that answered no address - three datasets in a row, each listed moments before with a title and a
        // type. The address is in the text, so it is read out of the text.
        harness.RespondRaw(
            "Dataset url https://storage.example.test/radar/bucket-1000000.csv?X-Amz-Signature=abc123"u8.ToArray(),
            "text/plain");

        var download = await harness.Client.Radar.RequestDatasetDownloadAsync(3033L, RadarDatasetFormat.Csv);

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.Equal("/client/v4/radar/datasets/download", harness.LastRequest.Path);
        Assert.Equal("format=CSV", harness.LastRequest.Query.TrimStart('?'));
        Assert.Equal("""{"datasetId":3033}""", harness.LastRequest.Body);

        // Whole, signature and all: that is what makes the address usable, and it is the caller's to keep for
        // the hour Cloudflare gave it. An integration run prints less than this on purpose - its annotations
        // are public, and a signature in one is a signature handed out.
        Assert.Equal(
            "https://storage.example.test/radar/bucket-1000000.csv?X-Amz-Signature=abc123",
            download!.Url);
        Assert.Equal(download.Url, download.ToString());
        Assert.True(download.AdditionalData is not { Count: > 0 });
    }

    [Fact]
    public async Task RequestDatasetDownloadAsync_finds_the_address_whatever_separates_it_from_the_words()
    {
        // The run that found the text answer printed it as "Dataset url https://…" and an exact match on that
        // spelling still missed it, so the separator was not the space it looked like - a tab reads as one in a
        // log, and so does a byte-order mark read as nothing. These are the spellings a log cannot tell apart.
        const string expected = "https://storage.example.test/radar/bucket-1000000.csv?X-Amz-Signature=abc123";

        Assert.Equal(expected, await Read($"Dataset url\t{expected}"));
        Assert.Equal(expected, await Read($"dataset URL {expected}"));
        Assert.Equal(expected, await Read($"\uFEFFDataset url {expected}"));
        Assert.Equal(expected, await Read($"Dataset url {expected}\r\n"));
        Assert.Equal(expected, await Read($"Prepared.\nDataset url: {expected}\nExpires in 3600s."));

        // A body that is JSON and answers no url stays no url: the address is read out of text, not invented
        // out of an envelope that did not offer one.
        Assert.Null(await Read("""{"success":true,"errors":[],"result":{"dataset":{}}}"""));
        Assert.Null(await Read("(no dataset)"));

        static async Task<string?> Read(string body)
        {
            using var harness = CloudflareHarness.Create();
            harness.RespondRaw(
                System.Text.Encoding.UTF8.GetBytes(body),
                "text/plain");

            var download = await harness.Client.Radar.RequestDatasetDownloadAsync(
                3033L,
                RadarDatasetFormat.Csv);
            return download?.Url;
        }
    }

    [Fact]
    public async Task RequestDatasetDownloadAsync_refuses_a_format_it_cannot_spell()
    {
        using var harness = CloudflareHarness.Create();

        var error = await Assert.ThrowsAsync<CloudflareConfigurationException>(() =>
            harness.Client.Radar.RequestDatasetDownloadAsync(3L, RadarDatasetFormat.Unknown));

        Assert.Contains("not a dataset format", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task GetAsnAsSetAsync_reads_the_sets_and_the_paths_beside_them()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"as_sets":[{"as_members_count":12,"as_set_members_count":3,"as_set_upstreams_count":4,"asn_cone_size":3400,"irr_sources":["RADB","RIPE"],"name":"AS-CLOUDFLARE","hierarchical_asn":13335,"inferred_asn":13335,"peeringdb_asn":13335,"descr":"Cloudflare AS-SET","as_members":[],"as_set_members":[],"mnt_by":[{"mntner":"MAINT-AS13335","irr":"radb","source":"RADB","descr":"Cloudflare Inc","admin_c":["CFNL-AP"],"tech_c":["CFNL-AP"],"mnt_by":["MAINT-AS13335"],"created":"2019-01-01T00:00:00Z","last_modified":null}],"admin_c":["CFNL-AP"],"tech_c":[],"org":null,"inference_matches":[{"asn":13335,"method":"peeringdb","detail":null}],"is_cf_transit":true}],"paths":[["13335","15169"],["13335","174"]]}}""");

        var asSet = await harness.Client.Radar.GetAsnAsSetAsync(13335L);

        Assert.Equal("/client/v4/radar/entities/asns/13335/as_set", harness.LastRequest.Path);
        Assert.Equal("", harness.LastRequest.Query);
        Assert.NotNull(asSet);

        var set = Assert.Single(asSet!.AsSets!);

        // snake_case here, camelCase on the annotation beside it: radar publishes both, so each name is pinned.
        Assert.Equal("AS-CLOUDFLARE", set.Name);
        Assert.Equal(12L, set.AsMembersCount);
        Assert.Equal(3L, set.AsSetMembersCount);
        Assert.Equal(4L, set.AsSetUpstreamsCount);
        Assert.Equal(3400L, set.AsnConeSize);
        Assert.Equal(new[] { "RADB", "RIPE" }, set.IrrSources!);
        Assert.Equal(13335L, set.HierarchicalAsn);
        Assert.Equal(13335L, set.PeeringdbAsn);
        Assert.Equal("AS-CLOUDFLARE: 12 member(s), cone 3400", set.ToString());

        // Nine fields Cloudflare answers here that its published schema for this endpoint does not list: the
        // IRR record behind the set, and how Cloudflare worked out which ASN it belongs to. They are modelled
        // rather than left in AdditionalData, because they are the interesting half of the answer.
        Assert.Equal("Cloudflare AS-SET", set.Description);
        Assert.Empty(set.AsMembers!);
        Assert.Empty(set.AsSetMembers!);
        Assert.Equal(new[] { "CFNL-AP" }, set.AdminContacts!);
        Assert.Empty(set.TechnicalContacts!);
        Assert.Null(set.Organisation);
        Assert.True(set.IsCloudflareTransit);
        Assert.True(set.AdditionalData is not { Count: > 0 });

        var maintainer = Assert.Single(set.MaintainedBy!);
        Assert.Equal("MAINT-AS13335", maintainer.Maintainer);
        Assert.Equal("radb", maintainer.Irr);
        Assert.Equal("RADB", maintainer.Source);
        Assert.Equal("Cloudflare Inc", maintainer.Description);
        Assert.Equal(new[] { "CFNL-AP" }, maintainer.TechnicalContacts!);
        Assert.Equal("2019-01-01T00:00:00Z", maintainer.Created);
        Assert.Null(maintainer.LastModified);
        Assert.Equal("MAINT-AS13335 (RADB)", maintainer.ToString());

        var inference = Assert.Single(set.InferenceMatches!);
        Assert.Equal(13335L, inference.Asn);
        Assert.Equal("peeringdb", inference.Method);
        Assert.Null(inference.Detail);
        Assert.Equal("AS13335 by peeringdb", inference.ToString());

        // The paths stay nested, because which ASN sits beside which is the information.
        Assert.Equal(2, asSet.Paths!.Count);
        Assert.Equal(new[] { "13335", "15169" }, asSet.Paths[0]);
        Assert.Equal("1 AS-SET(s), 2 path(s)", asSet.ToString());
    }

    [Fact]
    public async Task GetAsnRelationshipsAsync_sends_the_second_asn_and_reads_when_it_was_measured()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"meta":{"data_time":"2026-02-22T00:00:00Z","query_time":"2026-02-23T09:00:00Z","total_peers":3},"rels":[{"asn1":13335,"asn1_country":"US","asn1_name":"Cloudflare","asn2":15169,"asn2_country":"US","asn2_name":"Google","rel":"provider-customer","aspa_validated":true},{"asn1":13335,"asn1_country":"US","asn1_name":"Cloudflare","asn2":402940,"asn2_country":"US","asn2_name":"Interfere","rel":"peer","aspa_validated":false}]}}""");

        var relationships = await harness.Client.Radar.GetAsnRelationshipsAsync(13335L, 15169L);

        Assert.Equal("/client/v4/radar/entities/asns/13335/rel", harness.LastRequest.Path);
        Assert.Equal("asn2=15169", harness.LastRequest.Query.TrimStart('?'));
        Assert.NotNull(relationships);

        Assert.Equal(2, relationships!.Rels!.Count);
        var relation = relationships.Rels[0];
        Assert.Equal(13335L, relation.Asn1);
        Assert.Equal("Cloudflare", relation.Asn1Name);
        Assert.Equal(15169L, relation.Asn2);
        Assert.Equal("US", relation.Asn2Country);

        // Cloudflare publishes rel as a string with no list of values, so it stays one - and a live answer
        // spells it provider-customer and peer, not the upstream/downstream its name suggests.
        Assert.Equal("provider-customer", relation.Rel);
        Assert.Equal("AS13335 - AS15169 (provider-customer)", relation.ToString());

        // Unpublished, and answered as both, so it is a bool rather than the JSON it arrived as.
        Assert.True(relation.AspaValidated);
        Assert.False(relationships.Rels[1].AspaValidated);
        Assert.True(relation.AdditionalData is not { Count: > 0 });

        Assert.Equal(3L, relationships.Meta!.TotalPeers);
        Assert.Equal("2026-02-22T00:00:00Z", relationships.Meta.DataTime);
        Assert.Equal("3 peer(s) as of 2026-02-22T00:00:00Z", relationships.Meta.ToString());
        Assert.Equal("2 relationship(s), 3 peer(s) as of 2026-02-22T00:00:00Z", relationships.ToString());
    }

    [Fact]
    public async Task The_asn_endpoints_answer_null_when_cloudflare_has_no_such_asn()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":false,"errors":[{"code":10007,"message":"ASN not found"}],"messages":[],"result":null}""",
            HttpStatusCode.NotFound);
        harness.Respond(
            """{"success":false,"errors":[{"code":10007,"message":"ASN not found"}],"messages":[],"result":null}""",
            HttpStatusCode.NotFound);

        Assert.Null(await harness.Client.Radar.GetAsnAsSetAsync(64500L));
        Assert.Null(await harness.Client.Radar.GetAsnRelationshipsAsync(64500L));
        Assert.Equal("/client/v4/radar/entities/asns/64500/as_set", harness.Requests[0].Path);
        Assert.Equal("/client/v4/radar/entities/asns/64500/rel", harness.Requests[1].Path);
    }

    [Fact]
    public async Task ListBotnetThreatFeedAsync_sends_the_negated_asns_and_keeps_the_meta_beside_the_ranking()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"ases":[{"asn":3356,"country":"US","name":"Level 3","rank":1,"rankChange":2}],"meta":{"date":"2026-02-23","total":500,"compareDate":"2026-02-16"}}}""");

        var feed = await harness.Client.Radar.ListBotnetThreatFeedAsync(new RadarBotnetThreatFeedParameters
        {
            Asns = new List<string> { "-174", "3356" },
            CompareDateRange = "7d",
            Date = new DateTimeOffset(2026, 2, 23, 0, 0, 0, TimeSpan.Zero),
            Limit = 5,
            Location = "US",
            Metric = RadarBotnetMetric.NumberOfOffendingIps,
            Offset = 10,
            SortOrder = RadarSortOrder.Descending,
        });

        Assert.Equal("/client/v4/radar/entities/asns/botnet_threat_feed", harness.LastRequest.Path);

        // One comma-separated asn parameter, where a leading - excludes rather than includes: that grammar is
        // why the entries are strings.
        Assert.Equal(
            "asn=-174%2C3356&compareDateRange=7d&date=2026-02-23&limit=5&location=US" +
            "&metric=NUMBER_OF_OFFENDING_IPS&offset=10&sortOrder=DESC",
            harness.LastRequest.Query.TrimStart('?'));

        Assert.NotNull(feed);
        var entry = Assert.Single(feed!.Ases!);
        Assert.Equal(3356L, entry.Asn);
        Assert.Equal(1L, entry.Rank);
        Assert.Equal(2L, entry.RankChange);
        Assert.Equal("#1 AS3356 Level 3 (+2)", entry.ToString());
        Assert.Equal("2026-02-23: 500 AS(s) against 2026-02-16", feed.Meta!.ToString());

        // Not a page: the total beside the list is the only total this endpoint offers.
        Assert.Equal("1 ranked AS(s), 2026-02-23: 500 AS(s) against 2026-02-16", feed.ToString());
    }

    [Fact]
    public void The_botnet_filter_of_asns_negates_the_ones_that_are_negative()
    {
        var parameters = RadarBotnetThreatFeedParameters.OfAsns(-174L, 3356L);

        Assert.Equal(new[] { "-174", "3356" }, parameters.Asns!);
    }

    [Theory]
    [InlineData(RadarContinent.Africa, "AF")]
    [InlineData(RadarContinent.Asia, "AS")]
    [InlineData(RadarContinent.Europe, "EU")]
    [InlineData(RadarContinent.NorthAmerica, "NA")]
    [InlineData(RadarContinent.Oceania, "OC")]
    [InlineData(RadarContinent.SouthAmerica, "SA")]
    public void Every_continent_is_sent_as_the_code_radar_publishes(RadarContinent continent, string expected) =>
        Assert.Equal(expected, CloudflareEnumConverter<RadarContinent>.ToWire(continent));

    [Theory]
    [InlineData(RadarSearchCategory.AdministrativeDivisions, "ADM1S")]
    [InlineData(RadarSearchCategory.Asns, "ASNS")]
    [InlineData(RadarSearchCategory.Bots, "BOTS")]
    [InlineData(RadarSearchCategory.CertificateAuthorities, "CERTIFICATE_AUTHORITIES")]
    [InlineData(RadarSearchCategory.CertificateLogs, "CERTIFICATE_LOGS")]
    [InlineData(RadarSearchCategory.Origins, "ORIGINS")]
    [InlineData(RadarSearchCategory.OriginRegions, "ORIGIN_REGIONS")]
    [InlineData(RadarSearchCategory.Industries, "INDUSTRIES")]
    [InlineData(RadarSearchCategory.Locations, "LOCATIONS")]
    [InlineData(RadarSearchCategory.Notebooks, "NOTEBOOKS")]
    [InlineData(RadarSearchCategory.TopLevelDomains, "TLDS")]
    [InlineData(RadarSearchCategory.Verticals, "VERTICALS")]
    public void Every_search_category_is_sent_as_the_spelling_radar_publishes(RadarSearchCategory category, string expected) =>
        Assert.Equal(expected, CloudflareEnumConverter<RadarSearchCategory>.ToWire(category));
    [Fact]
    public void An_as_set_reads_whatever_json_is_cf_transit_arrives_as()
    {
        // Cloudflare answers the field and does not publish it, and no answer has shown its value - so it is
        // read as JSON and offered as a bool that answers null for anything it cannot read. A typed bool would
        // fail the whole as_set answer over one field nobody has seen.
        Assert.True(Read("true").IsCloudflareTransit);
        Assert.False(Read("false").IsCloudflareTransit);
        Assert.True(Read("1").IsCloudflareTransit);
        Assert.False(Read("0").IsCloudflareTransit);
        Assert.True(Read("\"true\"").IsCloudflareTransit);
        Assert.Null(Read("\"perhaps\"").IsCloudflareTransit);
        Assert.Null(Read("null").IsCloudflareTransit);
        Assert.Null(Read("""{"some":"object"}""").IsCloudflareTransit);

        static RadarAsSet Read(string json) => JsonSerializer.Deserialize<RadarAsSet>(
            $"{{\"name\":\"AS-TEST\",\"is_cf_transit\":{json}}}",
            CloudflareJson.Default)!;
    }

    [Fact]
    public async Task GetTopDomainsAsync_sends_the_filters_in_the_order_radar_publishes_them()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"meta":{"confidenceInfo":null,"dateRange":[{"startTime":"2026-11-02T00:00:00Z","endTime":"2026-11-09T00:00:00Z"}],"lastUpdated":"2026-11-09T05:00:00Z","normalization":"PERCENTAGE","top_0":{"date":"2026-11-02"},"units":[{"name":"rank","value":"position"}]},"top_0":[{"domain":"google.com","rank":1,"categories":[{"id":18,"name":"Technology","superCategoryId":3}],"pctRankChange":0.5},{"domain":"cloudflare.com","rank":4211,"categories":[]}],"top_1":[{"domain":"example.com","rank":7,"categories":[]}]}}""");

        var top = await harness.Client.Radar.GetTopDomainsAsync(new RadarRankingTopParameters
        {
            Date = new List<DateTimeOffset> { new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero) },
            DomainCategory = new List<string> { "18" },
            Limit = 2,
            Location = new List<string> { "US", "GB" },
            Name = new List<string> { "first", "second" },
            RankingType = RadarRankingType.TrendingRise,
        });

        var request = harness.LastRequest;
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/client/v4/radar/ranking/top", request.Path);
        Assert.Equal(
            "date=2026-11-02&domainCategory=18&limit=2&location=US&location=GB&name=first&name=second" +
            "&rankingType=TRENDING_RISE",
            request.Query.TrimStart('?'));

        // The first series is typed. The second arrives where Cloudflare puts anything this library has no
        // published shape for, and SeriesNames is what tells a caller to look for it.
        Assert.Equal(2, top!.Top0!.Count);
        Assert.Equal("google.com", top.Top0[0].Domain);
        Assert.Equal(1L, top.Top0[0].Rank);
        Assert.Equal(0.5, top.Top0[0].PercentRankChange);
        Assert.Equal("1 google.com (+0.5%)", top.Top0[0].ToString());

        var category = Assert.Single(top.Top0[0].Categories!);
        Assert.Equal(18L, category.Id);
        Assert.Equal("Technology", category.Name);
        Assert.Equal(3L, category.SuperCategoryId);
        Assert.Equal("18 Technology", category.ToString());
        Assert.Equal("4211 cloudflare.com", top.Top0[1].ToString());
        Assert.Equal(new[] { "top_1" }, top.SeriesNames);
        Assert.Equal(
            "2 domain(s), 2026-11-02 over 2026-11-02T00:00:00Z to 2026-11-09T00:00:00Z, " +
            "updated 2026-11-09T05:00:00Z, and top_1",
            top.ToString());

        // The top lists publish confidenceInfo as nullable, and this one answered it as null.
        Assert.Null(top.Meta!.ConfidenceInfo);
        Assert.Equal("2026-11-02", top.Meta.Top0!.Date);
        Assert.Null(top.Meta.Top0.ServiceCategory);
        Assert.Equal("PERCENTAGE", top.Meta.Normalization);
        Assert.Equal(RadarNormalizations.Percentage, top.Meta.Normalization);
        Assert.Equal("rank=position", Assert.Single(top.Meta.Units!).ToString());
        Assert.Equal(
            "2026-11-02T00:00:00Z to 2026-11-09T00:00:00Z",
            Assert.Single(top.Meta.DateRange!).ToString());
        Assert.Equal(
            "2026-11-02 over 2026-11-02T00:00:00Z to 2026-11-09T00:00:00Z, updated 2026-11-09T05:00:00Z",
            top.Meta.ToString());
    }

    [Fact]
    public async Task GetTopDomainsAsync_refuses_a_limit_that_asks_for_nothing_and_a_ranking_it_cannot_spell()
    {
        using var harness = CloudflareHarness.Create();

        var error = await Assert.ThrowsAsync<CloudflareConfigurationException>(() =>
            harness.Client.Radar.GetTopDomainsAsync(new RadarRankingTopParameters
            {
                Limit = 0,
                RankingType = RadarRankingType.Unknown,
            }));

        Assert.Equal(2, error.Problems.Count);
        Assert.Contains("at least one entry", error.Problems[0], StringComparison.Ordinal);
        Assert.Contains("not a radar ranking type", error.Problems[1], StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task GetTopDomainTimeseriesGroupsAsync_leaves_the_series_named_as_the_caller_named_them()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"meta":{"aggInterval":"ONE_DAY","confidenceInfo":{"annotations":[{"dataSource":"HTTP","description":"Something moved","endDate":"2026-11-03","eventType":"TRAFFIC_ANOMALY","isInstantaneous":false,"linkedUrl":"https://radar.cloudflare.com/","startDate":"2026-11-02","tags":["dns"]}],"level":0.85},"dateRange":[{"startTime":"2026-11-02T00:00:00Z","endTime":"2026-11-09T00:00:00Z"}],"lastUpdated":"2026-11-09T05:00:00Z","normalization":"PERCENTAGE","units":[{"name":"rank","value":"position"}]},"serie_0":{"timestamps":["2026-11-02T00:00:00Z","2026-11-03T00:00:00Z"],"google.com":[1,2],"cloudflare.com":["a","b"]},"serie_1":{"timestamps":["2026-11-04T00:00:00Z"],"google.com":[3]}}}""");

        var groups = await harness.Client.Radar.GetTopDomainTimeseriesGroupsAsync(
            new RadarRankingTimeseriesGroupsParameters
            {
                DateStart = new List<DateTimeOffset> { new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero) },
                DateEnd = new List<DateTimeOffset> { new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero) },
                DateRange = new List<string> { "7d", "7dcontrol" },
                Domains = new List<string> { "google.com", "cloudflare.com" },
                Limit = 10,
                Location = new List<string> { "US" },
                Name = new List<string> { "one" },
                RankingType = RadarRankingType.Popular,
            });

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Equal("/client/v4/radar/ranking/timeseries_groups", harness.LastRequest.Path);
        Assert.Equal(
            "dateEnd=2026-11-09T00%3A00%3A00Z&dateRange=7d&dateRange=7dcontrol" +
            "&dateStart=2026-11-02T00%3A00%3A00Z&domains=google.com&domains=cloudflare.com&limit=10" +
            "&location=US&name=one&rankingType=POPULAR",
            harness.LastRequest.Query.TrimStart('?'));

        // The value series are the caller's own names, so they cannot be typed - and one of them answers
        // strings, which is why they are left as the JSON they arrived as.
        var serie = groups!.Serie0!;
        Assert.Equal(2, serie.Count);
        Assert.Equal(new[] { "2026-11-02T00:00:00Z", "2026-11-03T00:00:00Z" }, serie.Timestamps!);
        Assert.Equal(new[] { "google.com", "cloudflare.com" }, serie.SeriesNames);
        Assert.Equal("2 timestamp(s), 2 series: google.com, cloudflare.com", serie.ToString());
        Assert.Equal(1, serie.AdditionalData!["google.com"][0].GetInt32());
        Assert.Equal("a", serie.AdditionalData["cloudflare.com"][0].GetString());

        // A second group is not published, so it is not typed - but it is not lost either.
        Assert.Equal(new[] { "serie_1" }, groups.SeriesNames);

        Assert.Equal("ONE_DAY", groups.Meta!.AggInterval);
        Assert.Equal(RadarAggregationIntervals.OneDay, groups.Meta.AggInterval);
        Assert.Equal(0.85, groups.Meta.ConfidenceInfo!.Level);
        Assert.Equal("level 0.85, 1 annotation(s)", groups.Meta.ConfidenceInfo.ToString());

        // The annotations a confidence report carries are the ones the annotation list answers, with fewer of
        // their fields filled in, so they are read as the same type - isInstantaneous included.
        var annotation = Assert.Single(groups.Meta.ConfidenceInfo.Annotations!);
        Assert.False(annotation.IsInstantaneous);
        Assert.Equal("HTTP", annotation.DataSource);
        Assert.Equal("TRAFFIC_ANOMALY", annotation.EventType);
        Assert.Equal(new[] { "dns" }, annotation.Tags!);
        Assert.True(annotation.AdditionalData is not { Count: > 0 });

        Assert.Equal(
            "ONE_DAY PERCENTAGE over 2026-11-02T00:00:00Z to 2026-11-09T00:00:00Z " +
            "(level 0.85, 1 annotation(s))",
            groups.Meta.ToString());
        Assert.Equal(
            "2 timestamp(s), 2 series: google.com, cloudflare.com, ONE_DAY PERCENTAGE over " +
            "2026-11-02T00:00:00Z to 2026-11-09T00:00:00Z (level 0.85, 1 annotation(s))",
            groups.ToString());
    }

    [Fact]
    public async Task GetDomainRankingAsync_puts_the_domain_in_the_path_and_asks_for_the_places_when_told_to()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"details_0":{"categories":[{"id":18,"name":"Technology","superCategoryId":3}],"bucket":"top_1000000","rank":4211,"top_locations":[{"locationCode":"US","locationName":"United States","rank":1}]},"meta":{"dateRange":[{"startTime":"2026-11-02","endTime":"2026-11-09"}]}}}""");

        var ranking = await harness.Client.Radar.GetDomainRankingAsync(
            "cloudflare.com",
            new RadarDomainRankingParameters
            {
                Date = new List<DateTimeOffset> { new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero) },
                IncludeTopLocations = true,
                Limit = 1,
                Name = new List<string> { "one" },
                RankingType = RadarRankingType.Popular,
            });

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Equal("/client/v4/radar/ranking/domain/cloudflare.com", harness.LastRequest.Path);
        Assert.Equal(
            "date=2026-11-02&includeTopLocations=true&limit=1&name=one&rankingType=POPULAR",
            harness.LastRequest.Query.TrimStart('?'));

        // Every field but the categories is optional, and a domain outside the ranked buckets answers no rank.
        Assert.Equal(4211L, ranking!.Details0!.Rank);
        Assert.Equal("top_1000000", ranking.Details0.Bucket);
        Assert.Equal("rank 4211 in top_1000000, 18 Technology", ranking.Details0.ToString());

        var place = Assert.Single(ranking.Details0.TopLocations!);
        Assert.Equal("US", place.LocationCode);
        Assert.Equal("United States", place.LocationName);
        Assert.Equal(1L, place.Rank);
        Assert.Equal("1 US", place.ToString());

        Assert.Equal("2026-11-02 to 2026-11-09", Assert.Single(ranking.Meta!.DateRange!).ToString());
        Assert.Equal(
            "rank 4211 in top_1000000, 18 Technology, 2026-11-02 to 2026-11-09",
            ranking.ToString());
    }

    [Fact]
    public async Task GetDomainRankingAsync_needs_a_domain_to_ask_about()
    {
        using var harness = CloudflareHarness.Create();

        var error = await Assert.ThrowsAsync<CloudflareConfigurationException>(() =>
            harness.Client.Radar.GetDomainRankingAsync(" "));

        Assert.Contains("'domain' must not be null or empty", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task ListInternetServiceCategoriesAsync_reads_the_names_a_service_ranking_is_filtered_by()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"categories_0":[{"name":"Cloud Computing"},{"name":"Search Engines"}],"categories_1":[{"name":"Streaming"}]}}""");

        var categories = await harness.Client.Radar.ListInternetServiceCategoriesAsync(
            new RadarInternetServiceCategoriesParameters { Limit = 2 });

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Equal("/client/v4/radar/ranking/internet_services/categories", harness.LastRequest.Path);
        Assert.Equal("limit=2", harness.LastRequest.Query.TrimStart('?'));
        Assert.Equal(
            new[] { "Cloud Computing", "Search Engines" },
            categories!.Categories0!.Select(category => category.Name!));
        Assert.Equal("2 category(ies)", categories.ToString());
        Assert.True(categories.AdditionalData!.ContainsKey("categories_1"));
    }

    [Fact]
    public async Task GetTopInternetServicesAsync_names_the_category_each_series_is_for()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"meta":{"confidenceInfo":null,"dateRange":[{"startTime":"2026-11-02","endTime":"2026-11-09"}],"lastUpdated":"2026-11-09T05:00:00Z","normalization":"PERCENTAGE","top_0":{"date":"2026-11-02","serviceCategory":"Cloud Computing"},"units":[{"name":"rank","value":"position"}]},"top_0":[{"rank":1,"service":"AWS"},{"rank":2,"service":"Google Cloud"}]}}""");

        var top = await harness.Client.Radar.GetTopInternetServicesAsync(new RadarInternetServiceTopParameters
        {
            Date = new List<DateTimeOffset> { new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero) },
            Limit = 2,
            ServiceCategory = new List<string> { "Cloud Computing" },
        });

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Equal("/client/v4/radar/ranking/internet_services/top", harness.LastRequest.Path);
        Assert.Equal(
            "date=2026-11-02&limit=2&serviceCategory=Cloud%20Computing",
            harness.LastRequest.Query.TrimStart('?'));

        Assert.Equal(2, top!.Top0!.Count);
        Assert.Equal(1L, top.Top0[0].Rank);
        Assert.Equal("AWS", top.Top0[0].Service);
        Assert.Equal("1 AWS", top.Top0[0].ToString());
        Assert.Equal("2026-11-02 (Cloud Computing)", top.Meta!.Top0!.ToString());
        Assert.Empty(top.SeriesNames);
        Assert.Equal(
            "2 service(s), 2026-11-02 (Cloud Computing) over 2026-11-02 to 2026-11-09, " +
            "updated 2026-11-09T05:00:00Z",
            top.ToString());
    }

    [Fact]
    public async Task GetInternetServiceTimeseriesGroupsAsync_reads_a_level_that_arrives_as_a_whole_number()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"meta":{"aggInterval":"ONE_HOUR","confidenceInfo":{"annotations":[],"level":95},"dateRange":[],"lastUpdated":"2026-11-09T05:00:00Z","normalization":"RAW_VALUES","units":[]},"serie_0":{"timestamps":["2026-11-09T00:00:00Z"],"AWS":[12]}}}""");

        var groups = await harness.Client.Radar.GetInternetServiceTimeseriesGroupsAsync(
            new RadarInternetServiceTimeseriesGroupsParameters
            {
                DateRange = new List<string> { "1d" },
                Name = new List<string> { "one", "two" },
                ServiceCategory = new List<string> { "Cloud Computing" },
            });

        Assert.Equal(
            "/client/v4/radar/ranking/internet_services/timeseries_groups",
            harness.LastRequest.Path);
        Assert.Equal(
            "dateRange=1d&name=one&name=two&serviceCategory=Cloud%20Computing",
            harness.LastRequest.Query.TrimStart('?'));

        // Cloudflare publishes level as a number with no range: one endpoint answered a fraction and this one
        // a whole number, so it is a double - an int would read 95 and fail on 0.85.
        Assert.Equal("ONE_HOUR", groups!.Meta!.AggInterval);
        Assert.Equal(RadarNormalizations.RawValues, groups.Meta.Normalization);
        Assert.Equal(95d, groups.Meta.ConfidenceInfo!.Level);
        Assert.Equal("level 95", groups.Meta.ConfidenceInfo.ToString());
        Assert.Empty(groups.Meta.ConfidenceInfo.Annotations!);
        Assert.Empty(groups.Meta.DateRange!);
        Assert.Equal(12, groups.Serie0!.AdditionalData!["AWS"][0].GetInt32());
        Assert.Equal("ONE_HOUR RAW_VALUES (level 95)", groups.Meta.ToString());
    }

    [Fact]
    public void Every_ranking_type_is_spelled_as_radar_publishes_it()
    {
        Assert.Equal(
            new[] { "POPULAR", "TRENDING_RISE", "TRENDING_STEADY" },
            CloudflareEnumConverter<RadarRankingType>.KnownValues);
    }

    [Fact]
    public void Every_annotation_filter_is_spelled_as_radar_publishes_it()
    {
        Assert.Equal(
            new[]
            {
                "ALL", "AI_BOTS", "AI_GATEWAY", "BGP", "BOTS", "CONNECTION_ANOMALY", "CT", "DNS",
                "DNS_MAGNITUDE", "DNS_AS112", "DOS", "EMAIL_ROUTING", "EMAIL_SECURITY", "FW", "FW_PG", "HTTP",
                "HTTP_CONTROL", "HTTP_CRAWLER_REFERER", "HTTP_ORIGINS", "IQI", "LEAKED_CREDENTIALS", "NET",
                "ROBOTS_TXT", "SPEED", "WORKERS_AI",
            },
            CloudflareEnumConverter<RadarAnnotationDataSource>.KnownValues);

        Assert.Equal(
            new[] { "EVENT", "GENERAL", "OUTAGE", "PARTIAL_PROJECTION", "PIPELINE", "TRAFFIC_ANOMALY" },
            CloudflareEnumConverter<RadarAnnotationEventType>.KnownValues);

        Assert.Equal(
            new[]
            {
                "BLOCKING", "CABLE_CUT", "CYBERATTACK", "DNS", "FIRE", "GOVERNMENT_DIRECTED", "MAINTENANCE",
                "MECHANICAL", "MILITARY_ACTION", "MISCONFIGURATION", "NATURAL_DISASTER", "NETWORK_PROBLEM",
                "POWER_OUTAGE", "SOFTWARE", "TECHNICAL_PROBLEM", "UNKNOWN", "WEATHER",
            },
            CloudflareEnumConverter<RadarAnnotationOutageCause>.KnownValues);

        Assert.Equal(
            new[] { "NATIONWIDE", "REGIONAL", "NETWORK", "PLATFORM" },
            CloudflareEnumConverter<RadarAnnotationOutageType>.KnownValues);
    }

    [Fact]
    public void Every_dataset_and_ranking_filter_is_spelled_as_radar_publishes_it()
    {
        Assert.Equal(
            new[] { "RANKING_BUCKET", "REPORT" },
            CloudflareEnumConverter<RadarDatasetType>.KnownValues);

        // The one format radar models, because the answer stays a JSON envelope carrying a URL.
        Assert.Equal(
            new[] { "JSON", "CSV" },
            CloudflareEnumConverter<RadarDatasetFormat>.KnownValues);

        Assert.Equal(
            new[] { "OFFENSE_COUNT", "NUMBER_OF_OFFENDING_IPS" },
            CloudflareEnumConverter<RadarBotnetMetric>.KnownValues);

        Assert.Equal(
            new[] { "ASC", "DESC" },
            CloudflareEnumConverter<RadarSortOrder>.KnownValues);
    }

    [Fact]
    public async Task ListBotsAsync_sends_its_filters_singly_and_pages_with_offset()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"bots":[{"category":"SEARCH_ENGINE_CRAWLER","description":"Google's search crawler","kind":"BOT","name":"Googlebot","operator":"Google","slug":"googlebot","userAgentPatterns":["Googlebot/*"]},{"category":"AI_CRAWLER","description":"Fetches pages for training","kind":"BOT","name":"GPTBot","operator":"OpenAI","slug":"gptbot","userAgentPatterns":["GPTBot/*"]}]}}""");

        var bots = await harness.Client.Radar.ListBotsAsync(new RadarBotListParameters
        {
            BotCategory = RadarBotCategory.AiCrawler,
            BotOperator = "Google",
            BotVerificationStatus = RadarBotVerificationStatus.Verified,
            Kind = RadarBotKind.Bot,
            Limit = 2,
            Offset = 4,
        });

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Equal("/client/v4/radar/bots", harness.LastRequest.Path);

        // The list takes its filters singly and calls the kind filter kind, where the summaries beside it take
        // lists of them and call it botKind. Both are Cloudflare's own spellings for the same idea.
        Assert.Equal(
            "botCategory=AI_CRAWLER&botOperator=Google&botVerificationStatus=VERIFIED&kind=BOT&limit=2&offset=4",
            harness.LastRequest.Query.TrimStart('?'));

        Assert.Equal(2, bots.Count);
        var first = bots.Items[0];
        Assert.Equal("Googlebot", first.Name);
        Assert.Equal("googlebot", first.Slug);
        Assert.Equal("SEARCH_ENGINE_CRAWLER", first.Category);
        Assert.Equal("BOT", first.Kind);
        Assert.Equal("Google", first.Operator);
        Assert.Equal(new[] { "Googlebot/*" }, first.UserAgentPatterns!);
        Assert.Equal("Googlebot (BOT) by Google", first.ToString());
        Assert.True(first.AdditionalData is not { Count: > 0 });

        // Three fields only the single-bot read answers, so a list entry carries none of them.
        Assert.Null(first.OperatorUrl);
        Assert.Null(first.UserAgents);
        Assert.Null(first.SignatureAgentUrl);
    }

    [Fact]
    public async Task ListBotsAsync_refuses_a_category_it_cannot_spell()
    {
        using var harness = CloudflareHarness.Create();

        var error = await Assert.ThrowsAsync<CloudflareConfigurationException>(() =>
            harness.Client.Radar.ListBotsAsync(new RadarBotListParameters
            {
                BotCategory = RadarBotCategory.Unknown,
            }));

        Assert.Contains("not a bot category", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task ListAllBotsAsync_walks_by_offset_and_stops_on_a_short_page()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"bots":[{"slug":"googlebot"},{"slug":"bingbot"}]}}""");
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"bots":[{"slug":"gptbot"}]}}""");

        var seen = new List<string?>();
        await foreach (var bot in harness.Client.Radar.ListAllBotsAsync(pageSize: 2))
        {
            seen.Add(bot.Slug);
        }

        Assert.Equal(new[] { "googlebot", "bingbot", "gptbot" }, seen);
        Assert.Equal(2, harness.RequestCount);
        Assert.Equal("limit=2&offset=2", harness.LastRequest.Query.TrimStart('?'));
    }

    [Fact]
    public async Task GetBotAsync_reads_the_three_fields_only_the_single_read_answers()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"bot":{"category":"AI_CRAWLER","description":"Fetches pages for training","kind":"BOT","name":"GPTBot","operator":"OpenAI","operatorUrl":"https://openai.com/gptbot","slug":"gptbot","userAgentPatterns":["GPTBot/*"],"userAgents":["GPTBot/1.0"],"signatureAgentUrl":null}}}""");

        var bot = await harness.Client.Radar.GetBotAsync("gptbot");

        Assert.Equal("/client/v4/radar/bots/gptbot", harness.LastRequest.Path);
        Assert.Equal("", harness.LastRequest.Query);
        Assert.Equal("GPTBot", bot!.Name);
        Assert.Equal("https://openai.com/gptbot", bot.OperatorUrl);
        Assert.Equal(new[] { "GPTBot/1.0" }, bot.UserAgents!);

        // Published as nullable, and answered as null here: not every bot signs its requests.
        Assert.Null(bot.SignatureAgentUrl);
        Assert.True(bot.AdditionalData is not { Count: > 0 });
    }

    [Fact]
    public async Task GetBotAsync_reports_a_slug_radar_does_not_have_as_null()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":false,"errors":[{"code":12004,"message":"Bot not found"}],"messages":[],"result":null}""",
            HttpStatusCode.NotFound);

        var bot = await harness.Client.Radar.GetBotAsync("no-such-bot");

        // A slug nobody has is an answer, not a failure: the caller asked whether radar knows the bot.
        Assert.Null(bot);
        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task GetBotSummaryAsync_puts_the_dimension_in_the_path_and_reads_the_dictionary()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"meta":{"confidenceInfo":{"annotations":[],"level":0.9},"dateRange":[{"startTime":"2026-11-02T00:00:00Z","endTime":"2026-11-09T00:00:00Z"}],"lastUpdated":"2026-11-09T05:00:00Z","normalization":"PERCENTAGE","units":[{"name":"bot_kind","value":"percentage"}]},"summary_0":{"BOT":"63.65","AGENT":"1.20"}}}""");

        var summary = await harness.Client.Radar.GetBotSummaryAsync(
            RadarBotDimension.BotKind,
            new RadarBotSummaryParameters
            {
                Asn = new List<string> { "13335" },
                BotCategory = new List<RadarBotCategory>
                {
                    RadarBotCategory.AiCrawler,
                    RadarBotCategory.SearchEngineCrawler,
                },
                DateRange = new List<string> { "7d" },
                LimitPerGroup = 5,
                Location = new List<string> { "US" },
                Name = new List<string> { "one" },
            });

        Assert.Equal("/client/v4/radar/bots/summary/BOT_KIND", harness.LastRequest.Path);
        Assert.Equal(
            "asn=13335&botCategory=AI_CRAWLER&botCategory=SEARCH_ENGINE_CRAWLER&dateRange=7d" +
            "&limitPerGroup=5&location=US&name=one",
            harness.LastRequest.Query.TrimStart('?'));

        // A summary is one number per value of the dimension, and the keys are the dimension's own values, so
        // it is the dictionary Cloudflare publishes rather than a list this library would have to name.
        Assert.Equal(2, summary!.Summary0!.Count);
        Assert.Equal("63.65", summary.Summary0["BOT"]);
        Assert.Equal("1.20", summary.Summary0["AGENT"]);
        Assert.Empty(summary.SeriesNames);
        Assert.Equal(0.9, summary.Meta!.ConfidenceInfo!.Level);
        Assert.Equal(RadarNormalizations.Percentage, summary.Meta.Normalization);
        Assert.Equal(
            "over 2026-11-02T00:00:00Z to 2026-11-09T00:00:00Z, updated 2026-11-09T05:00:00Z",
            summary.Meta.ToString());
        Assert.Equal(
            "2 value(s), over 2026-11-02T00:00:00Z to 2026-11-09T00:00:00Z, updated 2026-11-09T05:00:00Z",
            summary.ToString());
    }

    [Fact]
    public async Task GetBotSummaryAsync_refuses_a_dimension_it_cannot_put_in_a_path()
    {
        using var harness = CloudflareHarness.Create();

        var error = await Assert.ThrowsAsync<CloudflareConfigurationException>(() =>
            harness.Client.Radar.GetBotSummaryAsync(RadarBotDimension.Unknown));

        Assert.Contains("not a radar bot dimension", error.Message, StringComparison.Ordinal);
        Assert.Contains("no path to put it in", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task GetBotTimeseriesAsync_reads_the_groups_by_the_names_cloudflare_gave_them()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"meta":{"aggInterval":"ONE_DAY","confidenceInfo":null,"dateRange":[],"lastUpdated":"2026-11-09T05:00:00Z","normalization":"RAW_VALUES","units":[{"name":"requests","value":"count"}]},"googlebot":{"timestamps":["2026-11-08T00:00:00Z","2026-11-09T00:00:00Z"],"requests":[100,120]},"gptbot":{"timestamps":["2026-11-08T00:00:00Z"],"requests":[7]}}}""");

        var series = await harness.Client.Radar.GetBotTimeseriesAsync(new RadarBotTimeseriesParameters
        {
            AggInterval = RadarAggregationInterval.OneDay,
            Bot = new List<string> { "googlebot", "gptbot" },
            DateRange = new List<string> { "7d" },
        });

        Assert.Equal("/client/v4/radar/bots/timeseries", harness.LastRequest.Path);

        // Asked for as 1d, answered as ONE_DAY: one idea, two spellings, one on each side of the call.
        Assert.Equal(
            "aggInterval=1d&bot=googlebot&bot=gptbot&dateRange=7d",
            harness.LastRequest.Query.TrimStart('?'));
        Assert.Equal("ONE_DAY", series!.Meta!.AggInterval);
        Assert.Equal(RadarAggregationIntervals.OneDay, series.Meta.AggInterval);

        // The groups are named after what was filtered by, so none of them is published and all of them are
        // read by name.
        Assert.Equal(new[] { "googlebot", "gptbot" }, series.GroupNames);
        Assert.Equal(2, series.Group("googlebot")!.Count);
        Assert.Equal(120, series.Group("googlebot")!.AdditionalData!["requests"][1].GetInt32());
        Assert.Equal(7, series.Group("gptbot")!.AdditionalData!["requests"][0].GetInt32());
        Assert.Null(series.Group("no-such-bot"));
        // A series' meta says its interval, its normalization, its period and its confidence, and nothing
        // else - the update time is on it but not in what it prints, which is the shape the rankings set.
        Assert.Equal("2 group(s): googlebot, gptbot, ONE_DAY RAW_VALUES", series.ToString());
    }

    [Fact]
    public async Task GetBotTimeseriesGroupsAsync_sends_every_filter_in_the_order_cloudflare_publishes_them()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"meta":{"aggInterval":"ONE_DAY","confidenceInfo":null,"dateRange":[],"lastUpdated":"2026-11-09T05:00:00Z","normalization":"PERCENTAGE","units":[]},"serie_0":{"timestamps":["2026-11-08T00:00:00Z"],"AI_CRAWLER":[12],"SEARCH_ENGINE_CRAWLER":[88]}}}""");

        var groups = await harness.Client.Radar.GetBotTimeseriesGroupsAsync(
            RadarBotDimension.BotCategory,
            new RadarBotTimeseriesGroupsParameters
            {
                AggInterval = RadarAggregationInterval.OneDay,
                Asn = new List<string> { "13335" },
                Bot = new List<string> { "googlebot" },
                BotCategory = new List<RadarBotCategory> { RadarBotCategory.AiCrawler },
                BotKind = new List<RadarBotKind> { RadarBotKind.Bot },
                BotOperator = new List<string> { "Google" },
                BotVerificationStatus = new List<RadarBotVerificationStatus>
                {
                    RadarBotVerificationStatus.Verified,
                },
                Continent = new List<string> { "EU" },
                DateEnd = new List<DateTimeOffset> { new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero) },
                DateRange = new List<string> { "7d", "7dcontrol" },
                DateStart = new List<DateTimeOffset> { new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero) },
                LimitPerGroup = 3,
                Location = new List<string> { "US", "GB" },
                Name = new List<string> { "one", "two" },
            });

        Assert.Equal("/client/v4/radar/bots/timeseries_groups/BOT_CATEGORY", harness.LastRequest.Path);
        Assert.Equal(
            "aggInterval=1d&asn=13335&bot=googlebot&botCategory=AI_CRAWLER&botKind=BOT&botOperator=Google" +
            "&botVerificationStatus=VERIFIED&continent=EU&dateEnd=2026-11-09T00%3A00%3A00Z&dateRange=7d" +
            "&dateRange=7dcontrol&dateStart=2026-11-02T00%3A00%3A00Z&limitPerGroup=3&location=US" +
            "&location=GB&name=one&name=two",
            harness.LastRequest.Query.TrimStart('?'));

        // The same timeseries_groups shape the rankings answer, so the same type reads it: the value series
        // are named after the categories this call asked about, and stay the JSON they arrived as.
        Assert.Equal(new[] { "AI_CRAWLER", "SEARCH_ENGINE_CRAWLER" }, groups!.Serie0!.SeriesNames);
        Assert.Equal(88, groups.Serie0.AdditionalData!["SEARCH_ENGINE_CRAWLER"][0].GetInt32());
        Assert.Equal("1 timestamp(s), 2 series: AI_CRAWLER, SEARCH_ENGINE_CRAWLER", groups.Serie0.ToString());
    }

    [Fact]
    public async Task GetWebCrawlerSummaryAsync_reads_the_client_types_a_crawler_answered_for()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"meta":{"confidenceInfo":null,"dateRange":[{"startTime":"2026-11-08","endTime":"2026-11-09"}],"lastUpdated":"2026-11-09T05:00:00Z","normalization":"PERCENTAGE","units":[{"name":"client_type","value":"percentage"}]},"summary_0":{"AI_BOT":"11.4","HUMAN":"80.0","NON_AI_BOT":"8.6"}}}""");

        var summary = await harness.Client.Radar.GetWebCrawlerSummaryAsync(
            RadarWebCrawlerDimension.ClientType,
            new RadarWebCrawlerSummaryParameters
            {
                BotOperator = new List<string> { "Google" },
                ClientType = new List<RadarBotClientType> { RadarBotClientType.AiBot, RadarBotClientType.Human },
                DateRange = new List<string> { "1d" },
                Industry = new List<string> { "News" },
                LimitPerGroup = 4,
                Name = new List<string> { "one" },
                ResponseStatus = new List<string> { "200", "404" },
                ResponseStatusCategory = new List<RadarResponseStatusCategory>
                {
                    RadarResponseStatusCategory.Success,
                    RadarResponseStatusCategory.ClientError,
                },
                Vertical = new List<string> { "search" },
            });

        Assert.Equal("/client/v4/radar/bots/crawlers/summary/CLIENT_TYPE", harness.LastRequest.Path);
        Assert.Equal(
            "botOperator=Google&clientType=AI_BOT&clientType=HUMAN&dateRange=1d&industry=News" +
            "&limitPerGroup=4&name=one&responseStatus=200&responseStatus=404" +
            "&responseStatusCategory=SUCCESS&responseStatusCategory=CLIENT_ERROR&vertical=search",
            harness.LastRequest.Query.TrimStart('?'));

        Assert.Equal(3, summary!.Summary0!.Count);
        Assert.Equal("11.4", summary.Summary0["AI_BOT"]);
        Assert.Equal("3 value(s), over 2026-11-08 to 2026-11-09, updated 2026-11-09T05:00:00Z", summary.ToString());
    }

    [Fact]
    public async Task GetWebCrawlerTimeseriesGroupsAsync_asks_for_one_of_the_three_normalizations()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"meta":{"aggInterval":"ONE_HOUR","confidenceInfo":null,"dateRange":[],"lastUpdated":"2026-11-09T05:00:00Z","normalization":"PERCENTAGE_CHANGE","units":[]},"serie_0":{"timestamps":["2026-11-09T00:00:00Z"],"SUCCESS":[1.5]}}}""");

        var groups = await harness.Client.Radar.GetWebCrawlerTimeseriesGroupsAsync(
            RadarWebCrawlerDimension.ResponseStatusCategory,
            new RadarWebCrawlerTimeseriesGroupsParameters
            {
                AggInterval = RadarAggregationInterval.OneHour,
                ClientType = new List<RadarBotClientType> { RadarBotClientType.NonAiBot },
                DateRange = new List<string> { "1d" },
                Normalization = RadarTimeseriesNormalization.PercentageChange,
            });

        Assert.Equal(
            "/client/v4/radar/bots/crawlers/timeseries_groups/RESPONSE_STATUS_CATEGORY",
            harness.LastRequest.Path);
        Assert.Equal(
            "aggInterval=1h&clientType=NON_AI_BOT&dateRange=1d&normalization=PERCENTAGE_CHANGE",
            harness.LastRequest.Query.TrimStart('?'));

        // Cloudflare reports eight normalizations and lets a caller ask for three, which is why the two lists
        // are kept apart rather than merged into one enum.
        Assert.Equal("PERCENTAGE_CHANGE", groups!.Meta!.Normalization);
        Assert.Equal(1.5, groups.Serie0!.AdditionalData!["SUCCESS"][0].GetDouble());
    }

    [Fact]
    public async Task GetTopVerifiedBotsAsync_reads_the_share_of_traffic_each_one_accounted_for()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"meta":{"confidenceInfo":null,"dateRange":[{"startTime":"2026-11-02","endTime":"2026-11-09"}],"lastUpdated":"2026-11-09T05:00:00Z","normalization":"PERCENTAGE","units":[{"name":"bot_name","value":"percentage"}]},"top_0":[{"botCategory":"SEARCH_ENGINE_CRAWLER","botName":"Googlebot","botOwner":"Google","value":"45.02"},{"botCategory":"AI_CRAWLER","botName":"GPTBot","botOwner":"OpenAI","value":"2.10"}]}}""");

        var top = await harness.Client.Radar.GetTopVerifiedBotsAsync(new RadarVerifiedBotTopParameters
        {
            Asn = new List<string> { "13335" },
            Continent = new List<string> { "EU" },
            DateRange = new List<string> { "7d" },
            Limit = 2,
            Location = new List<string> { "US" },
            Name = new List<string> { "one" },
        });

        Assert.Equal("/client/v4/radar/verified_bots/top/bots", harness.LastRequest.Path);
        Assert.Equal(
            "asn=13335&continent=EU&dateRange=7d&limit=2&location=US&name=one",
            harness.LastRequest.Query.TrimStart('?'));

        Assert.Equal(2, top!.Top0!.Count);
        Assert.Equal("Googlebot", top.Top0[0].BotName);
        Assert.Equal("Google", top.Top0[0].BotOwner);
        Assert.Equal("SEARCH_ENGINE_CRAWLER", top.Top0[0].BotCategory);

        // Published as a string and left as one: it is a share of traffic that Cloudflare spells however it
        // likes, and a number insisted on would fail the whole ranking over one entry.
        Assert.Equal("45.02", top.Top0[0].Value);
        Assert.Equal("Googlebot 45.02 by Google", top.Top0[0].ToString());
        Assert.Empty(top.SeriesNames);
        Assert.Equal(
            "2 entry(ies) [Googlebot 45.02 by Google, GPTBot 2.10 by OpenAI], " +
            "over 2026-11-02 to 2026-11-09, updated 2026-11-09T05:00:00Z",
            top.ToString());
    }

    [Fact]
    public async Task GetTopVerifiedBotCategoriesAsync_reads_the_categories_beside_the_bots()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"meta":{"confidenceInfo":null,"dateRange":[],"lastUpdated":"2026-11-09T05:00:00Z","normalization":"PERCENTAGE","units":[]},"top_0":[{"botCategory":"SEARCH_ENGINE_CRAWLER","value":"60.10"},{"botCategory":"AI_CRAWLER","value":"11.40"}]}}""");

        var top = await harness.Client.Radar.GetTopVerifiedBotCategoriesAsync(
            new RadarVerifiedBotTopParameters { Limit = 2 });

        Assert.Equal("/client/v4/radar/verified_bots/top/categories", harness.LastRequest.Path);
        Assert.Equal("limit=2", harness.LastRequest.Query.TrimStart('?'));
        Assert.Equal("SEARCH_ENGINE_CRAWLER 60.10", top!.Top0![0].ToString());

        // The meta carries no dateRange on this answer, so all it has to say is when Cloudflare updated it -
        // which is what a summary's meta prints when there is nothing else in it.
        Assert.Equal("updated 2026-11-09T05:00:00Z", top.Meta!.ToString());
        Assert.Equal("2 entry(ies) [SEARCH_ENGINE_CRAWLER 60.10, AI_CRAWLER 11.40], updated 2026-11-09T05:00:00Z", top.ToString());
    }

    [Fact]
    public async Task Radar_bot_filters_refuse_a_value_they_cannot_spell()
    {
        using var harness = CloudflareHarness.Create();

        var error = await Assert.ThrowsAsync<CloudflareConfigurationException>(() =>
            harness.Client.Radar.GetBotSummaryAsync(
                RadarBotDimension.Bot,
                new RadarBotSummaryParameters
                {
                    BotCategory = new List<RadarBotCategory> { RadarBotCategory.Unknown },
                    LimitPerGroup = 0,
                }));

        Assert.Equal(2, error.Problems.Count);
        Assert.Contains("not a bot category", error.Problems[0], StringComparison.Ordinal);
        Assert.Contains("at least one entry", error.Problems[1], StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);

        var crawler = await Assert.ThrowsAsync<CloudflareConfigurationException>(() =>
            harness.Client.Radar.GetWebCrawlerSummaryAsync(
                RadarWebCrawlerDimension.UserAgent,
                new RadarWebCrawlerSummaryParameters
                {
                    ClientType = new List<RadarBotClientType> { RadarBotClientType.Unknown },
                }));

        Assert.Contains("not a client type", Assert.Single(crawler.Problems), StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public void Every_bot_filter_is_spelled_as_radar_publishes_it()
    {
        Assert.Equal(
            new[] { "BOT", "BOT_KIND", "BOT_OPERATOR", "BOT_CATEGORY" },
            CloudflareEnumConverter<RadarBotDimension>.KnownValues);

        Assert.Equal(
            new[]
            {
                "CLIENT_TYPE", "USER_AGENT", "REFERER", "CRAWL_REFER_RATIO", "VERTICAL", "INDUSTRY",
                "RESPONSE_STATUS", "RESPONSE_STATUS_CATEGORY",
            },
            CloudflareEnumConverter<RadarWebCrawlerDimension>.KnownValues);

        Assert.Equal(new[] { "AGENT", "BOT" }, CloudflareEnumConverter<RadarBotKind>.KnownValues);

        Assert.Equal(
            new[]
            {
                "SEARCH_ENGINE_CRAWLER", "SEARCH_ENGINE_OPTIMIZATION", "MONITORING_AND_ANALYTICS",
                "ADVERTISING_AND_MARKETING", "SOCIAL_MEDIA_MARKETING", "PAGE_PREVIEW", "ACADEMIC_RESEARCH",
                "SECURITY", "ACCESSIBILITY", "WEBHOOKS", "FEED_FETCHER", "AI_CRAWLER", "AGGREGATOR",
                "AI_ASSISTANT", "AI_SEARCH", "ARCHIVER",
            },
            CloudflareEnumConverter<RadarBotCategory>.KnownValues);

        Assert.Equal(
            new[] { "VERIFIED" },
            CloudflareEnumConverter<RadarBotVerificationStatus>.KnownValues);

        Assert.Equal(
            new[] { "HUMAN", "NON_AI_BOT", "AI_BOT", "MIXED_PURPOSE" },
            CloudflareEnumConverter<RadarBotClientType>.KnownValues);

        Assert.Equal(
            new[] { "INFORMATIONAL", "SUCCESS", "REDIRECTION", "CLIENT_ERROR", "SERVER_ERROR" },
            CloudflareEnumConverter<RadarResponseStatusCategory>.KnownValues);

        // The two spellings of one idea: a request asks for 1d and an answer reports ONE_DAY.
        Assert.Equal(
            new[] { "15m", "1h", "1d", "1w" },
            CloudflareEnumConverter<RadarAggregationInterval>.KnownValues);

        Assert.Equal(
            new[] { "PERCENTAGE", "MIN0_MAX", "PERCENTAGE_CHANGE" },
            CloudflareEnumConverter<RadarTimeseriesNormalization>.KnownValues);
    }
}
