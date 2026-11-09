using System.Net;
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
            """{"success":true,"errors":[],"messages":[],"result":{"geolocations":[{"code":"GB-ENG","latitude":"52.0","locale":"en","longitude":"-1.0","name":"England","parent":{"code":"GB","latitude":"54.0","longitude":"-2.0","name":"United Kingdom","parent":{"code":"EU","latitude":"51.0","longitude":"10.0","name":"Europe","parent":null,"type":"CONTINENT"},"type":"COUNTRY"},"type":"ADM1"}]}}""");

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
    }

    [Fact]
    public async Task GetGeolocationAsync_reads_one_place_and_tolerates_one_that_is_not_there()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":{"geolocation":{"code":"GB","latitude":"54.0","longitude":"-2.0","name":"United Kingdom","parent":{"code":"EU","latitude":"51.0","longitude":"10.0","name":"Europe","parent":null,"type":"CONTINENT"},"type":"COUNTRY"}}}""");
        harness.Respond(
            """{"success":false,"errors":[{"code":12004,"message":"Geolocation not found"}],"messages":[],"result":null}""",
            HttpStatusCode.NotFound);

        var found = await harness.Client.Radar.GetGeolocationAsync("GB");
        var missing = await harness.Client.Radar.GetGeolocationAsync("QQ");

        Assert.Equal("/client/v4/radar/geolocations/GB", harness.Requests[0].Path);
        Assert.NotNull(found);
        Assert.Equal(RadarGeolocationType.Country, found!.Type);
        Assert.Single(found.Ancestors);
        Assert.Null(missing);
        Assert.Equal(2, harness.RequestCount);
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

        await harness.Client.Radar.ListLocationsAsync();
        await harness.Client.Radar.ListAsnsAsync();
        await harness.Client.Radar.ListGeolocationsAsync();
        await harness.Client.Radar.SearchAsync("cloudflare");

        // Radar is public data at the API root, beside GET /ips: no id to resolve, so no id to get wrong.
        Assert.All(harness.Requests, request => Assert.StartsWith("/client/v4/radar/", request.Path, StringComparison.Ordinal));
        Assert.All(harness.Requests, request => Assert.Contains("Authorization", request.Headers.Keys));
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
}
