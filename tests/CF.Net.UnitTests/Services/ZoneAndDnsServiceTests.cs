using System.Net;
using System.Text.Json;
using CFNet.Exceptions;
using CFNet.Http;
using CFNet.Models.Common;
using CFNet.Models.Dns;
using CFNet.Models.Ssl;
using CFNet.Models.Zones;
using CFNet.Serialization;
using CFNet.UnitTests.Testing;
using Xunit;

namespace CFNet.UnitTests.Services;

public class ZonesServiceTests
{
    [Fact]
    public async Task ListAsync_hits_the_zones_collection_and_returns_a_page()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage(
            "[{\"id\":\"z-1\",\"name\":\"a.test\",\"status\":\"active\"},{\"id\":\"z-2\",\"name\":\"b.test\",\"status\":\"pending\"}]",
            page: 1, totalPages: 2, count: 2, totalCount: 4);

        var page = await harness.Client.Zones.ListAsync();

        Assert.Equal("/client/v4/zones", harness.LastRequest.Path);
        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Equal(2, page.Count);
        Assert.Equal("a.test", page.Items[0].Name);
        Assert.True(page.Items[0].IsActive);
        Assert.False(page.Items[1].IsActive);
        Assert.Equal(4, page.TotalCount);
        Assert.True(page.HasMore);
        Assert.Equal(2, page.NextPage);
    }

    [Fact]
    public async Task ListAllAsync_walks_every_page_exactly_once()
    {
        using var harness = CloudflareHarness.Create();
        harness
            .RespondWithPage("[{\"id\":\"z-1\",\"name\":\"a.test\"}]", page: 1, totalPages: 3, count: 1, totalCount: 3)
            .RespondWithPage("[{\"id\":\"z-2\",\"name\":\"b.test\"}]", page: 2, totalPages: 3, count: 1, totalCount: 3)
            .RespondWithPage("[{\"id\":\"z-3\",\"name\":\"c.test\"}]", page: 3, totalPages: 3, count: 1, totalCount: 3);

        var zones = await harness.Client.Zones.ListAllAsync().CollectAsync();

        Assert.Equal(new[] { "a.test", "b.test", "c.test" }, zones.Select(zone => zone.Name));
        Assert.Equal(3, harness.RequestCount);
        Assert.Equal("page=1", harness.Requests[0].Query.TrimStart('?'));
        Assert.Equal("page=2", harness.Requests[1].Query.TrimStart('?'));
        Assert.Equal("page=3", harness.Requests[2].Query.TrimStart('?'));
    }

    [Fact]
    public async Task ListAllAsync_keeps_the_callers_filters_on_every_page()
    {
        using var harness = CloudflareHarness.Create();
        harness
            .RespondWithPage("[{\"id\":\"z-1\",\"name\":\"a.test\"}]", page: 1, totalPages: 2, count: 1, totalCount: 2)
            .RespondWithPage("[{\"id\":\"z-2\",\"name\":\"b.test\"}]", page: 2, totalPages: 2, count: 1, totalCount: 2);

        _ = await harness.Client.Zones.ListAllAsync(new ZoneListParameters
        {
            Status = ZoneStatusValues.Active,
            PerPage = 25,
            Direction = SortDirection.Ascending,
        }).CollectAsync();

        Assert.Equal("page=1&per_page=25&status=active&direction=asc", harness.Requests[0].Query.TrimStart('?'));
        Assert.Equal("page=2&per_page=25&status=active&direction=asc", harness.Requests[1].Query.TrimStart('?'));
    }

    [Fact]
    public async Task ListAllAsync_stops_when_a_page_comes_back_empty()
    {
        using var harness = CloudflareHarness.Create();
        harness
            .RespondWithPage("[]", page: 1, totalPages: 5, count: 0, totalCount: 10)
            .RespondWithPage("[]", page: 2, totalPages: 5, count: 0, totalCount: 10);

        var zones = await harness.Client.Zones.ListAllAsync().CollectAsync();

        Assert.Empty(zones);
        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task GetAsync_uses_the_default_zone_id_and_returns_null_on_404()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.NotFound, 1001, "Zone not found");

        var zone = await harness.Client.Zones.GetAsync();

        Assert.Null(zone);
        Assert.Equal($"/client/v4/zones/{CloudflareHarness.TestZoneId}", harness.LastRequest.Path);
    }

    [Fact]
    public async Task GetAsync_returns_null_when_cloudflare_refuses_to_show_the_zone()
    {
        using var harness = CloudflareHarness.Create();
        // Observed against the live API: an unknown zone id is answered with 403 and 9109 ("Invalid zone
        // identifier"), not 404, and a zone outside the token's Zone Resources with 403 and 10000.
        harness.RespondWithError(HttpStatusCode.Forbidden, 9109, "Invalid zone identifier");

        var zone = await harness.Client.Zones.GetAsync();

        Assert.Null(zone);
    }

    [Fact]
    public async Task GetAsync_prefers_an_explicit_zone_id()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"other\",\"name\":\"other.test\"}");

        var zone = await harness.Client.Zones.GetAsync("other");

        Assert.Equal("other", zone!.Id);
        Assert.Equal("/client/v4/zones/other", harness.LastRequest.Path);
    }

    [Fact]
    public async Task GetAsync_throws_when_no_zone_id_is_available()
    {
        using var harness = CloudflareHarness.Create(options => options.ZoneId = null);

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(() => harness.Client.Zones.GetAsync());

        Assert.Contains("CLOUDFLARE_ZONE_ID", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task GetByNameAsync_filters_by_name_and_prefers_the_exact_hit()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage(
            "[{\"id\":\"z-2\",\"name\":\"EXAMPLE.test\"},{\"id\":\"z-1\",\"name\":\"other.test\"}]",
            page: 1, totalPages: 1, count: 2, totalCount: 2);

        var zone = await harness.Client.Zones.GetByNameAsync("example.test");

        Assert.Equal("z-2", zone!.Id);
        Assert.Equal("per_page=1&name=example.test", harness.LastRequest.Query.TrimStart('?'));
    }

    [Fact]
    public async Task GetByNameAsync_rejects_a_blank_name()
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<CloudflareConfigurationException>(() => harness.Client.Zones.GetByNameAsync("  "));
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task CreateAsync_sends_the_name_account_and_type()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"z-new\",\"name\":\"new.test\",\"status\":\"pending\"}");

        var zone = await harness.Client.Zones.CreateAsync("new.test");

        Assert.Equal("z-new", zone.Id);
        Assert.Equal("/client/v4/zones", harness.LastRequest.Path);
        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.Contains("\"name\":\"new.test\"", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.Contains($"\"account\":{{\"id\":\"{CloudflareHarness.TestAccountId}\"}}", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"full\"", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_accepts_a_partial_zone_type()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"z-p\"}");

        await harness.Client.Zones.CreateAsync("partial.test", ZoneTypeValues.Partial);

        Assert.Contains("\"type\":\"partial\"", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_uses_an_explicit_account_id_when_the_request_supplies_one()
    {
        using var harness = CloudflareHarness.Create(options => options.AccountId = null);
        harness.RespondWithResult("{\"id\":\"z-x\"}");

        await harness.Client.Zones.CreateAsync(new CreateZoneRequest("x.test", "acct-explicit"));

        Assert.Contains("\"id\":\"acct-explicit\"", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_request_without_a_name()
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Zones.CreateAsync(new CreateZoneRequest()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => harness.Client.Zones.CreateAsync((CreateZoneRequest)null!));
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task UpdateAsync_patches_the_zone()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"z-1\",\"paused\":true}");

        var zone = await harness.Client.Zones.UpdateAsync("z-1", new UpdateZoneRequest().WithPlan("plan-1"));

        Assert.True(zone.Paused);
        Assert.Equal("/client/v4/zones/z-1", harness.LastRequest.Path);
        Assert.Equal(HttpMethod.Patch, harness.LastRequest.Method);
        Assert.Contains("\"plan\":{\"id\":\"plan-1\"}", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("paused", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetPausedAsync_sends_only_the_paused_flag()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"z-1\",\"paused\":true}");

        await harness.Client.Zones.SetPausedAsync(true);

        Assert.Equal("{\"paused\":true}", harness.LastRequest.Body);
        Assert.Equal($"/client/v4/zones/{CloudflareHarness.TestZoneId}", harness.LastRequest.Path);
    }

    [Fact]
    public async Task DeleteAsync_deletes_and_ignores_the_result()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"z-1\"}");

        await harness.Client.Zones.DeleteAsync("z-1");

        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.Equal("/client/v4/zones/z-1", harness.LastRequest.Path);
    }

    [Fact]
    public async Task DeleteAsync_rejects_a_blank_id()
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<CloudflareConfigurationException>(() => harness.Client.Zones.DeleteAsync(""));
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task RunActivationCheckAsync_posts_to_the_activation_check_endpoint()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"z-1\"}");

        var result = await harness.Client.Zones.RunActivationCheckAsync("z-1");

        Assert.Equal("z-1", result.Id);
        Assert.Equal("/client/v4/zones/z-1/activation_check", harness.LastRequest.Path);
        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.Null(harness.LastRequest.Body);
    }

    [Fact]
    public async Task RunActivationCheckAsync_falls_back_to_the_known_id()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("null");

        var result = await harness.Client.Zones.RunActivationCheckAsync("z-9");

        Assert.Equal("z-9", result.Id);
    }

    [Fact]
    public async Task PurgeCacheAsync_sends_the_targeted_payload()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"purge_time\":4}");

        var result = await harness.Client.Zones.PurgeCacheAsync(
            PurgeCacheRequest.ForFiles(new[] { "https://a.test/x.css", "https://a.test/y.css" }));

        Assert.Equal(4, result.PurgeTime);
        Assert.Equal($"/client/v4/zones/{CloudflareHarness.TestZoneId}/purge_cache", harness.LastRequest.Path);
        Assert.Contains("\"files\":[\"https://a.test/x.css\",\"https://a.test/y.css\"]", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("purge_everything", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PurgeCacheAsync_sends_purge_everything_on_its_own()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{}");

        await harness.Client.Zones.PurgeCacheAsync(PurgeCacheRequest.Everything());

        Assert.Equal("{\"purge_everything\":true}", harness.LastRequest.Body);
    }

    [Fact]
    public void PurgeCacheRequest_is_validated_locally_before_any_call()
    {
        Assert.Throws<CloudflareConfigurationException>(() => new PurgeCacheRequest().EnsureValid());
        Assert.Throws<CloudflareConfigurationException>(
            () => new PurgeCacheRequest { PurgeEverything = true, Files = new List<string> { "https://a.test" } }.EnsureValid());

        PurgeCacheRequest.Everything().EnsureValid();
        PurgeCacheRequest.ForTags(new[] { "t" }).EnsureValid();
        new PurgeCacheRequest { Hosts = new List<string> { "a.test" } }.EnsureValid();
        new PurgeCacheRequest { Prefixes = new List<string> { "a.test/" } }.EnsureValid();
        new PurgeCacheRequest { IgnoreTags = new List<string> { "keep" }, Tags = new List<string> { "drop" } }.EnsureValid();
    }

    [Fact]
    public async Task PurgeCacheAsync_propagates_the_local_validation_failure()
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Zones.PurgeCacheAsync(new PurgeCacheRequest()));
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public void ZoneListParameters_builds_the_documented_query_string()
    {
        var query = new QueryStringBuilder();
        new ZoneListParameters
        {
            Page = 3,
            PerPage = 10,
            Name = "example.com",
            Status = ZoneStatusValues.Pending,
            AccountId = "acct-1",
            Order = "status",
            Direction = SortDirection.Ascending,
            Match = FilterMatchMode.Any,
        }.Apply(query);

        Assert.Equal(
            "page=3&per_page=10&name=example.com&status=pending&account.id=acct-1&order=status&direction=asc&match=any",
            query.ToString());
    }
}

public class DnsRecordsServiceTests
{
    [Fact]
    public async Task ListAsync_scopes_the_call_to_the_zone()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage(
            "[{\"id\":\"r-1\",\"name\":\"www.a.test\",\"type\":\"A\",\"content\":\"198.51.100.4\"}]",
            page: 1, totalPages: 1, count: 1, totalCount: 1);

        var page = await harness.Client.DnsRecords.ListAsync();

        Assert.Equal($"/client/v4/zones/{CloudflareHarness.TestZoneId}/dns_records", harness.LastRequest.Path);
        Assert.Single(page.Items);
        Assert.Equal(DnsRecordTypes.A, page.Items[0].Type);
    }

    [Fact]
    public async Task ListAsync_applies_every_filter_in_the_documented_order()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");

        await harness.Client.DnsRecords.ListAsync(new DnsRecordListParameters
        {
            Page = 2,
            PerPage = 100,
            Type = DnsRecordTypes.Cname,
            Name = "www.a.test",
            Content = "origin.a.test",
            Order = "type",
            Direction = SortDirection.Descending,
            Proxied = false,
            Tag = "owner:web",
            Tags = new[] { "env:prod", "team:core" },
            TagMatch = FilterMatchMode.All,
            Match = FilterMatchMode.Any,
            Search = "www",
        });

        Assert.Equal(
            "page=2&per_page=100&type=CNAME&name=www.a.test&content=origin.a.test&order=type&direction=desc" +
            "&proxied=false&tag=owner%3Aweb&tag=env%3Aprod&tag=team%3Acore&tag_match=all&match=any&search=www",
            harness.LastRequest.Query.TrimStart('?'));
    }

    [Fact]
    public async Task ListAllAsync_paginates_until_the_last_page()
    {
        using var harness = CloudflareHarness.Create();
        harness
            .RespondWithPage("[{\"id\":\"r-1\"},{\"id\":\"r-2\"}]", page: 1, totalPages: 2, count: 2, totalCount: 3)
            .RespondWithPage("[{\"id\":\"r-3\"}]", page: 2, totalPages: 2, count: 1, totalCount: 3);

        var records = await harness.Client.DnsRecords.ListAllAsync().CollectAsync();

        Assert.Equal(3, records.Count);
        Assert.Equal(2, harness.RequestCount);
    }

    [Fact]
    public async Task FindAsync_filters_by_name_and_optionally_type()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage("[{\"id\":\"r-1\",\"name\":\"www.a.test\"}]", page: 1, totalPages: 1, count: 1, totalCount: 1);

        var records = await harness.Client.DnsRecords.FindAsync("www.a.test", DnsRecordTypes.A);

        Assert.Single(records);
        Assert.Equal("per_page=100&type=A&name=www.a.test", harness.LastRequest.Query.TrimStart('?'));
    }

    [Fact]
    public async Task GetAsync_returns_null_when_the_record_is_gone()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.NotFound, 1003, "Unknown DNS record");

        var record = await harness.Client.DnsRecords.GetAsync("r-missing");

        Assert.Null(record);
        Assert.Equal($"/client/v4/zones/{CloudflareHarness.TestZoneId}/dns_records/r-missing", harness.LastRequest.Path);
    }

    [Fact]
    public async Task GetAsync_returns_null_when_the_zone_cannot_be_read()
    {
        using var harness = CloudflareHarness.Create();
        // A zone-scoped read the token is not allowed is answered with 403 and 10000, which for a lookup
        // means the same thing as "not there".
        harness.RespondWithError(HttpStatusCode.Forbidden, 10000, "Authentication error");

        var record = await harness.Client.DnsRecords.GetAsync("r-1");

        Assert.Null(record);
    }

    [Fact]
    public async Task GetAsync_percent_encodes_the_record_id()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"weird\"}");

        await harness.Client.DnsRecords.GetAsync("a/b");

        Assert.Contains("/dns_records/a%2Fb", harness.LastRequest.Uri!.OriginalString, StringComparison.Ordinal);
        Assert.Contains("/dns_records/a%2Fb", harness.LastRequest.Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_posts_the_record_and_returns_the_created_entity()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"r-new\",\"name\":\"www.a.test\",\"type\":\"A\",\"content\":\"198.51.100.4\",\"proxied\":true,\"ttl\":1}");

        var created = await harness.Client.DnsRecords.CreateAsync(new DnsRecord
        {
            Type = DnsRecordTypes.A,
            Name = "www.a.test",
            Content = "198.51.100.4",
            Proxied = true,
            Ttl = 1,
        });

        Assert.Equal("r-new", created.Id);
        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.Equal($"/client/v4/zones/{CloudflareHarness.TestZoneId}/dns_records", harness.LastRequest.Path);
        Assert.Contains("\"type\":\"A\"", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"proxied\":true", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"ttl\":1", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAddressRecordAsync_infers_A_from_an_ipv4_address()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"r-1\"}");

        await harness.Client.DnsRecords.CreateAddressRecordAsync("www.a.test", "198.51.100.4", proxied: false, ttl: 300);

        Assert.Contains("\"type\":\"A\"", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"proxied\":false", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"ttl\":300", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAddressRecordAsync_infers_AAAA_from_an_ipv6_address()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"r-1\"}");

        await harness.Client.DnsRecords.CreateAddressRecordAsync("v6.a.test", "2001:db8::1");

        Assert.Contains("\"type\":\"AAAA\"", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"content\":\"2001:db8::1\"", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateCnameRecordAsync_sends_a_cname()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"r-1\"}");

        await harness.Client.DnsRecords.CreateCnameRecordAsync("www.a.test", "origin.a.test", proxied: true);

        Assert.Contains("\"type\":\"CNAME\"", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"content\":\"origin.a.test\"", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateTxtRecordAsync_never_proxies()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"r-1\"}");

        await harness.Client.DnsRecords.CreateTxtRecordAsync("a.test", "v=spf1 -all");

        Assert.Contains("\"type\":\"TXT\"", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"proxied\":false", harness.LastRequest.Body, StringComparison.Ordinal);

        // A TXT value may legitimately be empty-ish text but must never be dropped.
        Assert.Contains("\"content\":\"v=spf1 -all\"", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_record_that_is_missing_its_identity()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.DnsRecords.CreateAsync(new DnsRecord()));

        Assert.Equal(3, exception.Problems.Count);
        Assert.Contains(exception.Problems, problem => problem.Contains("Type", StringComparison.Ordinal));
        Assert.Contains(exception.Problems, problem => problem.Contains("Name", StringComparison.Ordinal));
        Assert.Contains(exception.Problems, problem => problem.Contains("Content", StringComparison.Ordinal));
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_record_with_both_content_and_data()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(() => harness.Client.DnsRecords.CreateAsync(new DnsRecord
        {
            Type = DnsRecordTypes.Srv,
            Name = "_sip._tcp.a.test",
            Content = "should not be here",
            Data = new DnsRecordSrvData { Target = "t.a.test" },
        }));

        Assert.Contains(exception.Problems, problem => problem.Contains("both Content and Data", StringComparison.Ordinal));
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task CreateAsync_accepts_a_structured_record_without_content()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"r-srv\",\"type\":\"SRV\"}");

        await harness.Client.DnsRecords.CreateAsync(new DnsRecord
        {
            Type = DnsRecordTypes.Srv,
            Name = "_sip._tcp.a.test",
            Ttl = 1,
            Data = new DnsRecordSrvData
            {
                Service = "sip",
                Proto = "tcp",
                Priority = 10,
                Weight = 60,
                Port = 5060,
                Target = "sipserver.a.test",
            },
        });

        Assert.Contains("\"proto\":\"tcp\"", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"port\":5060", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"content\":", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateAsync_strips_the_identifiers_from_the_patch_body()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"r-1\",\"content\":\"203.0.113.9\"}");

        await harness.Client.DnsRecords.UpdateAsync("r-1", new DnsRecord
        {
            Id = "should-be-dropped",
            ZoneId = "should-be-dropped",
            ZoneName = "should-be-dropped",
            Content = "203.0.113.9",
        });

        Assert.Equal(HttpMethod.Patch, harness.LastRequest.Method);
        Assert.Equal($"/client/v4/zones/{CloudflareHarness.TestZoneId}/dns_records/r-1", harness.LastRequest.Path);
        Assert.Equal("{\"content\":\"203.0.113.9\"}", harness.LastRequest.Body);
    }

    [Fact]
    public async Task ReplaceAsync_sends_the_full_record_as_a_put()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"r-1\"}");

        await harness.Client.DnsRecords.ReplaceAsync("r-1", new DnsRecord
        {
            Id = "r-1",
            Type = DnsRecordTypes.A,
            Name = "www.a.test",
            Content = "203.0.113.9",
            Ttl = 120,
            Proxied = false,
        });

        Assert.Equal(HttpMethod.Put, harness.LastRequest.Method);
        Assert.Contains("\"type\":\"A\"", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"ttl\":120", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"id\":", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("zone_name", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplaceAsync_validates_the_record_like_a_create()
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.DnsRecords.ReplaceAsync("r-1", new DnsRecord { Content = "x" }));
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task UpdateAsync_does_not_validate_so_a_partial_patch_is_allowed()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"r-1\",\"comment\":\"hi\"}");

        await harness.Client.DnsRecords.UpdateAsync("r-1", new DnsRecord { Comment = "hi" });

        Assert.Equal("{\"comment\":\"hi\"}", harness.LastRequest.Body);
    }

    [Fact]
    public async Task DeleteAsync_removes_the_record()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"r-1\"}");

        await harness.Client.DnsRecords.DeleteAsync("r-1");

        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.Equal($"/client/v4/zones/{CloudflareHarness.TestZoneId}/dns_records/r-1", harness.LastRequest.Path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_record_ids_are_rejected_before_a_call_is_built(string? recordId)
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<CloudflareConfigurationException>(() => harness.Client.DnsRecords.DeleteAsync(recordId!));
        await Assert.ThrowsAsync<CloudflareConfigurationException>(() => harness.Client.DnsRecords.GetAsync(recordId!));
        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.DnsRecords.UpdateAsync(recordId!, new DnsRecord { Content = "x" }));
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task A_null_record_is_rejected()
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<ArgumentNullException>(() => harness.Client.DnsRecords.CreateAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => harness.Client.DnsRecords.UpdateAsync("r-1", null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => harness.Client.DnsRecords.ReplaceAsync("r-1", null!));
    }

    [Fact]
    public async Task A_zone_scoped_call_uses_the_explicit_zone_id()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");

        await harness.Client.DnsRecords.ListAsync(zoneId: "zone-explicit");

        Assert.Equal("/client/v4/zones/zone-explicit/dns_records", harness.LastRequest.Path);
    }

    [Fact]
    public async Task A_cloudflare_validation_error_is_surfaced_with_its_code()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.BadRequest, 1004, "DNS Validation Error");

        var exception = await Assert.ThrowsAsync<CloudflareApiException>(
            () => harness.Client.DnsRecords.CreateAsync(new DnsRecord
            {
                Type = DnsRecordTypes.A,
                Name = "www.a.test",
                Content = "not-an-ip",
            }));

        Assert.Equal(1004, exception.ErrorCode);
        Assert.True(exception.IsValidationFailure);
        Assert.False(exception.IsTransient);
        Assert.Contains("DNS Validation Error", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_returns_the_bind_file_verbatim()
    {
        using var harness = CloudflareHarness.Create();

        // The export endpoint answers with the zone file itself, not a JSON envelope.
        const string zoneFile = ";; a.test zone file\na.test.\t300\tIN\tA\t198.51.100.1\n";
        harness.Respond(zoneFile);

        var exported = await harness.Client.DnsRecords.ExportAsync();

        Assert.Equal(zoneFile, exported);
        Assert.Equal("GET", harness.LastRequest.Method.Method);
        Assert.Equal($"/client/v4/zones/{CloudflareHarness.TestZoneId}/dns_records/export", harness.LastRequest.Path);
    }

    [Fact]
    public async Task ExportAsync_honours_an_explicit_zone()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("");

        await harness.Client.DnsRecords.ExportAsync("z-other");

        Assert.Equal("/client/v4/zones/z-other/dns_records/export", harness.LastRequest.Path);
    }

    [Fact]
    public async Task ImportAsync_uploads_the_zone_file_as_multipart_with_the_proxied_field()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"recs_added\":3,\"total_records_parsed\":4}");

        var result = await harness.Client.DnsRecords.ImportAsync(
            "a.test.\t300\tIN\tA\t198.51.100.1\nwww.a.test.\t300\tIN\tCNAME\ta.test.\n",
            proxied: true);

        Assert.Equal(3, result!.RecordsAdded);
        Assert.Equal(4, result.TotalRecordsParsed);

        Assert.Equal("POST", harness.LastRequest.Method.Method);
        Assert.Equal($"/client/v4/zones/{CloudflareHarness.TestZoneId}/dns_records/import", harness.LastRequest.Path);
        Assert.Equal("multipart/form-data", harness.LastRequest.ContentType!.Split(';')[0].Trim());

        // The BIND config goes in the "file" part, and "proxied" is a plain field carrying the
        // strings the API documents, not a JSON boolean.
        Assert.Contains("name=file", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.Contains("filename=zone.txt", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.Contains("name=proxied", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.Contains("true", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.Contains("198.51.100.1", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_omits_proxied_when_the_caller_did_not_choose()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"recs_added\":1,\"total_records_parsed\":1}");

        await harness.Client.DnsRecords.ImportAsync("a.test.\t300\tIN\tA\t198.51.100.1");

        Assert.Contains("name=file", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("proxied", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_rejects_an_empty_zone_file_before_calling()
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<ArgumentException>(() => harness.Client.DnsRecords.ImportAsync(string.Empty));

        Assert.Equal(0, harness.RequestCount);
    }
}


public class ZoneSettingsTests
{
    [Fact]
    public async Task ListSettingsAsync_reads_every_setting_the_zone_has()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""
        [
          {"id":"always_online","value":"on","editable":true,"modified_on":"2026-05-13T23:14:19.472617Z"},
          {"id":"advanced_ddos","value":"off","editable":false},
          {"id":"browser_cache_ttl","value":14400,"editable":true},
          {"id":"ipv6","value":true,"editable":true},
          {"id":"aegis","value":{"enabled":true,"pool_id":"pool-1"},"editable":true}
        ]
        """);

        var settings = await harness.Client.Zones.ListSettingsAsync();

        Assert.Equal($"/client/v4/zones/{CloudflareHarness.TestZoneId}/settings", harness.LastRequest.Path);
        Assert.Equal(5, settings.Count);

        var alwaysOnline = settings.Single(setting => setting.Id == ZoneSettingIds.AlwaysOnline);
        Assert.Equal("on", alwaysOnline.AsString());
        Assert.True(alwaysOnline.IsOn);
        Assert.True(alwaysOnline.Editable);
        Assert.Equal(new DateTimeOffset(2026, 5, 13, 23, 14, 19, TimeSpan.Zero).AddTicks(4726170), alwaysOnline.ModifiedOn);

        Assert.False(settings.Single(setting => setting.Id == ZoneSettingIds.AdvancedDdos).IsOn);
        Assert.False(settings.Single(setting => setting.Id == ZoneSettingIds.AdvancedDdos).Editable);
        Assert.Equal(14400, settings.Single(setting => setting.Id == "browser_cache_ttl").AsNumber());
        Assert.True(settings.Single(setting => setting.Id == "ipv6").IsOn);

        // A setting whose value is an object stays reachable through the raw element.
        var aegis = settings.Single(setting => setting.Id == ZoneSettingIds.Aegis);
        Assert.Equal("pool-1", aegis.Value!.Value.GetProperty("pool_id").GetString());
    }

    [Fact]
    public async Task ListSettingsAsync_reads_a_number_that_cloudflare_sent_as_a_string()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""[{"id":"browser_cache_ttl","value":"7200"}]""");

        var settings = await harness.Client.Zones.ListSettingsAsync();

        Assert.Equal(7200, Assert.Single(settings).AsNumber());
    }

    [Fact]
    public async Task GetSettingAsync_scopes_the_call_to_one_setting()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"id":"security_level","value":"medium","editable":true}""");

        var setting = await harness.Client.Zones.GetSettingAsync("security_level");

        Assert.Equal($"/client/v4/zones/{CloudflareHarness.TestZoneId}/settings/security_level", harness.LastRequest.Path);
        Assert.Equal("medium", setting!.AsString());
    }

    [Fact]
    public async Task GetSettingAsync_returns_null_for_a_setting_the_zone_does_not_have()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.NotFound, 1001, "Unknown setting");

        var setting = await harness.Client.Zones.GetSettingAsync("not_a_setting");

        Assert.Null(setting);
    }

    [Fact]
    public async Task GetSettingAsync_percent_encodes_the_setting_id()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"id":"a/b"}""");

        await harness.Client.Zones.GetSettingAsync("a/b");

        // Uri.AbsolutePath decodes %2F, so the encoding has to be checked on the Uri itself.
        Assert.Contains("/settings/a%2Fb", harness.LastRequest.Uri!.OriginalString, StringComparison.Ordinal);
        Assert.Contains("/settings/a%2Fb", harness.LastRequest.Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetSettingOnOffAsync_sends_the_on_off_value_cloudflare_expects()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"id":"always_online","value":"off"}""");

        await harness.Client.Zones.SetSettingOnOffAsync(ZoneSettingIds.AlwaysOnline, on: false);

        Assert.Equal(HttpMethod.Patch, harness.LastRequest.Method);
        Assert.Equal($"/client/v4/zones/{CloudflareHarness.TestZoneId}/settings/always_online", harness.LastRequest.Path);
        Assert.Equal("{\"value\":\"off\"}", harness.LastRequest.Body);
    }

    [Fact]
    public async Task SetSettingValueAsync_sends_a_number_and_an_object_verbatim()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"id":"browser_cache_ttl","value":7200}""");

        await harness.Client.Zones.SetSettingValueAsync("browser_cache_ttl", 7200);
        Assert.Equal("{\"value\":7200}", harness.LastRequest.Body);

        harness.RespondWithResult("""{"id":"aegis","value":{"enabled":true,"pool_id":"pool-2"}}""");
        await harness.Client.Zones.SetSettingValueAsync(
            ZoneSettingIds.Aegis,
            new Dictionary<string, object?> { ["enabled"] = true, ["pool_id"] = "pool-2" });

        Assert.Equal($"/client/v4/zones/{CloudflareHarness.TestZoneId}/settings/aegis", harness.LastRequest.Path);
        Assert.Equal("{\"value\":{\"enabled\":true,\"pool_id\":\"pool-2\"}}", harness.LastRequest.Body);
    }

    [Fact]
    public async Task UpdateSettingAsync_can_send_the_enabled_flag_shape_instead()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"id":"ssl_recommender","value":true}""");

        await harness.Client.Zones.UpdateSettingAsync("ssl_recommender", UpdateZoneSettingRequest.OfEnabled(true));

        Assert.Equal("{\"enabled\":true}", harness.LastRequest.Body);
    }

    [Fact]
    public async Task UpdateSettingAsync_rejects_a_body_that_carries_neither_shape()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Zones.UpdateSettingAsync("always_online", new UpdateZoneSettingRequest()));

        Assert.Contains("Value", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public void UpdateSettingAsync_rejects_a_body_that_mixes_both_shapes()
    {
        var request = new UpdateZoneSettingRequest { Value = "on", Enabled = true };

        var exception = Assert.Throws<CloudflareConfigurationException>(request.EnsureValid);

        Assert.Contains("not both", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateSettingsAsync_sends_an_array_of_id_and_value_not_an_object()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""
        [{"id":"always_online","value":"on"},{"id":"browser_cache_ttl","value":14400}]
        """);

        var updated = await harness.Client.Zones.UpdateSettingsAsync(new[]
        {
            ZoneSettingChange.OnOff(ZoneSettingIds.AlwaysOnline, on: true),
            ZoneSettingChange.Of("browser_cache_ttl", 14400),
        });

        Assert.Equal(HttpMethod.Patch, harness.LastRequest.Method);
        Assert.Equal($"/client/v4/zones/{CloudflareHarness.TestZoneId}/settings", harness.LastRequest.Path);
        Assert.Equal("[{\"id\":\"always_online\",\"value\":\"on\"},{\"id\":\"browser_cache_ttl\",\"value\":14400}]", harness.LastRequest.Body);
        Assert.Equal(2, updated.Count);
    }

    [Fact]
    public async Task UpdateSettingsAsync_rejects_an_empty_batch_before_calling_cloudflare()
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Zones.UpdateSettingsAsync(Array.Empty<ZoneSettingChange>()));

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task UpdateSettingsAsync_rejects_a_change_with_no_setting_id()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Zones.UpdateSettingsAsync(new[] { new ZoneSettingChange { Value = "on" } }));

        Assert.Contains("Id", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task Settings_calls_use_an_explicit_zone_id_when_one_is_given()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");

        await harness.Client.Zones.ListSettingsAsync("zone-other");

        Assert.Equal("/client/v4/zones/zone-other/settings", harness.LastRequest.Path);
    }
}

public class SslCertificatePacksTests
{
    private const string PackJson =
        "{\"id\":\"pack-1\",\"zone_id\":\"zone-5678\",\"hosts\":[\"example.com\",\"*.example.com\"]," +
        "\"status\":\"pending_validation\",\"type\":\"advanced\",\"certificate_authority\":\"lets_encrypt\"," +
        "\"priority\":2,\"signature\":\"SHA256\",\"uploaded_on\":\"2026-01-02T03:04:05Z\"," +
        "\"validation_method\":\"txt\",\"validity_days\":365}";

    [Fact]
    public async Task ListAsync_reads_the_zone_scoped_collection_with_its_query_parameters()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage($"[{PackJson}]", page: 1, totalPages: 1, count: 1, totalCount: 1);

        var page = await harness.Client.Ssl.ListAsync(new SslCertificatePackListParameters
        {
            Status = "all",
            Deploy = SslCertificatePackDeployments.Production,
            PerPage = 50,
        });

        Assert.Equal($"/client/v4/zones/{CloudflareHarness.TestZoneId}/ssl/certificate_packs", harness.LastRequest.Path);
        Assert.Contains("per_page=50", harness.LastRequest.Query, StringComparison.Ordinal);
        Assert.Contains("deploy=production", harness.LastRequest.Query, StringComparison.Ordinal);
        Assert.Contains("status=all", harness.LastRequest.Query, StringComparison.Ordinal);
        // "per_page" contains "page", so the missing page number is asserted on the whole query.
        Assert.Equal("per_page=50&deploy=production&status=all", harness.LastRequest.Query.TrimStart('?'));

        Assert.Equal(1, page.TotalCount);
        var pack = Assert.Single(page.Items);
        Assert.Equal("pack-1", pack.Id);
        Assert.Equal("zone-5678", pack.ZoneId);
        Assert.Equal(new[] { "example.com", "*.example.com" }, pack.Hosts);
        Assert.Equal(SslCertificatePackStatuses.PendingValidation, pack.Status);
        Assert.Equal(SslCertificatePackTypes.Advanced, pack.Type);
        Assert.Equal(SslCertificateAuthorities.LetsEncrypt, pack.CertificateAuthority);
        Assert.Equal(2, pack.Priority!.Value);
        Assert.Equal("SHA256", pack.Signature);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), pack.UploadedOn);
        Assert.Equal("pack-1", pack.ToString());
    }

    [Fact]
    public async Task A_newly_modelled_field_arrives_typed_and_leaves_the_extension_data()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage($"[{PackJson}]", page: 1, totalPages: 1, count: 1, totalCount: 1);

        var pack = Assert.Single((await harness.Client.Ssl.ListAsync()).Items);

        // validation_method and validity_days were extension data until this release modelled them; they now
        // arrive typed, and nothing else in that payload is unknown, so nothing is left to catch.
        Assert.Equal(SslValidationMethods.Txt, pack.ValidationMethod);
        Assert.Equal(365, pack.ValidityDays);
        Assert.Null(pack.AdditionalData);
    }

    [Fact]
    public async Task A_field_the_model_does_not_name_is_kept_rather_than_dropped()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage(
            """
            [{
              "id": "pack-1",
              "validation_method": "txt",
              "validity_days": 90,
              "some_field_this_release_does_not_know": "kept",
              "certificates": [{
                "id": "cert-1",
                "geo_restrictions": { "label": "highest_security", "some_new_region_field": 3 }
              }]
            }]
            """,
            page: 1,
            totalPages: 1,
            count: 1,
            totalCount: 1);

        var pack = Assert.Single((await harness.Client.Ssl.ListAsync()).Items);

        Assert.Equal(SslValidationMethods.Txt, pack.ValidationMethod);
        Assert.Equal(90, pack.ValidityDays);
        Assert.Equal("kept", pack.AdditionalData!["some_field_this_release_does_not_know"].GetString());
        Assert.False(pack.AdditionalData.ContainsKey("validation_method"));
        Assert.False(pack.AdditionalData.ContainsKey("validity_days"));

        // geo_restrictions is typed now, and what the model still does not name survives inside it.
        var certificate = Assert.Single(pack.Certificates!);
        Assert.Equal(SslGeoRestrictionLabels.HighestSecurity, certificate.GeoRestrictions!.Label);
        Assert.Equal(3, certificate.GeoRestrictions.AdditionalData!["some_new_region_field"].GetInt32());
        Assert.Null(certificate.AdditionalData);
    }

    [Fact]
    public async Task ListAsync_sends_no_query_at_all_when_nothing_is_set()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage("[]", page: 1, totalPages: 1, count: 0, totalCount: 0);

        var page = await harness.Client.Ssl.ListAsync();

        Assert.Equal(string.Empty, harness.LastRequest.Query);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task ListAsync_honours_an_explicit_zone_id()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage("[]", page: 1, totalPages: 1, count: 0, totalCount: 0);

        await harness.Client.Ssl.ListAsync(null, "zone-other");

        Assert.Equal("/client/v4/zones/zone-other/ssl/certificate_packs", harness.LastRequest.Path);
    }

    [Fact]
    public async Task ListAllAsync_walks_every_page_and_keeps_the_filters()
    {
        using var harness = CloudflareHarness.Create();
        harness
            .RespondWithPage("[{\"id\":\"pack-1\"}]", page: 1, totalPages: 2, count: 1, totalCount: 2)
            .RespondWithPage("[{\"id\":\"pack-2\"}]", page: 2, totalPages: 2, count: 1, totalCount: 2);

        var packs = await harness.Client.Ssl
            .ListAllAsync(new SslCertificatePackListParameters { Status = "all", PerPage = 5 })
            .CollectAsync();

        Assert.Equal(new[] { "pack-1", "pack-2" }, packs.Select(pack => pack.Id));
        Assert.Equal(2, harness.RequestCount);
        Assert.Equal("page=1&per_page=5&status=all", harness.Requests[0].Query.TrimStart('?'));
        Assert.Equal("page=2&per_page=5&status=all", harness.Requests[1].Query.TrimStart('?'));
    }

    [Fact]
    public async Task ListAllAsync_stops_on_a_short_page_even_without_result_info()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[{\"id\":\"pack-1\"}]");

        var packs = await harness.Client.Ssl.ListAllAsync().CollectAsync();

        Assert.Equal("pack-1", Assert.Single(packs).Id);
        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task GetAsync_scopes_the_call_to_one_pack()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(PackJson);

        var pack = await harness.Client.Ssl.GetAsync("pack-1");

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Equal(
            $"/client/v4/zones/{CloudflareHarness.TestZoneId}/ssl/certificate_packs/pack-1",
            harness.LastRequest.Path);
        Assert.Equal("pack-1", pack?.Id);
    }

    [Fact]
    public async Task GetAsync_returns_null_when_the_zone_has_no_such_pack()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.NotFound, 10007, "Certificate pack not found");

        Assert.Null(await harness.Client.Ssl.GetAsync("missing"));
    }

    [Fact]
    public async Task GetAsync_percent_encodes_the_pack_id()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(PackJson);

        await harness.Client.Ssl.GetAsync("pack/1");

        Assert.Contains("/ssl/certificate_packs/pack%2F1", harness.LastRequest.Uri!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteAsync_returns_the_identifier_cloudflare_echoes()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"pack-1\"}");

        var deleted = await harness.Client.Ssl.DeleteAsync("pack-1");

        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.Equal(
            $"/client/v4/zones/{CloudflareHarness.TestZoneId}/ssl/certificate_packs/pack-1",
            harness.LastRequest.Path);
        Assert.Equal("pack-1", deleted);
    }

    [Fact]
    public async Task DeleteAsync_returns_null_when_cloudflare_sends_no_result()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("null");

        Assert.Null(await harness.Client.Ssl.DeleteAsync("pack-1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_pack_id_is_required(string? packId)
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<CloudflareConfigurationException>(() => harness.Client.Ssl.GetAsync(packId!));
        await Assert.ThrowsAsync<CloudflareConfigurationException>(() => harness.Client.Ssl.DeleteAsync(packId!));

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public void The_list_parameters_only_send_what_was_set()
    {
        var query = new QueryStringBuilder();
        new SslCertificatePackListParameters { Page = 3, PerPage = 20, Deploy = "staging" }.Apply(query);

        Assert.Equal("page=3&per_page=20&deploy=staging", query.ToString().TrimStart('?'));
    }
}

public class SslCertificatePackOrderTests
{
    private const string OrderPath = "/client/v4/zones/zone-5678/ssl/certificate_packs/order";

    private static OrderSslCertificatePackRequest ValidOrder(bool? branding = false) =>
        new(
            new[] { "example.com", "www.example.com" },
            SslValidationMethods.Txt,
            SslCertificatePackValidityDays.OneYear,
            SslCertificateAuthorities.LetsEncrypt,
            branding);

    [Fact]
    public async Task OrderAsync_posts_every_field_to_the_order_path()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """
            {
              "id": "pack-9",
              "hosts": ["example.com", "www.example.com"],
              "status": "initializing",
              "type": "advanced",
              "certificate_authority": "lets_encrypt",
              "certificates": [
                {
                  "id": "cert-1",
                  "hosts": ["example.com"],
                  "status": "initializing",
                  "bundle_method": "ubiquitous",
                  "expires_on": "2027-01-02T03:04:05Z",
                  "geo_restrictions": {"label": "eu"}
                }
              ]
            }
            """);

        var pack = await harness.Client.Ssl.OrderAsync(ValidOrder());

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.Equal(OrderPath, harness.LastRequest.Path);
        Assert.Equal(
            "{\"type\":\"advanced\",\"hosts\":[\"example.com\",\"www.example.com\"]," +
            "\"validation_method\":\"txt\",\"validity_days\":365," +
            "\"certificate_authority\":\"lets_encrypt\",\"cloudflare_branding\":false}",
            harness.LastRequest.Body);

        Assert.Equal("pack-9", pack.Id);
        Assert.Equal(SslCertificatePackStatuses.Initializing, pack.Status);

        var certificate = Assert.Single(pack.Certificates!);
        Assert.Equal("cert-1", certificate.Id);
        Assert.Equal(new[] { "example.com" }, certificate.Hosts);
        Assert.Equal("ubiquitous", certificate.BundleMethod);
        Assert.Equal(new DateTimeOffset(2027, 1, 2, 3, 4, 5, TimeSpan.Zero), certificate.ExpiresOn);
        Assert.Equal(SslGeoRestrictionLabels.Eu, certificate.GeoRestrictions!.Label);
    }

    [Fact]
    public async Task OrderAsync_leaves_branding_out_when_it_was_not_chosen()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"pack-9\"}");

        await harness.Client.Ssl.OrderAsync(ValidOrder(branding: null));

        Assert.DoesNotContain("cloudflare_branding", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"validity_days\":365", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OrderAsync_honours_an_explicit_zone_id()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"pack-9\"}");

        await harness.Client.Ssl.OrderAsync(ValidOrder(), "zone-other");

        Assert.Equal("/client/v4/zones/zone-other/ssl/certificate_packs/order", harness.LastRequest.Path);
    }

    [Fact]
    public async Task OrderAsync_checks_every_field_before_calling_out()
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<CloudflareConfigurationException>(() => harness.Client.Ssl.OrderAsync(
            new OrderSslCertificatePackRequest(Array.Empty<string>(), "txt", 365, "lets_encrypt")));

        await Assert.ThrowsAsync<CloudflareConfigurationException>(() => harness.Client.Ssl.OrderAsync(
            new OrderSslCertificatePackRequest(
                Enumerable.Repeat("host.example.com", OrderSslCertificatePackRequest.MaxHosts + 1),
                "txt", 365, "lets_encrypt")));

        await Assert.ThrowsAsync<CloudflareConfigurationException>(() => harness.Client.Ssl.OrderAsync(
            new OrderSslCertificatePackRequest(new[] { "example.com", " " }, "txt", 365, "lets_encrypt")));

        await Assert.ThrowsAsync<CloudflareConfigurationException>(() => harness.Client.Ssl.OrderAsync(
            new OrderSslCertificatePackRequest(new[] { "example.com" }, "dns", 365, "lets_encrypt")));

        // 730 and 1095 belong to request_validity, not to the validity_days an order accepts.
        await Assert.ThrowsAsync<CloudflareConfigurationException>(() => harness.Client.Ssl.OrderAsync(
            new OrderSslCertificatePackRequest(new[] { "example.com" }, "txt", 730, "lets_encrypt")));

        await Assert.ThrowsAsync<CloudflareConfigurationException>(() => harness.Client.Ssl.OrderAsync(
            new OrderSslCertificatePackRequest(new[] { "example.com" }, "txt", 365, "digicert")));

        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Ssl.OrderAsync(new OrderSslCertificatePackRequest
            {
                Type = SslCertificatePackTypes.Universal,
                Hosts = new List<string> { "example.com" },
                ValidationMethod = SslValidationMethods.Txt,
                ValidityDays = 365,
                CertificateAuthority = SslCertificateAuthorities.Google,
            }));

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task OrderAsync_needs_a_request()
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<ArgumentNullException>(() => harness.Client.Ssl.OrderAsync(null!));

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task An_order_without_a_result_payload_raises()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("null");

        var exception = await Assert.ThrowsAsync<CloudflareApiException>(() => harness.Client.Ssl.OrderAsync(ValidOrder()));

        Assert.Contains("without a result payload", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_order_constants_list_exactly_what_cloudflare_accepts()
    {
        Assert.Equal(new[] { "txt", "http", "email" }, SslValidationMethods.All);
        Assert.Equal(new[] { 14, 30, 90, 365 }, SslCertificatePackValidityDays.All);
        Assert.Equal(new[] { "google", "lets_encrypt", "ssl_com" }, SslCertificateAuthorities.All);
        Assert.Equal(50, OrderSslCertificatePackRequest.MaxHosts);
        Assert.Equal("advanced", SslCertificatePackTypes.Advanced);
    }

    [Fact]
    public void An_order_always_names_the_advanced_type()
    {
        Assert.Equal(SslCertificatePackTypes.Advanced, ValidOrder().Type);
    }
}

public class SslCertificatePackUpdateTests
{
    private const string PackPath = "/client/v4/zones/zone-5678/ssl/certificate_packs/pack-1";

    [Fact]
    public async Task SetCloudflareBrandingAsync_patches_the_pack()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"pack-1\",\"status\":\"pending_validation\"}");

        var pack = await harness.Client.Ssl.SetCloudflareBrandingAsync("pack-1", true);

        Assert.Equal(HttpMethod.Patch, harness.LastRequest.Method);
        Assert.Equal(PackPath, harness.LastRequest.Path);
        Assert.Equal("{\"cloudflare_branding\":true}", harness.LastRequest.Body);
        Assert.Equal("pack-1", pack.Id);
    }

    [Fact]
    public async Task SetCloudflareBrandingAsync_can_take_branding_off_again()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"pack-1\"}");

        await harness.Client.Ssl.SetCloudflareBrandingAsync("pack-1", false);

        // false has to be sent explicitly: omitting the field would leave the setting alone.
        Assert.Equal("{\"cloudflare_branding\":false}", harness.LastRequest.Body);
    }

    [Fact]
    public async Task RestartValidationAsync_sends_an_empty_body()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"pack-1\",\"status\":\"pending_validation\"}");

        var pack = await harness.Client.Ssl.RestartValidationAsync("pack-1");

        Assert.Equal(HttpMethod.Patch, harness.LastRequest.Method);
        Assert.Equal(PackPath, harness.LastRequest.Path);
        // An empty object is how "restart validation" is spelled: cloudflare_branding is the only field
        // this endpoint accepts, so sending nothing else is what asks for the validation restart.
        Assert.Equal("{}", harness.LastRequest.Body);
        Assert.Equal(SslCertificatePackStatuses.PendingValidation, pack.Status);
    }

    [Fact]
    public async Task UpdateAsync_percent_encodes_the_pack_id()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"pack/1\"}");

        await harness.Client.Ssl.UpdateAsync("pack/1", UpdateSslCertificatePackRequest.ToRestartValidation());

        Assert.Contains("/ssl/certificate_packs/pack%2F1", harness.LastRequest.Uri!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateAsync_needs_a_pack_id_and_a_request()
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Ssl.UpdateAsync("  ", UpdateSslCertificatePackRequest.ToRestartValidation()));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => harness.Client.Ssl.UpdateAsync("pack-1", null!));
        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Ssl.RestartValidationAsync(""));

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task An_update_without_a_result_payload_raises()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("null");

        var exception = await Assert.ThrowsAsync<CloudflareApiException>(
            () => harness.Client.Ssl.SetCloudflareBrandingAsync("pack-1", true));

        Assert.Contains("without a result payload", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_update_request_knows_which_of_the_two_operations_it_is()
    {
        Assert.True(UpdateSslCertificatePackRequest.ToRestartValidation().IsEmpty);
        Assert.False(UpdateSslCertificatePackRequest.ForCloudflareBranding(false).IsEmpty);
        Assert.Equal("restart validation", UpdateSslCertificatePackRequest.ToRestartValidation().ToString());
        Assert.Equal("cloudflare_branding=True", UpdateSslCertificatePackRequest.ForCloudflareBranding(true).ToString());
    }

    [Fact]
    public void The_status_constants_cover_the_issuance_steps()
    {
        Assert.Equal("pending_validation", SslCertificatePackStatuses.PendingValidation);
        Assert.Equal("validation_timed_out", SslCertificatePackStatuses.ValidationTimedOut);
        Assert.Equal("pending_issuance", SslCertificatePackStatuses.PendingIssuance);
        Assert.Equal("pending_deployment", SslCertificatePackStatuses.PendingDeployment);
        Assert.Equal("backup_issued", SslCertificatePackStatuses.BackupIssued);
    }
}

public class UniversalSslSettingsTests
{
    private const string SettingsPath = "/client/v4/zones/zone-5678/ssl/universal/settings";

    [Fact]
    public async Task GetUniversalSettingsAsync_reads_the_switch()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"enabled\":true}");

        var settings = await harness.Client.Ssl.GetUniversalSettingsAsync();

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Equal(SettingsPath, harness.LastRequest.Path);
        Assert.Equal(string.Empty, harness.LastRequest.Query);
        Assert.True(settings.Enabled);
        Assert.True(settings.IsEnabled);
        Assert.Equal("enabled", settings.ToString());
    }

    [Fact]
    public async Task GetUniversalSettingsAsync_reports_a_zone_that_turned_it_off()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"enabled\":false}");

        var settings = await harness.Client.Ssl.GetUniversalSettingsAsync();

        Assert.False(settings.Enabled);
        Assert.False(settings.IsEnabled);
        Assert.Equal("disabled", settings.ToString());
    }

    [Fact]
    public async Task GetUniversalSettingsAsync_says_unknown_when_cloudflare_omits_the_switch()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{}");

        var settings = await harness.Client.Ssl.GetUniversalSettingsAsync();

        Assert.Null(settings.Enabled);
        Assert.False(settings.IsEnabled);
        Assert.Equal("unknown", settings.ToString());
        Assert.Equal("unknown", new UniversalSslSettings().ToString());
    }

    [Fact]
    public async Task SetUniversalSslAsync_sends_the_switch_it_was_given()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"enabled\":false}");

        await harness.Client.Ssl.SetUniversalSslAsync(false);

        Assert.Equal(HttpMethod.Patch, harness.LastRequest.Method);
        Assert.Equal(SettingsPath, harness.LastRequest.Path);
        // false has to be in the body: an empty PATCH would leave the setting alone.
        Assert.Equal("{\"enabled\":false}", harness.LastRequest.Body);
    }

    [Fact]
    public async Task SetUniversalSslAsync_can_turn_it_back_on()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"enabled\":true}");

        var settings = await harness.Client.Ssl.SetUniversalSslAsync(true, "zone-other");

        Assert.Equal("/client/v4/zones/zone-other/ssl/universal/settings", harness.LastRequest.Path);
        Assert.Equal("{\"enabled\":true}", harness.LastRequest.Body);
        Assert.True(settings.IsEnabled);
    }

    [Fact]
    public async Task Universal_settings_without_a_result_payload_raise()
    {
        using var harness = CloudflareHarness.Create();
        harness
            .RespondWithResult("null")
            .RespondWithResult("null");

        var get = await Assert.ThrowsAsync<CloudflareApiException>(() => harness.Client.Ssl.GetUniversalSettingsAsync());
        var set = await Assert.ThrowsAsync<CloudflareApiException>(() => harness.Client.Ssl.SetUniversalSslAsync(true));

        Assert.Contains("without a result payload", get.Message, StringComparison.Ordinal);
        Assert.Contains("without a result payload", set.Message, StringComparison.Ordinal);
        Assert.Equal(2, harness.RequestCount);
    }
}

public class SslCertificatePackQuotaTests
{
    private const string QuotaPath = "/client/v4/zones/zone-5678/ssl/certificate_packs/quota";

    [Fact]
    public async Task GetQuotaAsync_reads_the_advanced_certificate_quota()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"advanced\":{\"allocated\":10,\"used\":3}}");

        var quota = await harness.Client.Ssl.GetQuotaAsync();

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Equal(QuotaPath, harness.LastRequest.Path);
        Assert.Equal(10, quota.Advanced!.Allocated);
        Assert.Equal(3, quota.Advanced.Used);
        Assert.Equal(7, quota.Remaining);
        Assert.Equal("advanced 3/10", quota.ToString());
        Assert.Equal("3/10", quota.Advanced.ToString());
    }

    [Fact]
    public async Task GetQuotaAsync_reports_no_quota_when_the_zone_has_none()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{}");

        var quota = await harness.Client.Ssl.GetQuotaAsync();

        Assert.Null(quota.Advanced);
        Assert.Null(quota.Remaining);
        Assert.Equal("no quota reported", quota.ToString());
        Assert.Equal("0/0", new SslAdvancedCertificateQuota().ToString());
    }

    [Fact]
    public async Task Remaining_needs_both_halves_of_the_quota()
    {
        using var harness = CloudflareHarness.Create();
        harness
            .RespondWithResult("{\"advanced\":{\"allocated\":10}}")
            .RespondWithResult("{\"advanced\":{\"used\":4}}");

        Assert.Null((await harness.Client.Ssl.GetQuotaAsync()).Remaining);
        Assert.Null((await harness.Client.Ssl.GetQuotaAsync()).Remaining);
    }

    [Fact]
    public async Task GetQuotaAsync_honours_an_explicit_zone_id()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"advanced\":{\"allocated\":1,\"used\":1}}");

        var quota = await harness.Client.Ssl.GetQuotaAsync("zone-other");

        Assert.Equal("/client/v4/zones/zone-other/ssl/certificate_packs/quota", harness.LastRequest.Path);
        Assert.Equal(0, quota.Remaining);
    }

    [Fact]
    public async Task A_quota_without_a_result_payload_raises()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("null");

        var exception = await Assert.ThrowsAsync<CloudflareApiException>(() => harness.Client.Ssl.GetQuotaAsync());

        Assert.Contains("without a result payload", exception.Message, StringComparison.Ordinal);
    }
}

public class SslCertificateValidationTests
{
    private const string PackJson =
        """
        {
          "id": "pack-1",
          "hosts": ["example.com", "*.example.com"],
          "status": "pending_validation",
          "type": "advanced",
          "certificate_authority": "lets_encrypt",
          "validation_method": "txt",
          "validity_days": 90,
          "created_on": "2026-05-05T13:00:15Z",
          "primary_certificate": "cert-1",
          "validation_records": [
            {
              "txt_name": "_acme-challenge.example.com",
              "txt_value": "ca3-abcdef",
              "cname": "_acme-challenge.example.com",
              "cname_target": "dcv.cloudflare.com",
              "emails": ["admin@example.com"],
              "status": "pending",
              "http_url": "http://example.com/.well-known/pki-validation/x.txt"
            }
          ],
          "validation_errors": [{"message": "DNS record missing for example.com"}]
        }
        """;

    [Fact]
    public async Task A_pack_carries_everything_needed_to_answer_the_challenge()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(PackJson);

        var pack = await harness.Client.Ssl.GetAsync("pack-1");

        Assert.Equal(SslValidationMethods.Txt, pack!.ValidationMethod);
        Assert.Equal(90, pack.ValidityDays);
        Assert.Equal(new DateTimeOffset(2026, 5, 5, 13, 0, 15, TimeSpan.Zero), pack.CreatedOn);
        Assert.Equal("cert-1", pack.PrimaryCertificate);

        var record = Assert.Single(pack.ValidationRecords!);
        Assert.Equal("_acme-challenge.example.com", record.TxtName);
        Assert.Equal("ca3-abcdef", record.TxtValue);
        Assert.Equal("_acme-challenge.example.com", record.Cname);
        Assert.Equal("dcv.cloudflare.com", record.CnameTarget);
        Assert.Equal(new[] { "admin@example.com" }, record.Emails);
        Assert.Equal("pending", record.Status);
        Assert.Equal("_acme-challenge.example.com", record.ToString());
        // The HTTP challenge detail is not modelled, so it survives in the extension data.
        Assert.Contains("http_url", record.AdditionalData!.Keys, StringComparer.Ordinal);

        var error = Assert.Single(pack.ValidationErrors!);
        Assert.Equal("DNS record missing for example.com", error.Message);
        Assert.Equal("DNS record missing for example.com", error.ToString());
    }

    [Fact]
    public async Task The_txt_challenge_can_be_handed_straight_to_the_dns_service()
    {
        using var harness = CloudflareHarness.Create();
        harness
            .RespondWithResult(PackJson)
            .RespondWithResult("{\"id\":\"rec-1\",\"type\":\"TXT\",\"name\":\"_acme-challenge.example.com\"}");

        var pack = await harness.Client.Ssl.GetAsync("pack-1");
        var record = Assert.Single(pack!.ValidationRecords!);

        await harness.Client.DnsRecords.CreateTxtRecordAsync(record.TxtName!, record.TxtValue!);

        Assert.Equal("/client/v4/zones/zone-5678/dns_records", harness.LastRequest.Path);
        Assert.Contains("_acme-challenge.example.com", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.Contains("ca3-abcdef", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"TXT\"", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pack_with_no_validation_details_yet_reads_as_empty()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"pack-2\",\"status\":\"initializing\"}");

        var pack = await harness.Client.Ssl.GetAsync("pack-2");

        Assert.Null(pack!.ValidationRecords);
        Assert.Null(pack.ValidationErrors);
        Assert.Null(pack.PrimaryCertificate);
        Assert.Null(pack.ValidityDays);
    }
}

public class SslVerificationTests
{
    private const string VerificationPath = "/client/v4/zones/zone-5678/ssl/verification";

    private const string VerificationJson =
        """
        [
          {
            "cert_pack_uuid": "pack-1",
            "certificate_status": "authorizing",
            "validation_method": "txt",
            "verification_status": false,
            "verification_type": "cname",
            "brand_check": false,
            "signature": "SHA256WithRSA",
            "verification_info": {
              "record_name": "_acme-challenge.example.com",
              "record_target": "ca3-abcdef",
              "some_new_field": 7
            }
          },
          {
            "cert_pack_uuid": "pack-1",
            "certificate_status": "active",
            "verification_status": true,
            "verification_info": { "policy_email": "admin@example.com" }
          }
        ]
        """;

    [Fact]
    public async Task GetVerificationAsync_reads_the_zone_scoped_array()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(VerificationJson);

        var entries = await harness.Client.Ssl.GetVerificationAsync();

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Equal(VerificationPath, harness.LastRequest.Path);
        Assert.Equal(string.Empty, harness.LastRequest.Query);

        Assert.Equal(2, entries.Count);
        var pending = entries[0];
        Assert.Equal("pack-1", pending.CertificatePackId);
        Assert.Equal(SslVerificationCertificateStatuses.Authorizing, pending.CertificateStatus);
        Assert.Equal(SslValidationMethods.Txt, pending.ValidationMethod);
        Assert.Equal(SslVerificationTypes.Cname, pending.VerificationType);
        Assert.False(pending.BrandCheck);
        Assert.Equal("SHA256WithRSA", pending.Signature);
        Assert.False(pending.VerificationStatus);
        Assert.False(pending.IsVerified);
        Assert.Equal("pack-1: authorizing", pending.ToString());

        var active = entries[1];
        Assert.Equal(SslVerificationCertificateStatuses.Active, active.CertificateStatus);
        Assert.True(active.IsVerified);
    }

    [Fact]
    public async Task Verification_info_names_the_record_the_authority_is_waiting_for()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(VerificationJson);

        var entries = await harness.Client.Ssl.GetVerificationAsync();

        var info = entries[0].VerificationInfo!;
        Assert.Equal("_acme-challenge.example.com", info.RecordName);
        Assert.Equal("ca3-abcdef", info.RecordTarget);
        Assert.Null(info.PolicyEmail);
        Assert.Equal("_acme-challenge.example.com -> ca3-abcdef", info.ToString());
        // An unknown field inside verification_info survives rather than being dropped.
        Assert.Equal(7, info.AdditionalData!["some_new_field"].GetInt32());

        Assert.Equal("admin@example.com", entries[1].VerificationInfo!.PolicyEmail);
        Assert.Equal("(no record)", entries[1].VerificationInfo.ToString());
    }

    [Fact]
    public async Task GetVerificationAsync_sends_retry_only_when_asked()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]").RespondWithResult("[]");

        await harness.Client.Ssl.GetVerificationAsync();
        Assert.Equal(string.Empty, harness.LastRequest.Query);

        await harness.Client.Ssl.GetVerificationAsync(retry: true);
        Assert.Equal("retry=true", harness.LastRequest.Query.TrimStart('?'));
    }

    [Fact]
    public async Task GetVerificationAsync_reads_an_empty_zone_as_an_empty_list()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");

        Assert.Empty(await harness.Client.Ssl.GetVerificationAsync());
    }

    [Fact]
    public async Task GetVerificationAsync_honours_an_explicit_zone_id()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");

        await harness.Client.Ssl.GetVerificationAsync(zoneId: "zone-other");

        Assert.Equal("/client/v4/zones/zone-other/ssl/verification", harness.LastRequest.Path);
    }

    [Fact]
    public async Task UpdateValidationMethodAsync_switches_the_method_and_validates_immediately()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"status\":\"pending_validation\",\"validation_method\":\"cname\"}");

        var update = await harness.Client.Ssl.UpdateValidationMethodAsync("pack-1", SslValidationMethods.Cname);

        Assert.Equal(HttpMethod.Patch, harness.LastRequest.Method);
        Assert.Equal($"{VerificationPath}/pack-1", harness.LastRequest.Path);
        Assert.Contains("\"validation_method\":\"cname\"", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.Equal("pending_validation", update.Status);
        Assert.Equal(SslValidationMethods.Cname, update.ValidationMethod);
        Assert.Equal("pending_validation via cname", update.ToString());
    }

    [Fact]
    public async Task RevalidateAsync_sends_an_empty_body_to_ask_for_a_recheck()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"status\":\"pending_validation\"}");

        var update = await harness.Client.Ssl.RevalidateAsync("pack-1");

        Assert.Equal(HttpMethod.Patch, harness.LastRequest.Method);
        Assert.Equal($"{VerificationPath}/pack-1", harness.LastRequest.Path);
        Assert.Equal("{}", harness.LastRequest.Body);
        Assert.Equal("pending_validation via the pack's current method", update.ToString());
        Assert.True(UpdateSslValidationMethodRequest.RevalidateNow().IsEmpty);
        Assert.False(UpdateSslValidationMethodRequest.WithMethod("txt").IsEmpty);
        Assert.Equal("revalidate now", UpdateSslValidationMethodRequest.RevalidateNow().ToString());
    }

    [Fact]
    public async Task A_blank_validation_method_is_rejected_before_any_request()
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Ssl.UpdateValidationMethodAsync("pack-1", "  "));

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task A_validation_method_update_without_a_result_payload_raises()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("null");

        var exception = await Assert.ThrowsAsync<CloudflareApiException>(
            () => harness.Client.Ssl.RevalidateAsync("pack-1"));

        Assert.Contains("without a result payload", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Dcv_delegation_is_available_for_validation_but_not_for_ordering()
    {
        // cname is a fourth value on the validation-method PATCH only; an order still rejects it.
        Assert.Equal(new[] { "txt", "http", "email" }, SslValidationMethods.All);
        Assert.DoesNotContain(SslValidationMethods.Cname, SslValidationMethods.All);

        var order = new OrderSslCertificatePackRequest(
            new[] { "example.com" },
            SslValidationMethods.Cname,
            365,
            SslCertificateAuthorities.LetsEncrypt);

        var exception = Assert.Throws<CloudflareConfigurationException>(() => order.EnsureValid());
        Assert.Contains("not one of txt, http, email", exception.Message, StringComparison.Ordinal);
    }
}

public class SslEnrolmentSettingsTests
{
    private const string KexPath = "/client/v4/zones/zone-5678/settings/auto_origin_tls_kex";
    private const string AutomaticModePath = "/client/v4/zones/zone-5678/settings/ssl_automatic_mode";

    [Fact]
    public async Task GetAutoOriginTlsKexAsync_reads_the_zone_setting()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            "{\"id\":\"auto_origin_tls_kex\",\"enabled\":true,\"modified_on\":\"2026-05-05T13:00:15Z\"}");

        var setting = await harness.Client.Ssl.GetAutoOriginTlsKexAsync();

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Equal(KexPath, harness.LastRequest.Path);
        Assert.Equal(string.Empty, harness.LastRequest.Query);
        Assert.Equal("auto_origin_tls_kex", setting.Id);
        Assert.True(setting.IsEnabled);
        Assert.Equal(new DateTimeOffset(2026, 5, 5, 13, 0, 15, TimeSpan.Zero), setting.ModifiedOn);
        Assert.Equal("enabled", setting.ToString());
    }

    [Fact]
    public async Task SetAutoOriginTlsKexAsync_sends_enabled_in_both_directions()
    {
        using var harness = CloudflareHarness.Create();
        harness
            .RespondWithResult("{\"id\":\"auto_origin_tls_kex\",\"enabled\":false}")
            .RespondWithResult("{\"id\":\"auto_origin_tls_kex\",\"enabled\":true}");

        var off = await harness.Client.Ssl.SetAutoOriginTlsKexAsync(false);
        Assert.Equal(HttpMethod.Patch, harness.LastRequest.Method);
        Assert.Equal(KexPath, harness.LastRequest.Path);
        // Turning it off has to be said out loud: an empty body would leave the setting alone.
        Assert.Equal("{\"enabled\":false}", harness.LastRequest.Body);
        Assert.False(off.IsEnabled);
        Assert.Equal("disabled", off.ToString());

        var on = await harness.Client.Ssl.SetAutoOriginTlsKexAsync(true);
        Assert.Equal("{\"enabled\":true}", harness.LastRequest.Body);
        Assert.True(on.IsEnabled);
    }

    [Fact]
    public void Auto_origin_tls_kex_without_an_enabled_flag_is_neither_on_nor_off()
    {
        var setting = new AutoOriginTlsKex();

        Assert.Null(setting.Enabled);
        Assert.False(setting.IsEnabled);
        Assert.Equal("unknown", setting.ToString());

        // The schema makes "enabled" required, so a body without it is refused here rather than at Cloudflare.
        Assert.Throws<CloudflareConfigurationException>(() => new UpdateAutoOriginTlsKexRequest().EnsureValid());
        UpdateAutoOriginTlsKexRequest.With(false).EnsureValid();
    }

    [Fact]
    public async Task GetAutomaticSslAsync_reads_the_enrolment_and_the_next_scan()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """
            {
              "id": "ssl_automatic_mode",
              "editable": true,
              "modified_on": "2026-01-02T03:04:05Z",
              "value": "auto",
              "next_scheduled_scan": "2026-02-01T05:20:00Z"
            }
            """);

        var mode = await harness.Client.Ssl.GetAutomaticSslAsync();

        Assert.Equal(AutomaticModePath, harness.LastRequest.Path);
        Assert.Equal("ssl_automatic_mode", mode.Id);
        Assert.True(mode.Editable);
        Assert.Equal(SslAutomaticModes.Auto, mode.Value);
        Assert.True(mode.IsAutomatic);
        Assert.Equal(new DateTimeOffset(2026, 2, 1, 5, 20, 0, TimeSpan.Zero), mode.NextScheduledScan);
        Assert.Equal("auto", mode.ToString());
    }

    [Fact]
    public async Task Enable_and_disable_automatic_ssl_send_auto_and_custom()
    {
        using var harness = CloudflareHarness.Create();
        harness
            .RespondWithResult("{\"value\":\"auto\"}")
            .RespondWithResult("{\"value\":\"custom\"}");

        var enrolled = await harness.Client.Ssl.EnableAutomaticSslAsync();
        Assert.Equal(HttpMethod.Patch, harness.LastRequest.Method);
        Assert.Equal(AutomaticModePath, harness.LastRequest.Path);
        Assert.Equal("{\"value\":\"auto\"}", harness.LastRequest.Body);
        Assert.True(enrolled.IsAutomatic);

        var optedOut = await harness.Client.Ssl.DisableAutomaticSslAsync();
        Assert.Equal("{\"value\":\"custom\"}", harness.LastRequest.Body);
        Assert.False(optedOut.IsAutomatic);
        Assert.Equal("custom", optedOut.ToString());
    }

    [Fact]
    public async Task SetAutomaticSslAsync_sends_an_explicit_value()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"value\":\"custom\",\"next_scheduled_scan\":null}");

        var mode = await harness.Client.Ssl.SetAutomaticSslAsync(SslAutomaticModes.Custom);

        Assert.Equal("{\"value\":\"custom\"}", harness.LastRequest.Body);
        Assert.Null(mode.NextScheduledScan);
    }

    [Fact]
    public async Task An_unknown_automatic_mode_is_refused_before_any_request()
    {
        using var harness = CloudflareHarness.Create();

        // Cloudflare answers this with error 1008, so the library says it first.
        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Ssl.SetAutomaticSslAsync("full"));
        Assert.Contains("not one of auto, custom", exception.Message, StringComparison.Ordinal);

        var blank = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Ssl.SetAutomaticSslAsync("  "));
        Assert.Contains("must not be null or empty", blank.Message, StringComparison.Ordinal);

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task GetRecommendationAsync_uses_the_singular_deprecated_path()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"ssl_automatic_mode\",\"value\":\"custom\",\"editable\":false}");

        var recommendation = await harness.Client.Ssl.GetRecommendationAsync();

        Assert.Equal("/client/v4/zones/zone-5678/ssl/recommendation", harness.LastRequest.Path);
        Assert.DoesNotContain("recommendations", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal("custom", recommendation.Value);
        Assert.False(recommendation.IsAutomatic);
    }

    [Fact]
    public async Task Enrolment_settings_keep_fields_this_release_does_not_name()
    {
        using var harness = CloudflareHarness.Create();
        harness
            .RespondWithResult("{\"enabled\":true,\"scan_interval_days\":7}")
            .RespondWithResult("{\"value\":\"auto\",\"recommended_mode\":\"full\"}");

        var kex = await harness.Client.Ssl.GetAutoOriginTlsKexAsync();
        Assert.Equal(7, kex.AdditionalData!["scan_interval_days"].GetInt32());

        var mode = await harness.Client.Ssl.GetAutomaticSslAsync();
        Assert.Equal("full", mode.AdditionalData!["recommended_mode"].GetString());
    }

    [Fact]
    public async Task Enrolment_settings_honour_an_explicit_zone_id()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"enabled\":false}").RespondWithResult("{\"value\":\"auto\"}");

        await harness.Client.Ssl.GetAutoOriginTlsKexAsync("zone-other");
        Assert.Equal("/client/v4/zones/zone-other/settings/auto_origin_tls_kex", harness.LastRequest.Path);

        await harness.Client.Ssl.GetAutomaticSslAsync("zone-other");
        Assert.Equal("/client/v4/zones/zone-other/settings/ssl_automatic_mode", harness.LastRequest.Path);
    }

    [Fact]
    public async Task An_enrolment_setting_without_a_result_payload_raises()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("null");

        var exception = await Assert.ThrowsAsync<CloudflareApiException>(
            () => harness.Client.Ssl.GetAutoOriginTlsKexAsync());

        Assert.Contains("without a result payload", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_setting_ids_are_spelled_the_way_the_paths_are()
    {
        Assert.Equal("auto_origin_tls_kex", ZoneSettingIds.AutoOriginTlsKex);
        Assert.Equal("ssl_automatic_mode", ZoneSettingIds.SslAutomaticMode);
        Assert.Equal(new[] { "auto", "custom" }, SslAutomaticModes.All);
    }
}

public class SslCertificateAnalysisTests
{
    private const string AnalyzePath = "/client/v4/zones/zone-5678/ssl/analyze";
    private const string FakePem = "-----BEGIN CERTIFICATE----- MIIBfake MIIBfake -----END CERTIFICATE-----";

    [Fact]
    public async Task AnalyzeCertificateAsync_posts_the_pem_and_returns_the_answer_as_raw_json()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """
            {
              "hosts": ["example.com", "www.example.com"],
              "signature": "SHA256WithRSA",
              "expires_on": "2027-01-02T03:04:05Z"
            }
            """);

        var analysis = await harness.Client.Ssl.AnalyzeCertificateAsync(
            AnalyzeSslCertificateRequest.ForCertificate(FakePem, SslBundleMethods.Ubiquitous));

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.Equal(AnalyzePath, harness.LastRequest.Path);
        Assert.Contains("\"certificate\":\"-----BEGIN CERTIFICATE-----", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"bundle_method\":\"ubiquitous\"", harness.LastRequest.Body, StringComparison.Ordinal);

        // The schema types this result as unknown, so the caller reads it rather than the library guessing.
        Assert.NotNull(analysis);
        Assert.Equal(JsonValueKind.Object, analysis!.Value.ValueKind);
        Assert.Equal("SHA256WithRSA", analysis.Value.GetProperty("signature").GetString());
        Assert.Equal(2, analysis.Value.GetProperty("hosts").GetArrayLength());
        Assert.Equal("example.com", analysis.Value.GetProperty("hosts")[0].GetString());
        Assert.Equal("2027-01-02T03:04:05Z", analysis.Value.GetProperty("expires_on").GetString());
    }

    [Fact]
    public async Task AnalyzeCertificateAsync_leaves_the_bundle_method_out_when_it_was_not_chosen()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"signature\":\"SHA256WithRSA\"}");

        await harness.Client.Ssl.AnalyzeCertificateAsync(AnalyzeSslCertificateRequest.ForCertificate(FakePem));

        Assert.DoesNotContain("bundle_method", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzeCertificateAsync_reads_a_response_that_carries_no_result_as_null()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("null");

        Assert.Null(await harness.Client.Ssl.AnalyzeCertificateAsync(
            AnalyzeSslCertificateRequest.ForCertificate(FakePem)));
    }

    [Fact]
    public async Task AnalyzeCertificateAsync_refuses_an_unknown_bundle_method_before_any_request()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Ssl.AnalyzeCertificateAsync(
                AnalyzeSslCertificateRequest.ForCertificate(FakePem, "shortest")));

        Assert.Contains("not one of ubiquitous, optimal, force", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public void A_certificate_that_was_set_but_is_blank_is_refused_locally()
    {
        var request = AnalyzeSslCertificateRequest.ForCertificate("   ");

        var exception = Assert.Throws<CloudflareConfigurationException>(() => request.EnsureValid());
        Assert.Contains("was set but is empty", exception.Message, StringComparison.Ordinal);

        // Both fields are optional in the schema, so an otherwise empty body is legitimate.
        new AnalyzeSslCertificateRequest().EnsureValid();
        Assert.Equal("(no certificate)", new AnalyzeSslCertificateRequest().ToString());
        Assert.Equal("certificate, optimal bundle", AnalyzeSslCertificateRequest
            .ForCertificate(FakePem, SslBundleMethods.Optimal).ToString());
        Assert.Equal(new[] { "ubiquitous", "optimal", "force" }, SslBundleMethods.All);
        Assert.Equal(new[] { "us", "eu", "highest_security" }, SslGeoRestrictionLabels.All);
    }

    [Fact]
    public async Task AnalyzeCertificateAsync_honours_an_explicit_zone_id()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{}");

        await harness.Client.Ssl.AnalyzeCertificateAsync(
            AnalyzeSslCertificateRequest.ForCertificate(FakePem),
            "zone-other");

        Assert.Equal("/client/v4/zones/zone-other/ssl/analyze", harness.LastRequest.Path);
    }

    [Fact]
    public async Task A_certificate_inside_a_pack_is_fully_typed()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """
            {
              "id": "pack-1",
              "certificates": [{
                "id": "cert-1",
                "hosts": ["example.com"],
                "status": "active",
                "bundle_method": "optimal",
                "expires_on": "2027-01-02T03:04:05Z",
                "issuer": "Let's Encrypt",
                "signature": "SHA256WithRSA",
                "priority": 1.5,
                "uploaded_on": "2026-01-02T03:04:05Z",
                "modified_on": "2026-01-03T03:04:05Z",
                "zone_id": "zone-5678",
                "geo_restrictions": { "label": "us" }
              }]
            }
            """);

        var pack = await harness.Client.Ssl.GetAsync("pack-1");

        var certificate = Assert.Single(pack!.Certificates!);
        Assert.Equal("Let's Encrypt", certificate.Issuer);
        Assert.Equal("SHA256WithRSA", certificate.Signature);
        Assert.Equal(1.5, certificate.Priority);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), certificate.UploadedOn);
        Assert.Equal(new DateTimeOffset(2026, 1, 3, 3, 4, 5, TimeSpan.Zero), certificate.ModifiedOn);
        Assert.Equal("zone-5678", certificate.ZoneId);
        Assert.Equal(SslBundleMethods.Optimal, certificate.BundleMethod);
        Assert.Equal(SslGeoRestrictionLabels.Us, certificate.GeoRestrictions!.Label);
        Assert.Equal("us", certificate.GeoRestrictions.ToString());
        Assert.Equal("(no restriction)", new SslCertificateGeoRestrictions().ToString());
        Assert.Null(certificate.AdditionalData);
    }
}

/// <summary>
/// SSL for SaaS: the custom hostnames under <c>/zones/{zone_id}/custom_hostnames</c>.
/// </summary>
public class CustomHostnamesServiceTests
{
    private const string ListPath = "/client/v4/zones/zone-5678/custom_hostnames";

    private const string OnePath = "/client/v4/zones/zone-5678/custom_hostnames/host-1";

    private const string HostnameJson = """
        {
          "id": "host-1",
          "hostname": "shop.customer.com",
          "created_at": "2026-10-01T09:00:00Z",
          "custom_metadata": { "customer_id": "42" },
          "custom_origin_server": "origin.example.com",
          "custom_origin_sni": ":request_host_header:",
          "status": "pending",
          "verification_errors": ["DNS record not found"],
          "ownership_verification": {
            "name": "_cf-custom-hostname.shop.customer.com",
            "type": "txt",
            "value": "810b7d5f"
          },
          "ownership_verification_http": {
            "http_body": "38b0f2b9",
            "http_url": "http://shop.customer.com/.well-known/cf-custom-hostname-challenge-host-1.txt"
          },
          "ssl": {
            "id": "ssl-1",
            "status": "pending_validation",
            "method": "txt",
            "type": "dv",
            "wildcard": false,
            "certificate_authority": "lets_encrypt",
            "bundle_method": "ubiquitous",
            "hosts": ["shop.customer.com"],
            "issuer": "Let's Encrypt",
            "serial_number": "0x123",
            "signature": "SHA256WithRSA",
            "custom_csr_id": "csr-1",
            "expires_on": "2027-01-01T00:00:00Z",
            "uploaded_on": "2026-10-01T09:00:00Z",
            "settings": {
              "early_hints": "on",
              "http2": "off",
              "min_tls_version": "1.2",
              "tls_1_3": "on",
              "ciphers": ["ECDHE-RSA-AES128-GCM-SHA256"]
            },
            "validation_records": [
              {
                "txt_name": "_acme-challenge.shop.customer.com",
                "txt_value": "ca3-xyz",
                "status": "pending",
                "some_new_record_field": 7
              }
            ],
            "validation_errors": [{ "message": "DNS record missing" }],
            "unknown_ssl_field": true
          },
          "some_new_field": 11
        }
        """;

    [Fact]
    public async Task ListAsync_sends_the_documented_filters_including_the_dotted_hostname_ones()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage($"[{HostnameJson}]", page: 1, totalPages: 1, count: 1, totalCount: 1);

        var page = await harness.Client.CustomHostnames.ListAsync(new CustomHostnameListParameters
        {
            PerPage = 50,
            Direction = CustomHostnameSortDirections.Descending,
            HostnameContains = "shop",
            Order = CustomHostnameSortOrders.SslStatus,
            SslStatus = CustomHostnameSslStatuses.PendingValidation,
            Wildcard = false,
            Ssl = true,
        });

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Equal(ListPath, harness.LastRequest.Path);
        Assert.Equal(
            "?per_page=50&direction=desc&hostname.contain=shop&order=ssl_status&ssl_status=pending_validation&wildcard=false&ssl=1",
            harness.LastRequest.Query);

        Assert.Single(page.Items);
        Assert.Equal("shop.customer.com", page.Items[0].Hostname);
    }

    [Fact]
    public async Task The_ssl_filter_is_one_and_zero_on_the_wire_not_true_and_false()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage("[]", page: 1, totalPages: 1, count: 0, totalCount: 0);
        harness.RespondWithPage("[]", page: 1, totalPages: 1, count: 0, totalCount: 0);
        harness.RespondWithPage("[]", page: 1, totalPages: 1, count: 0, totalCount: 0);

        await harness.Client.CustomHostnames.ListAsync(new CustomHostnameListParameters { Ssl = true });
        Assert.Equal("?ssl=1", harness.LastRequest.Query);

        await harness.Client.CustomHostnames.ListAsync(new CustomHostnameListParameters { Ssl = false });
        Assert.Equal("?ssl=0", harness.LastRequest.Query);

        // Unset means the filter is not sent at all, rather than being sent as a default.
        await harness.Client.CustomHostnames.ListAsync(new CustomHostnameListParameters());
        Assert.Equal(string.Empty, harness.LastRequest.Query);
    }

    [Fact]
    public async Task ListAllAsync_walks_the_pages_without_dropping_a_filter()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage($"[{HostnameJson}]", page: 1, totalPages: 2, count: 1, totalCount: 2);
        harness.RespondWithPage($"[{HostnameJson}]", page: 2, totalPages: 2, count: 1, totalCount: 2);

        var hostnames = await harness.Client.CustomHostnames
            .ListAllAsync(new CustomHostnameListParameters
            {
                PerPage = 1,
                HostnameStatus = CustomHostnameStatuses.Pending,
                CertificateAuthority = CustomHostnameCertificateAuthorities.LetsEncrypt,
            })
            .CollectAsync();

        Assert.Equal(2, hostnames.Count);
        Assert.Equal(2, harness.RequestCount);
        Assert.Equal(
            "?page=1&per_page=1&certificate_authority=lets_encrypt&hostname_status=pending",
            harness.Requests[0].Query);
        Assert.Equal(
            "?page=2&per_page=1&certificate_authority=lets_encrypt&hostname_status=pending",
            harness.Requests[1].Query);
    }

    [Fact]
    public async Task GetAsync_reads_the_hostname_the_challenge_and_the_certificate_state()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(HostnameJson);

        var hostname = await harness.Client.CustomHostnames.GetAsync("host-1");

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Equal(OnePath, harness.LastRequest.Path);
        Assert.NotNull(hostname);
        Assert.Equal("shop.customer.com", hostname!.Hostname);
        Assert.Equal(CustomHostnameStatuses.Pending, hostname.Status);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero), hostname.CreatedAt);
        Assert.Equal("42", hostname.CustomMetadata!["customer_id"]);
        Assert.Equal(":request_host_header:", hostname.CustomOriginSni);
        Assert.Equal("DNS record not found", Assert.Single(hostname.VerificationErrors!));
        Assert.Equal("shop.customer.com", hostname.ToString());

        // Both verification routes are described, because either can be the one a customer can manage.
        Assert.Equal("txt", hostname.OwnershipVerification!.Type);
        Assert.Equal("_cf-custom-hostname.shop.customer.com", hostname.OwnershipVerification.Name);
        Assert.Equal("810b7d5f", hostname.OwnershipVerification.Value);
        Assert.Equal("38b0f2b9", hostname.OwnershipVerificationHttp!.HttpBody);

        var ssl = hostname.Ssl!;
        Assert.Equal(CustomHostnameSslStatuses.PendingValidation, ssl.Status);
        Assert.Equal(CustomHostnameDcvMethods.Txt, ssl.Method);
        Assert.Equal(CustomHostnameValidationTypes.DomainValidation, ssl.Type);
        Assert.False(ssl.Wildcard);
        Assert.Equal("pending_validation", ssl.ToString());
        Assert.Equal("csr-1", ssl.CustomCsrId);
        Assert.Equal(2027, ssl.ExpiresOn!.Value.Year);

        var record = Assert.Single(ssl.ValidationRecords!);
        Assert.Equal("_acme-challenge.shop.customer.com", record.TxtName);
        Assert.Equal("ca3-xyz", record.TxtValue);
        Assert.Equal("_acme-challenge.shop.customer.com", record.ToString());
        Assert.Equal("DNS record missing", Assert.Single(ssl.ValidationErrors!)!.Message);

        // Unknown fields survive at every level rather than being dropped.
        Assert.Equal(11, hostname.AdditionalData!["some_new_field"].GetInt32());
        Assert.True(ssl.AdditionalData!["unknown_ssl_field"].GetBoolean());
        Assert.Equal(7, record.AdditionalData!["some_new_record_field"].GetInt32());
    }

    [Fact]
    public async Task The_per_hostname_tls_settings_keep_the_names_the_schema_uses()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(HostnameJson);

        var settings = (await harness.Client.CustomHostnames.GetAsync("host-1"))!.Ssl!.Settings!;

        Assert.Equal("on", settings.EarlyHints);
        Assert.Equal("off", settings.Http2);
        Assert.Equal("1.2", settings.MinTlsVersion);
        // tls_1_3 is the one field here whose JSON name is not what a naming policy would produce.
        Assert.Equal("on", settings.Tls13);
        Assert.Equal("ECDHE-RSA-AES128-GCM-SHA256", Assert.Single(settings.Ciphers!));
    }

    [Fact]
    public async Task GetAsync_returns_null_when_the_zone_has_no_such_hostname()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.NotFound, 10007, "Invalid Custom Hostname identifier");

        Assert.Null(await harness.Client.CustomHostnames.GetAsync("missing"));
    }

    [Fact]
    public async Task CreateAsync_posts_the_hostname_and_the_certificate_configuration()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(HostnameJson);

        await harness.Client.CustomHostnames.CreateAsync(new CreateCustomHostnameRequest
        {
            Hostname = "shop.customer.com",
            CustomOriginServer = "origin.example.com",
            CustomMetadata = new Dictionary<string, string> { ["customer_id"] = "42" },
            Ssl = new CustomHostnameSslRequest
            {
                Method = CustomHostnameDcvMethods.Txt,
                Type = CustomHostnameValidationTypes.DomainValidation,
                CertificateAuthority = CustomHostnameCertificateAuthorities.LetsEncrypt,
                Settings = new CustomHostnameSslSettings { MinTlsVersion = "1.2", Tls13 = "on" },
            },
        });

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.Equal(ListPath, harness.LastRequest.Path);

        var body = JsonDocument.Parse(harness.LastRequest.Body!);
        Assert.Equal("shop.customer.com", body.RootElement.GetProperty("hostname").GetString());
        Assert.Equal("origin.example.com", body.RootElement.GetProperty("custom_origin_server").GetString());
        Assert.Equal("42", body.RootElement.GetProperty("custom_metadata").GetProperty("customer_id").GetString());

        var ssl = body.RootElement.GetProperty("ssl");
        Assert.Equal("txt", ssl.GetProperty("method").GetString());
        Assert.Equal("dv", ssl.GetProperty("type").GetString());
        Assert.Equal("lets_encrypt", ssl.GetProperty("certificate_authority").GetString());
        Assert.Equal("1.2", ssl.GetProperty("settings").GetProperty("min_tls_version").GetString());
        Assert.Equal("on", ssl.GetProperty("settings").GetProperty("tls_1_3").GetString());
    }

    [Fact]
    public async Task The_convenience_create_asks_for_domain_validation_over_http_by_default()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(HostnameJson);

        await harness.Client.CustomHostnames.CreateAsync("shop.customer.com");

        var ssl = JsonDocument.Parse(harness.LastRequest.Body!).RootElement.GetProperty("ssl");
        Assert.Equal(CustomHostnameDcvMethods.Http, ssl.GetProperty("method").GetString());
        Assert.Equal(CustomHostnameValidationTypes.DomainValidation, ssl.GetProperty("type").GetString());
        Assert.False(ssl.GetProperty("wildcard").GetBoolean());
    }

    [Fact]
    public async Task CreateAsync_checks_the_request_before_sending_anything()
    {
        using var harness = CloudflareHarness.Create();

        var noHostname = new CreateCustomHostnameRequest();
        var emptyBundle = CreateCustomHostnameRequest.For(
            "shop.customer.com",
            new CustomHostnameSslRequest { CustomCertBundle = new List<CustomHostnameCertificatePair>() });
        var hugeBundle = CreateCustomHostnameRequest.For(
            "shop.customer.com",
            new CustomHostnameSslRequest
            {
                CustomCertBundle = Enumerable
                    .Repeat(CustomHostnameCertificatePair.Of("cert", "key"), CreateCustomHostnameRequest.MaxBundleSize + 1)
                    .ToList(),
            });
        var halfAPair = CreateCustomHostnameRequest.For(
            "shop.customer.com",
            new CustomHostnameSslRequest
            {
                CustomCertBundle = new List<CustomHostnameCertificatePair> { new() { CustomCertificate = "cert" } },
            });

        Assert.Throws<CloudflareConfigurationException>(() => noHostname.EnsureValid());
        Assert.Throws<CloudflareConfigurationException>(() => emptyBundle.EnsureValid());
        Assert.Throws<CloudflareConfigurationException>(() => hugeBundle.EnsureValid());
        Assert.Throws<CloudflareConfigurationException>(() => halfAPair.EnsureValid());

        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.CustomHostnames.CreateAsync(noHostname));

        // Nothing reached the transport: the checks are the point of them.
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task A_bundle_of_two_pairs_is_accepted_because_that_is_what_bundling_means()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(HostnameJson);

        await harness.Client.CustomHostnames.CreateAsync(CreateCustomHostnameRequest.For(
            "shop.customer.com",
            new CustomHostnameSslRequest
            {
                CustomCertBundle = new List<CustomHostnameCertificatePair>
                {
                    CustomHostnameCertificatePair.Of("rsa-cert", "rsa-key"),
                    CustomHostnameCertificatePair.Of("ecdsa-cert", "ecdsa-key"),
                },
            }));

        var bundle = JsonDocument.Parse(harness.LastRequest.Body!).RootElement
            .GetProperty("ssl").GetProperty("custom_cert_bundle");
        Assert.Equal(2, bundle.GetArrayLength());
        Assert.Equal("rsa-cert", bundle[0].GetProperty("custom_certificate").GetString());
        Assert.Equal("ecdsa-key", bundle[1].GetProperty("custom_key").GetString());
    }

    [Fact]
    public async Task UpdateAsync_patches_what_was_set_and_never_renames_the_hostname()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(HostnameJson);

        var updated = await harness.Client.CustomHostnames.UpdateAsync(
            "host-1",
            new UpdateCustomHostnameRequest { CustomOriginServer = "new-origin.example.com" });

        Assert.Equal(HttpMethod.Patch, harness.LastRequest.Method);
        Assert.Equal(OnePath, harness.LastRequest.Path);
        Assert.NotNull(updated);

        var body = JsonDocument.Parse(harness.LastRequest.Body!).RootElement;
        Assert.Equal("new-origin.example.com", body.GetProperty("custom_origin_server").GetString());
        // There is no hostname field in the PATCH schema, so nothing here can rename it.
        Assert.False(body.TryGetProperty("hostname", out _));
    }

    [Fact]
    public async Task RetryValidationAsync_is_the_patch_that_completes_domain_control_validation()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(HostnameJson);

        await harness.Client.CustomHostnames.RetryValidationAsync("host-1", CustomHostnameDcvMethods.Email);

        Assert.Equal(HttpMethod.Patch, harness.LastRequest.Method);
        Assert.Equal(OnePath, harness.LastRequest.Path);
        Assert.Equal(
            "{\"ssl\":{\"method\":\"email\",\"type\":\"dv\",\"wildcard\":false}}",
            harness.LastRequest.Body);
        Assert.Equal("retry validation with email", UpdateCustomHostnameRequest.ToRetryValidation("email").ToString());
    }

    [Fact]
    public async Task DeleteAsync_sends_a_delete_with_no_body()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("null");

        await harness.Client.CustomHostnames.DeleteAsync("host-1");

        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.Equal(OnePath, harness.LastRequest.Path);
        Assert.True(string.IsNullOrEmpty(harness.LastRequest.Body));
    }

    [Fact]
    public async Task The_zone_can_be_overridden_per_call()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(HostnameJson);

        await harness.Client.CustomHostnames.GetAsync("host-1", zoneId: "zone-other");

        Assert.Equal("/client/v4/zones/zone-other/custom_hostnames/host-1", harness.LastRequest.Path);
    }

    [Fact]
    public void The_enumerations_carry_every_value_the_schema_defines()
    {
        Assert.Equal(16, CustomHostnameStatuses.All.Count);
        Assert.Equal(21, CustomHostnameSslStatuses.All.Count);
        Assert.Equal(3, CustomHostnameDcvMethods.All.Count);
        Assert.Equal(CustomHostnameValidationTypes.DomainValidation, Assert.Single(CustomHostnameValidationTypes.All));
        Assert.Equal(2, CustomHostnameSortOrders.All.Count);
        Assert.Equal(2, CustomHostnameSortDirections.All.Count);

        // DigiCert can issue a custom hostname's certificate but is not accepted as a list filter, and the
        // advanced certificate pack authorities are a different set again: no DigiCert there either.
        Assert.Equal(4, CustomHostnameCertificateAuthorities.All.Count);
        Assert.Contains(CustomHostnameCertificateAuthorities.DigiCert, CustomHostnameCertificateAuthorities.All);
        Assert.Equal(3, CustomHostnameCertificateAuthorities.Filterable.Count);
        Assert.DoesNotContain(CustomHostnameCertificateAuthorities.DigiCert, CustomHostnameCertificateAuthorities.Filterable);
        Assert.DoesNotContain(CustomHostnameCertificateAuthorities.DigiCert, SslCertificateAuthorities.All);
    }

    [Fact]
    public void The_requests_describe_themselves_without_repeating_a_certificate()
    {
        Assert.Equal("shop.customer.com", CreateCustomHostnameRequest.For("shop.customer.com").ToString());
        Assert.Equal("txt", CustomHostnameSslRequest.ForValidation("txt").ToString());
        Assert.Equal(
            "uploaded certificate",
            CustomHostnameSslRequest.WithUploadedCertificate("-----BEGIN CERTIFICATE-----", "key").ToString());
        Assert.Equal("ssl settings", new CustomHostnameSslRequest().ToString());
        Assert.Equal("custom hostname settings", new UpdateCustomHostnameRequest().ToString());
        Assert.Equal("certificate and key", CustomHostnameCertificatePair.Of("cert", "key").ToString());
        Assert.Equal(string.Empty, new CustomHostname().ToString());
        Assert.Equal("_acme-challenge", new CustomHostnameSslValidationRecord { Cname = "_acme-challenge" }.ToString());
    }
}

public class OriginTlsComplianceModesTests
{
    private const string SettingPath = "/client/v4/zones/zone-5678/settings/origin_tls_compliance_modes";

    private const string ModesJson = """
        {
          "id": "origin_tls_compliance_modes",
          "editable": true,
          "value": ["fips", "pqh"],
          "modified_on": "2026-10-01T09:00:00Z",
          "some_new_setting_field": "still-here"
        }
        """;

    [Fact]
    public async Task GetOriginTlsComplianceModesAsync_reads_the_constraint_auto_origin_tls_kex_works_inside()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(ModesJson);

        var modes = await harness.Client.Ssl.GetOriginTlsComplianceModesAsync();

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Equal(SettingPath, harness.LastRequest.Path);
        Assert.Equal(string.Empty, harness.LastRequest.Query);

        Assert.Equal("origin_tls_compliance_modes", modes!.Id);
        Assert.True(modes.Editable);
        Assert.Equal(new[] { "fips", "pqh" }, modes.Value);
        Assert.True(modes.IsConstrained);
        Assert.Equal(2026, modes.ModifiedOn?.Year);
        Assert.Equal("fips, pqh", modes.ToString());
        Assert.True(modes.AdditionalData!.ContainsKey("some_new_setting_field"));
    }

    [Fact]
    public async Task GetOriginTlsComplianceModesAsync_returns_null_when_the_zone_reports_no_result()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.NotFound, 1001, "Unknown setting");

        Assert.Null(await harness.Client.Ssl.GetOriginTlsComplianceModesAsync());
    }

    [Fact]
    public async Task ReplaceOriginTlsComplianceModesAsync_puts_the_complete_new_list()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(ModesJson);

        var modes = await harness.Client.Ssl.ReplaceOriginTlsComplianceModesAsync(
            UpdateOriginTlsComplianceModesRequest.With(
                OriginTlsComplianceModeValues.Fips,
                OriginTlsComplianceModeValues.PostQuantumHybrid));

        Assert.Equal(HttpMethod.Put, harness.LastRequest.Method);
        Assert.Equal(SettingPath, harness.LastRequest.Path);
        Assert.Equal("""{"value":["fips","pqh"]}""", harness.LastRequest.Body);
        Assert.Equal("fips, pqh", modes.ToString());
    }

    [Fact]
    public async Task ChangeOriginTlsComplianceModesAsync_patches_the_same_bytes_because_patch_is_also_a_replace()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(ModesJson);
        harness.RespondWithResult(ModesJson);

        await harness.Client.Ssl.ChangeOriginTlsComplianceModesAsync(
            UpdateOriginTlsComplianceModesRequest.With(OriginTlsComplianceModeValues.Fips));

        Assert.Equal(HttpMethod.Patch, harness.LastRequest.Method);
        Assert.Equal(SettingPath, harness.LastRequest.Path);
        Assert.Equal("""{"value":["fips"]}""", harness.LastRequest.Body);

        // Both verbs are documented as a full replace, not a merge, so they must send the same body.
        await harness.Client.Ssl.ReplaceOriginTlsComplianceModesAsync(
            UpdateOriginTlsComplianceModesRequest.With(OriginTlsComplianceModeValues.Fips));

        Assert.Equal(HttpMethod.Put, harness.LastRequest.Method);
        Assert.Equal("""{"value":["fips"]}""", harness.LastRequest.Body);
        Assert.Equal(2, harness.RequestCount);
    }

    [Fact]
    public async Task Clearing_the_constraint_is_an_empty_list_not_a_missing_one()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(ModesJson);

        await harness.Client.Ssl.ReplaceOriginTlsComplianceModesAsync(UpdateOriginTlsComplianceModesRequest.Clear());
        Assert.Equal("""{"value":[]}""", harness.LastRequest.Body);

        // A missing list is refused locally: Cloudflare could not read it as the complete replacement.
        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Ssl.ChangeOriginTlsComplianceModesAsync(new UpdateOriginTlsComplianceModesRequest()));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => harness.Client.Ssl.ReplaceOriginTlsComplianceModesAsync(null!));

        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task ClearOriginTlsComplianceModesAsync_deletes_the_setting_which_comes_back_without_a_value()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""
            {
              "id": "origin_tls_compliance_modes",
              "editable": true,
              "modified_on": "2026-10-02T09:00:00Z"
            }
            """);

        var cleared = await harness.Client.Ssl.ClearOriginTlsComplianceModesAsync();

        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.Equal(SettingPath, harness.LastRequest.Path);
        Assert.True(string.IsNullOrEmpty(harness.LastRequest.Body));

        Assert.Equal("origin_tls_compliance_modes", cleared!.Id);
        Assert.Null(cleared.Value);
        Assert.False(cleared.IsConstrained);
        Assert.Equal("(no constraint)", cleared.ToString());
    }

    [Fact]
    public async Task A_mode_this_library_has_never_heard_of_survives_in_both_directions()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""
            { "id": "origin_tls_compliance_modes", "editable": true, "value": ["cnsa2"] }
            """);
        harness.RespondWithResult(ModesJson);

        var modes = await harness.Client.Ssl.GetOriginTlsComplianceModesAsync();
        Assert.Equal(new[] { "cnsa2" }, modes!.Value);
        Assert.Equal("cnsa2", modes.ToString());

        // Cloudflare says to treat unknown values as opaque strings, so sending one back is allowed too.
        await harness.Client.Ssl.ChangeOriginTlsComplianceModesAsync(
            UpdateOriginTlsComplianceModesRequest.With("cnsa2"));
        Assert.Equal("""{"value":["cnsa2"]}""", harness.LastRequest.Body);
    }

    [Fact]
    public async Task The_compliance_modes_zone_can_be_overridden_per_call()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(ModesJson);
        harness.RespondWithResult(ModesJson);

        await harness.Client.Ssl.GetOriginTlsComplianceModesAsync(zoneId: "zone-other");
        Assert.Equal(
            "/client/v4/zones/zone-other/settings/origin_tls_compliance_modes",
            harness.LastRequest.Path);

        await harness.Client.Ssl.ClearOriginTlsComplianceModesAsync(zoneId: "zone-other");
        Assert.Equal(
            "/client/v4/zones/zone-other/settings/origin_tls_compliance_modes",
            harness.LastRequest.Path);
    }

    [Fact]
    public void The_documented_modes_and_the_requests_describe_themselves()
    {
        Assert.Equal(new[] { "fips", "pqh" }, OriginTlsComplianceModeValues.All);
        Assert.Equal("fips, pqh", UpdateOriginTlsComplianceModesRequest.With("fips", "pqh").ToString());
        Assert.Equal("clear the constraint", UpdateOriginTlsComplianceModesRequest.Clear().ToString());
        Assert.Equal("(no list)", new UpdateOriginTlsComplianceModesRequest().ToString());
        Assert.Equal("(no constraint)", new OriginTlsComplianceModes().ToString());
    }
}

public class CustomHostnameFallbackOriginTests
{
    private const string FallbackPath = "/client/v4/zones/zone-5678/custom_hostnames/fallback_origin";

    private const string OriginJson = """
        {
          "origin": "fallback.example.com",
          "status": "active",
          "created_at": "2026-09-20T10:00:00Z",
          "updated_at": "2026-09-21T11:00:00Z",
          "some_new_fallback_field": 3
        }
        """;

    [Fact]
    public async Task GetFallbackOriginAsync_reads_the_origin_hostnames_without_their_own_fall_back_to()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(OriginJson);

        var origin = await harness.Client.CustomHostnames.GetFallbackOriginAsync();

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Equal(FallbackPath, harness.LastRequest.Path);
        Assert.Equal(string.Empty, harness.LastRequest.Query);

        Assert.Equal("fallback.example.com", origin!.Origin);
        Assert.Equal(CustomHostnameFallbackOriginStatuses.Active, origin.Status);
        Assert.Equal(2026, origin.CreatedAt?.Year);
        Assert.Equal(2026, origin.UpdatedAt?.Year);
        Assert.False(origin.HasErrors);
        Assert.Equal("fallback.example.com (active)", origin.ToString());
        Assert.True(origin.AdditionalData!.ContainsKey("some_new_fallback_field"));
    }

    [Fact]
    public async Task Activation_errors_are_plain_strings_inside_an_otherwise_successful_response()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""
            {
              "origin": "fallback.example.com",
              "status": "deployment_timed_out",
              "errors": [
                "Origin did not complete the TLS handshake",
                "Certificate does not cover the origin hostname"
              ]
            }
            """);

        var origin = await harness.Client.CustomHostnames.GetFallbackOriginAsync();

        Assert.True(origin!.HasErrors);
        Assert.Equal(2, origin.Errors!.Count);
        Assert.Equal("Origin did not complete the TLS handshake", origin.Errors[0]);
        Assert.Equal("fallback.example.com (deployment_timed_out, 2 activation errors)", origin.ToString());
    }

    [Fact]
    public async Task GetFallbackOriginAsync_returns_null_when_the_zone_has_none()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.NotFound, 10007, "Fallback origin not found");

        Assert.Null(await harness.Client.CustomHostnames.GetFallbackOriginAsync());
    }

    [Fact]
    public async Task SetFallbackOriginAsync_puts_the_one_field_the_body_is_allowed_to_have()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""
            { "origin": "fallback.example.com", "status": "pending_deployment" }
            """);

        var saved = await harness.Client.CustomHostnames.SetFallbackOriginAsync(
            UpdateCustomHostnameFallbackOriginRequest.For("fallback.example.com"));

        Assert.Equal(HttpMethod.Put, harness.LastRequest.Method);
        Assert.Equal(FallbackPath, harness.LastRequest.Path);
        Assert.Equal("""{"origin":"fallback.example.com"}""", harness.LastRequest.Body);
        Assert.Equal(CustomHostnameFallbackOriginStatuses.PendingDeployment, saved.Status);
        Assert.Equal("fallback.example.com (pending_deployment)", saved.ToString());
    }

    [Fact]
    public async Task The_string_overload_sends_the_same_request_as_the_request_object()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(OriginJson);

        await harness.Client.CustomHostnames.SetFallbackOriginAsync("fallback.example.com");

        Assert.Equal(HttpMethod.Put, harness.LastRequest.Method);
        Assert.Equal(FallbackPath, harness.LastRequest.Path);
        Assert.Equal("""{"origin":"fallback.example.com"}""", harness.LastRequest.Body);
    }

    [Fact]
    public async Task A_fallback_origin_without_an_origin_never_reaches_the_transport()
    {
        using var harness = CloudflareHarness.Create();

        Assert.Throws<CloudflareConfigurationException>(
            () => new UpdateCustomHostnameFallbackOriginRequest { Origin = "   " }.EnsureValid());

        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.CustomHostnames.SetFallbackOriginAsync(new UpdateCustomHostnameFallbackOriginRequest()));
        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.CustomHostnames.SetFallbackOriginAsync(" "));

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task DeleteFallbackOriginAsync_sends_a_delete_with_no_body()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("null");

        await harness.Client.CustomHostnames.DeleteFallbackOriginAsync();

        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.Equal(FallbackPath, harness.LastRequest.Path);
        Assert.True(string.IsNullOrEmpty(harness.LastRequest.Body));
    }

    [Fact]
    public async Task The_fallback_origin_zone_can_be_overridden_per_call()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(OriginJson);
        harness.RespondWithResult("null");

        await harness.Client.CustomHostnames.GetFallbackOriginAsync(zoneId: "zone-other");
        Assert.Equal("/client/v4/zones/zone-other/custom_hostnames/fallback_origin", harness.LastRequest.Path);

        await harness.Client.CustomHostnames.DeleteFallbackOriginAsync(zoneId: "zone-other");
        Assert.Equal("/client/v4/zones/zone-other/custom_hostnames/fallback_origin", harness.LastRequest.Path);
    }

    [Fact]
    public void The_documented_statuses_and_the_requests_describe_themselves()
    {
        Assert.Equal(
            new[]
            {
                "initializing",
                "pending_deployment",
                "pending_deletion",
                "active",
                "deployment_timed_out",
                "deletion_timed_out",
            },
            CustomHostnameFallbackOriginStatuses.All);

        Assert.Equal(
            "origin.example.com",
            UpdateCustomHostnameFallbackOriginRequest.For("origin.example.com").ToString());
        Assert.Equal("(no origin)", new UpdateCustomHostnameFallbackOriginRequest().ToString());
        Assert.Equal("(no fallback origin)", new CustomHostnameFallbackOrigin().ToString());
        // An origin Cloudflare reported no status for reads as unknown rather than being left out, the way
        // UniversalSslSettings treats a missing enabled flag.
        Assert.Equal(
            "fallback.example.com (unknown)",
            new CustomHostnameFallbackOrigin { Origin = "fallback.example.com" }.ToString());
        Assert.Equal(
            "pending_deletion",
            new CustomHostnameFallbackOrigin { Status = "pending_deletion" }.ToString());
    }
}

public class OriginTlsClientAuthServiceTests
{
    private const string RootPath = "/client/v4/zones/zone-5678/origin_tls_client_auth";

    private const string SettingsPath = RootPath + "/settings";

    private const string HostnamesPath = RootPath + "/hostnames";

    private const string CertificatesPath = RootPath + "/hostnames/certificates";

    private const string Pem = "-----BEGIN CERTIFICATE----- MIID -----END CERTIFICATE-----";

    private const string KeyPem = "-----BEGIN RSA PRIVATE KEY----- MIIE -----END RSA PRIVATE KEY-----";

    private const string AssociationJson = """
        {
          "cert_id": "cert-1",
          "cert_status": "active",
          "cert_updated_at": "2026-10-01T09:00:00Z",
          "cert_uploaded_on": "2026-09-01T09:00:00Z",
          "certificate": "-----BEGIN CERTIFICATE----- MIID -----END CERTIFICATE-----",
          "created_at": "2026-09-02T09:00:00Z",
          "enabled": true,
          "expires_on": "2027-09-01T09:00:00Z",
          "hostname": "app.example.com",
          "issuer": "GlobalSign",
          "serial_number": "6743787633689793699141714808227354901",
          "signature": "SHA256WithRSA",
          "status": "active",
          "updated_at": "2026-10-01T09:00:00Z",
          "some_new_field": true
        }
        """;

    private const string CertificateJson = """
        {
          "id": "cert-1",
          "certificate": "-----BEGIN CERTIFICATE----- MIID -----END CERTIFICATE-----",
          "expires_on": "2027-09-01T09:00:00Z",
          "issuer": "GlobalSign",
          "serial_number": "6743787633689793699141714808227354901",
          "signature": "SHA256WithRSA",
          "status": "active",
          "uploaded_on": "2026-09-01T09:00:00Z",
          "some_new_certificate_field": 4
        }
        """;

    private const string ZoneCertificateJson = """
        {
          "id": "zone-cert-1",
          "certificate": "-----BEGIN CERTIFICATE----- MIID -----END CERTIFICATE-----",
          "enabled": true,
          "expires_on": "2027-09-01T09:00:00Z",
          "issuer": "GlobalSign",
          "private_key": "-----BEGIN RSA PRIVATE KEY----- MIIE -----END RSA PRIVATE KEY-----",
          "signature": "SHA256WithRSA",
          "status": "active",
          "uploaded_on": "2026-09-01T09:00:00Z",
          "some_new_zone_field": "kept"
        }
        """;

    [Fact]
    public async Task GetSettingsAsync_reads_the_one_boolean_this_resource_has()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{ "enabled": true }""");

        var setting = await harness.Client.OriginTlsClientAuth.GetSettingsAsync();

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Equal(SettingsPath, harness.LastRequest.Path);
        Assert.Equal(string.Empty, harness.LastRequest.Query);

        Assert.True(setting.Enabled);
        Assert.Equal("zone-level authenticated origin pulls enabled", setting.ToString());
    }

    [Fact]
    public async Task SetSettingsAsync_sends_the_flag_explicitly_in_both_directions()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{ "enabled": true }""");
        harness.RespondWithResult("""{ "enabled": false }""");

        await harness.Client.OriginTlsClientAuth.SetSettingsAsync(true);
        Assert.Equal(HttpMethod.Put, harness.LastRequest.Method);
        Assert.Equal(SettingsPath, harness.LastRequest.Path);
        Assert.Equal("""{"enabled":true}""", harness.LastRequest.Body);

        // Off has to be said, not implied: the schema marks the flag required.
        var disabled = await harness.Client.OriginTlsClientAuth.SetSettingsAsync(false);
        Assert.Equal("""{"enabled":false}""", harness.LastRequest.Body);
        Assert.False(disabled.Enabled);
        Assert.Equal("zone-level authenticated origin pulls disabled", disabled.ToString());
    }

    [Fact]
    public async Task GetHostnameAsync_reads_the_association_and_both_of_its_statuses()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(AssociationJson);

        var association = await harness.Client.OriginTlsClientAuth.GetHostnameAsync("app.example.com");

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Equal(HostnamesPath + "/app.example.com", harness.LastRequest.Path);

        Assert.Equal("app.example.com", association!.Hostname);
        Assert.Equal("cert-1", association.CertId);
        Assert.True(association.IsEnabled);
        Assert.Equal(OriginTlsClientAuthStatuses.Active, association.Status);
        Assert.Equal(OriginTlsClientAuthStatuses.Active, association.CertStatus);
        Assert.Equal("GlobalSign", association.Issuer);
        Assert.Equal("SHA256WithRSA", association.Signature);
        Assert.Equal(2027, association.ExpiresOn?.Year);
        Assert.Equal(2026, association.CertUploadedOn?.Year);
        Assert.True(association.AdditionalData!.ContainsKey("some_new_field"));

        // The PEM comes back in the response but must not leak into a status line.
        Assert.DoesNotContain("MIID", association.ToString());
        Assert.Contains("app.example.com", association.ToString());
        Assert.Contains("enabled", association.ToString());
    }

    [Fact]
    public async Task GetHostnameAsync_returns_null_when_the_hostname_has_no_association()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.NotFound, 10007, "Hostname not found");

        Assert.Null(await harness.Client.OriginTlsClientAuth.GetHostnameAsync("missing.example.com"));
    }

    [Fact]
    public async Task SetHostnamesAsync_puts_a_config_array_and_reads_back_an_array()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult($"[{AssociationJson}]");

        var associations = await harness.Client.OriginTlsClientAuth.SetHostnamesAsync(
            UpdateOriginTlsHostnameAssociationsRequest.For(
                OriginTlsHostnameAssociationChange.Enable("app.example.com", "cert-1")));

        Assert.Equal(HttpMethod.Put, harness.LastRequest.Method);

        // The hostname travels in the body: this path has no hostname segment in it.
        Assert.Equal(HostnamesPath, harness.LastRequest.Path);
        Assert.Equal(
            """{"config":[{"cert_id":"cert-1","enabled":true,"hostname":"app.example.com"}]}""",
            harness.LastRequest.Body);

        Assert.Single(associations);
        Assert.Equal("app.example.com", associations[0].Hostname);
    }

    [Fact]
    public async Task Voiding_an_association_sends_enabled_as_null_because_null_is_the_third_state()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");
        harness.RespondWithResult("[]");

        await harness.Client.OriginTlsClientAuth.VoidHostnameAsync("app.example.com", "cert-1");
        Assert.Equal(
            """{"config":[{"cert_id":"cert-1","enabled":null,"hostname":"app.example.com"}]}""",
            harness.LastRequest.Body);

        // Disable is not the same call: the association survives it, and still counts towards the 100.
        await harness.Client.OriginTlsClientAuth.DisableHostnameAsync("app.example.com", "cert-1");
        Assert.Equal(
            """{"config":[{"cert_id":"cert-1","enabled":false,"hostname":"app.example.com"}]}""",
            harness.LastRequest.Body);
    }

    [Fact]
    public async Task Several_hostnames_can_be_changed_in_one_call()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult($"[{AssociationJson}]");

        await harness.Client.OriginTlsClientAuth.SetHostnamesAsync(
            UpdateOriginTlsHostnameAssociationsRequest.For(
                OriginTlsHostnameAssociationChange.Enable("app.example.com", "cert-1"),
                OriginTlsHostnameAssociationChange.Disable("api.example.com", "cert-1"),
                OriginTlsHostnameAssociationChange.Void("old.example.com", "cert-2")));

        var body = JsonDocument.Parse(harness.LastRequest.Body!).RootElement.GetProperty("config");
        Assert.Equal(3, body.GetArrayLength());
        Assert.True(body[0].GetProperty("enabled").GetBoolean());
        Assert.False(body[1].GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body[2].GetProperty("enabled").ValueKind);
        Assert.Equal("old.example.com", body[2].GetProperty("hostname").GetString());
    }

    [Fact]
    public async Task An_empty_or_hostnameless_batch_never_reaches_the_transport()
    {
        using var harness = CloudflareHarness.Create();

        var empty = new UpdateOriginTlsHostnameAssociationsRequest();
        var noHostname = UpdateOriginTlsHostnameAssociationsRequest.For(
            new OriginTlsHostnameAssociationChange { CertId = "cert-1", Enabled = true });

        Assert.Throws<CloudflareConfigurationException>(() => empty.EnsureValid());
        Assert.Throws<CloudflareConfigurationException>(() => noHostname.EnsureValid());

        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.OriginTlsClientAuth.SetHostnamesAsync(empty));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => harness.Client.OriginTlsClientAuth.SetHostnamesAsync(null!));

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task ListCertificatesAsync_reads_the_whole_collection_and_the_endpoint_does_not_page()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult($"[{CertificateJson}]");

        var certificates = await harness.Client.OriginTlsClientAuth.ListCertificatesAsync();

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Equal(CertificatesPath, harness.LastRequest.Path);
        Assert.Equal(string.Empty, harness.LastRequest.Query);

        Assert.Single(certificates);
        Assert.Equal("cert-1", certificates[0].Id);
    }

    [Fact]
    public async Task GetCertificateAsync_reads_one_certificate_without_echoing_the_pem()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(CertificateJson);

        var certificate = await harness.Client.OriginTlsClientAuth.GetCertificateAsync("cert-1");

        Assert.Equal(CertificatesPath + "/cert-1", harness.LastRequest.Path);
        Assert.Equal(Pem, certificate!.Certificate);
        Assert.Equal(OriginTlsClientAuthStatuses.Active, certificate.Status);
        Assert.True(certificate.AdditionalData!.ContainsKey("some_new_certificate_field"));

        Assert.DoesNotContain("MIID", certificate.ToString());
        Assert.Contains("cert-1", certificate.ToString());
        Assert.Contains("expires", certificate.ToString());
    }

    [Fact]
    public async Task GetCertificateAsync_returns_null_when_the_zone_has_no_such_certificate()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.NotFound, 10007, "Certificate not found");

        Assert.Null(await harness.Client.OriginTlsClientAuth.GetCertificateAsync("cert-missing"));
    }

    [Fact]
    public async Task UploadCertificateAsync_posts_both_halves_of_the_pair()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(CertificateJson);

        var uploaded = await harness.Client.OriginTlsClientAuth.UploadCertificateAsync(
            UploadOriginTlsCertificateRequest.Of(Pem, KeyPem));

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.Equal(CertificatesPath, harness.LastRequest.Path);
        Assert.Equal(
            """{"certificate":"-----BEGIN CERTIFICATE----- MIID -----END CERTIFICATE-----","private_key":"-----BEGIN RSA PRIVATE KEY----- MIIE -----END RSA PRIVATE KEY-----"}""",
            harness.LastRequest.Body);
        Assert.Equal("cert-1", uploaded.Id);
    }

    [Fact]
    public async Task The_string_overload_uploads_the_same_body()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(CertificateJson);

        await harness.Client.OriginTlsClientAuth.UploadCertificateAsync(Pem, KeyPem);

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.Equal(CertificatesPath, harness.LastRequest.Path);

        var body = JsonDocument.Parse(harness.LastRequest.Body!).RootElement;
        Assert.Equal(Pem, body.GetProperty("certificate").GetString());
        Assert.Equal(KeyPem, body.GetProperty("private_key").GetString());
    }

    [Fact]
    public async Task Half_a_pair_never_reaches_the_transport()
    {
        using var harness = CloudflareHarness.Create();

        Assert.Throws<CloudflareConfigurationException>(
            () => new UploadOriginTlsCertificateRequest { Certificate = Pem }.EnsureValid());
        Assert.Throws<CloudflareConfigurationException>(
            () => new UploadOriginTlsCertificateRequest { PrivateKey = KeyPem }.EnsureValid());

        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.OriginTlsClientAuth.UploadCertificateAsync(new UploadOriginTlsCertificateRequest()));
        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.OriginTlsClientAuth.UploadCertificateAsync("  ", KeyPem));

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public void An_upload_request_reports_lengths_and_never_key_material()
    {
        var request = UploadOriginTlsCertificateRequest.Of(Pem, KeyPem);

        Assert.Equal(
            $"certificate ({Pem.Length} characters) and private key ({KeyPem.Length} characters)",
            request.ToString());
        Assert.DoesNotContain("MIID", request.ToString());
        Assert.DoesNotContain("MIIE", request.ToString());
        Assert.Equal("(no changes)", new UpdateOriginTlsHostnameAssociationsRequest().ToString());
    }

    [Fact]
    public async Task DeleteCertificateAsync_sends_a_delete_and_returns_what_was_removed()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(CertificateJson);

        var removed = await harness.Client.OriginTlsClientAuth.DeleteCertificateAsync("cert-1");

        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.Equal(CertificatesPath + "/cert-1", harness.LastRequest.Path);
        Assert.True(string.IsNullOrEmpty(harness.LastRequest.Body));
        Assert.Equal("cert-1", removed!.Id);
    }

    [Fact]
    public async Task The_deprecated_zone_endpoints_still_answer_and_hand_back_the_private_key()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult($"[{ZoneCertificateJson}]");
        harness.RespondWithResult(ZoneCertificateJson);

        var certificates = await harness.Client.OriginTlsClientAuth.ListZoneCertificatesAsync();
        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Equal(RootPath, harness.LastRequest.Path);
        Assert.Single(certificates);

        var one = await harness.Client.OriginTlsClientAuth.GetZoneCertificateAsync("zone-cert-1");
        Assert.Equal(RootPath + "/zone-cert-1", harness.LastRequest.Path);
        Assert.True(one!.HasPrivateKey);
        Assert.Equal(KeyPem, one.PrivateKey);
        Assert.True(one.AdditionalData!.ContainsKey("some_new_zone_field"));

        // The key is in the model because Cloudflare returns it, and out of every summary we print.
        Assert.DoesNotContain("MIIE", one.ToString());
        Assert.Contains("private key", one.ToString());
        Assert.Contains(KeyPem.Length.ToString(), one.ToString());
    }

    [Fact]
    public async Task Uploading_a_zone_certificate_uses_the_same_body_as_a_hostname_one()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(ZoneCertificateJson);
        harness.RespondWithResult(ZoneCertificateJson);

        var uploaded = await harness.Client.OriginTlsClientAuth.UploadZoneCertificateAsync(
            UploadOriginTlsCertificateRequest.Of(Pem, KeyPem));

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.Equal(RootPath, harness.LastRequest.Path);
        Assert.Equal("zone-cert-1", uploaded.Id);

        var removed = await harness.Client.OriginTlsClientAuth.DeleteZoneCertificateAsync("zone-cert-1");
        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.Equal(RootPath + "/zone-cert-1", harness.LastRequest.Path);
        Assert.Equal("zone-cert-1", removed!.Id);
    }

    [Fact]
    public async Task The_zone_can_be_overridden_per_call()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{ "enabled": false }""");
        harness.RespondWithResult("[]");

        await harness.Client.OriginTlsClientAuth.GetSettingsAsync(zoneId: "zone-other");
        Assert.Equal("/client/v4/zones/zone-other/origin_tls_client_auth/settings", harness.LastRequest.Path);

        await harness.Client.OriginTlsClientAuth.ListCertificatesAsync(zoneId: "zone-other");
        Assert.Equal(
            "/client/v4/zones/zone-other/origin_tls_client_auth/hostnames/certificates",
            harness.LastRequest.Path);
    }

    [Fact]
    public void The_status_set_has_deleted_in_it_which_the_fallback_origin_one_does_not()
    {
        Assert.Equal(
            new[]
            {
                "initializing",
                "pending_deployment",
                "pending_deletion",
                "active",
                "deleted",
                "deployment_timed_out",
                "deletion_timed_out",
            },
            OriginTlsClientAuthStatuses.All);

        // Two neighbouring resources, two different enums: sharing one list would be wrong for one of them.
        Assert.Contains("deleted", OriginTlsClientAuthStatuses.All);
        Assert.DoesNotContain("deleted", CustomHostnameFallbackOriginStatuses.All);
        Assert.Equal(
            CustomHostnameFallbackOriginStatuses.All.Count + 1,
            OriginTlsClientAuthStatuses.All.Count);
    }

    [Fact]
    public void The_three_states_and_the_requests_describe_themselves()
    {
        Assert.Equal(
            "enable app.example.com",
            OriginTlsHostnameAssociationChange.Enable("app.example.com", "cert-1").ToString());
        Assert.Equal(
            "disable app.example.com",
            OriginTlsHostnameAssociationChange.Disable("app.example.com", "cert-1").ToString());
        Assert.Equal(
            "void app.example.com",
            OriginTlsHostnameAssociationChange.Void("app.example.com", "cert-1").ToString());
        Assert.Equal(
            "2 change(s): enable app.example.com, void old.example.com",
            UpdateOriginTlsHostnameAssociationsRequest.For(
                OriginTlsHostnameAssociationChange.Enable("app.example.com", "cert-1"),
                OriginTlsHostnameAssociationChange.Void("old.example.com", "cert-1")).ToString());
        Assert.Equal("enable", UpdateOriginTlsClientAuthSettingRequest.Enable().ToString());
        Assert.Equal("disable", UpdateOriginTlsClientAuthSettingRequest.Disable().ToString());
        Assert.Equal("(no certificate)", new OriginTlsClientCertificate().ToString());
        Assert.Equal("(no certificate)", new ZoneAuthenticatedOriginPull().ToString());
        Assert.Equal(
            "zone-level authenticated origin pulls not reported",
            new OriginTlsClientAuthSetting().ToString());
        Assert.Equal("void", new OriginTlsHostnameAssociation { Hostname = null }.ToString());
    }
}

public class DnssecServiceTests
{
    private const string RootPath = "/client/v4/zones/zone-5678/dnssec";

    private const string ZskPath = RootPath + "/zsk";

    private const string DsRecord = "example.com. 3600 IN DS 12345 13 2 ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789";

    private const string DnssecJson = """
        {
          "algorithm": "13",
          "digest": "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789",
          "digest_algorithm": "SHA256",
          "digest_type": "2",
          "dnssec_multi_signer": false,
          "dnssec_presigned": false,
          "dnssec_use_nsec3": false,
          "ds": "example.com. 3600 IN DS 12345 13 2 ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789",
          "flags": 257,
          "key_tag": 12345,
          "key_type": "ECDSAP256SHA256",
          "modified_on": "2026-10-01T09:00:00Z",
          "public_key": "mGt8lqBbYcVW4pQ0vN2xR7sT9uY1aB3cD5eF7gH9iJ0kL2mN4oP6qR8sT0uV2wX4",
          "status": "active",
          "some_new_dnssec_field": "kept"
        }
        """;

    private const string ZskJson = """
        [
          {
            "DNSKEY": {
              "Algorithm": 13,
              "Flags": 257,
              "Hdr": {
                "Class": 1,
                "Name": "example.com.",
                "Rdlength": 67,
                "Rrtype": 48,
                "Ttl": 3600
              },
              "Protocol": 3,
              "PublicKey": "mGt8lqBbYcVW4pQ0vN2xR7sT9uY1aB3cD5eF7gH9iJ0kL2mN4oP6qR8sT0uV2wX4"
            },
            "Location": "us-east",
            "Name": "example.com.",
            "KeyTag": 12345,
            "some_new_zsk_field": true
          }
        ]
        """;

    [Fact]
    public async Task GetAsync_reads_every_published_field_including_the_DS_record()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(DnssecJson);

        var dnssec = await harness.Client.Dnssec.GetAsync();

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Equal(RootPath, harness.LastRequest.Path);
        Assert.Equal(string.Empty, harness.LastRequest.Query);

        Assert.Equal(DnssecStatuses.Active, dnssec.Status);
        Assert.Equal(DsRecord, dnssec.Ds);
        Assert.Equal(12345, dnssec.KeyTag);
        Assert.Equal(257, dnssec.Flags);
        Assert.Equal("13", dnssec.Algorithm);
        Assert.Equal("2", dnssec.DigestType);
        Assert.Equal("SHA256", dnssec.DigestAlgorithm);
        Assert.Equal("ECDSAP256SHA256", dnssec.KeyType);
        Assert.StartsWith("ABCDEF0123", dnssec.Digest, StringComparison.Ordinal);
        Assert.StartsWith("mGt8lqBb", dnssec.PublicKey, StringComparison.Ordinal);
        Assert.Equal(2026, dnssec.ModifiedOn?.Year);

        // The three signing switches are separate features, not one "advanced" blob.
        Assert.False(dnssec.DnssecMultiSigner);
        Assert.False(dnssec.DnssecPresigned);
        Assert.False(dnssec.DnssecUseNsec3);

        Assert.True(dnssec.IsActive);
        Assert.False(dnssec.IsPending);
        Assert.True(dnssec.AdditionalData!.ContainsKey("some_new_dnssec_field"));
        Assert.Equal("active (key tag 12345, algorithm 13)", dnssec.ToString());
    }

    [Fact]
    public async Task GetAsync_tells_a_signed_zone_from_the_two_waiting_states()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{ "status": "pending", "key_tag": 999 }""");
        harness.RespondWithResult("""{ "status": "pending-disabled" }""");
        harness.RespondWithResult("""{ "status": "disabled" }""");
        harness.RespondWithResult("""{ "status": "error", "algorithm": "13" }""");

        // Enabling is not finished when the call returns: the DS record has to reach the registrar first.
        var pending = await harness.Client.Dnssec.GetAsync();
        Assert.True(pending.IsPending);
        Assert.False(pending.IsActive);
        Assert.Equal("pending (key tag 999, algorithm unknown)", pending.ToString());

        // Disabling has its own waiting state, and it is still "pending" as far as a caller cares.
        var pendingDisabled = await harness.Client.Dnssec.GetAsync();
        Assert.True(pendingDisabled.IsPending);
        Assert.False(pendingDisabled.IsActive);
        Assert.Equal("pending-disabled", pendingDisabled.ToString());

        var disabled = await harness.Client.Dnssec.GetAsync();
        Assert.False(disabled.IsPending);
        Assert.False(disabled.IsActive);
        Assert.Equal(DnssecStatuses.Disabled, disabled.Status);

        var error = await harness.Client.Dnssec.GetAsync();
        Assert.False(error.IsPending);
        Assert.False(error.IsActive);
        Assert.Equal(DnssecStatuses.Error, error.Status);
        Assert.Equal(4, harness.RequestCount);
    }

    [Fact]
    public async Task GetAsync_uses_the_configured_zone_and_accepts_an_explicit_one()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{ "status": "disabled" }""");
        harness.RespondWithResult("""{ "status": "disabled" }""");

        await harness.Client.Dnssec.GetAsync();
        Assert.Equal(RootPath, harness.LastRequest.Path);

        await harness.Client.Dnssec.GetAsync("other");
        Assert.Equal("/client/v4/zones/other/dnssec", harness.LastRequest.Path);
    }

    [Fact]
    public async Task GetAsync_throws_when_no_zone_id_is_available()
    {
        using var harness = CloudflareHarness.Create(options => options.ZoneId = null);

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(() => harness.Client.Dnssec.GetAsync());

        Assert.Contains("CLOUDFLARE_ZONE_ID", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task GetAsync_surfaces_a_DNS_validation_error_with_its_code()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.BadRequest, 1004, "DNS Validation Error");

        var exception = await Assert.ThrowsAsync<CloudflareApiException>(() => harness.Client.Dnssec.GetAsync());

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.True(exception.HasErrorCode(1004));
        Assert.True(exception.IsValidationFailure);
        Assert.False(exception.IsRateLimited);
    }

    [Fact]
    public async Task UpdateAsync_sends_only_the_fields_that_are_set()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(DnssecJson);
        harness.RespondWithResult(DnssecJson);

        await harness.Client.Dnssec.UpdateAsync(new UpdateDnssecRequest { Status = DnssecSettableStatuses.Active });
        Assert.Equal(HttpMethod.Patch, harness.LastRequest.Method);
        Assert.Equal(RootPath, harness.LastRequest.Path);
        Assert.Equal("""{"status":"active"}""", harness.LastRequest.Body);

        // A PATCH here merges: the status is untouched when only the switches are sent.
        await harness.Client.Dnssec.UpdateAsync(
            UpdateDnssecRequest.SigningOptions(multiSigner: true, useNsec3: false));
        Assert.Equal("""{"dnssec_multi_signer":true,"dnssec_use_nsec3":false}""", harness.LastRequest.Body);
    }

    [Fact]
    public async Task UpdateAsync_keeps_a_switch_that_is_turned_off_on_the_wire()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(DnssecJson);
        harness.RespondWithResult(DnssecJson);

        // The serializer drops nulls, not false values - "off" has to survive the round trip.
        await harness.Client.Dnssec.UpdateAsync(UpdateDnssecRequest.SigningOptions(presigned: false));
        Assert.Equal("""{"dnssec_presigned":false}""", harness.LastRequest.Body);

        await harness.Client.Dnssec.UpdateAsync(
            new UpdateDnssecRequest { DnssecUseNsec3 = false, DnssecMultiSigner = false });
        Assert.Equal("""{"dnssec_multi_signer":false,"dnssec_use_nsec3":false}""", harness.LastRequest.Body);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("pending-disabled")]
    [InlineData("error")]
    public async Task UpdateAsync_rejects_the_three_statuses_only_Cloudflare_can_report(string status)
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Dnssec.UpdateAsync(new UpdateDnssecRequest { Status = status }));

        Assert.Contains("a state Cloudflare reports", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_request_that_changes_nothing()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Dnssec.UpdateAsync(new UpdateDnssecRequest()));

        Assert.Contains("something to change", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task UpdateAsync_throws_before_sending_when_the_request_is_null()
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => harness.Client.Dnssec.UpdateAsync(null!));

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task EnableAsync_and_DisableAsync_send_the_two_settable_statuses()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{ "status": "pending", "ds": "example.com. 3600 IN DS 12345 13 2 ABCD" }""");
        harness.RespondWithResult("""{ "status": "pending-disabled" }""");

        var enabled = await harness.Client.Dnssec.EnableAsync();
        Assert.Equal(HttpMethod.Patch, harness.LastRequest.Method);
        Assert.Equal(RootPath, harness.LastRequest.Path);
        Assert.Equal("""{"status":"active"}""", harness.LastRequest.Body);

        // The DS record is what the caller has to hand to the registrar, so it comes back on the enable.
        Assert.Contains("IN DS 12345", enabled.Ds, StringComparison.Ordinal);
        Assert.True(enabled.IsPending);

        var disabled = await harness.Client.Dnssec.DisableAsync();
        Assert.Equal("""{"status":"disabled"}""", harness.LastRequest.Body);
        Assert.True(disabled.IsPending);
        Assert.False(disabled.IsActive);
    }

    [Fact]
    public async Task DeleteAsync_returns_the_bare_string_result_and_null_when_there_is_none()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("""{"success":true,"errors":[],"messages":[],"result":"DNSSEC records deleted"}""");
        harness.Respond("""{"success":true,"errors":[],"messages":[],"result":null}""");

        // One of the few endpoints whose result is a string rather than an object.
        var message = await harness.Client.Dnssec.DeleteAsync();
        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.Equal(RootPath, harness.LastRequest.Path);
        Assert.Equal("DNSSEC records deleted", message);

        Assert.Null(await harness.Client.Dnssec.DeleteAsync());
    }

    [Fact]
    public async Task DeleteAsync_throws_when_Cloudflare_reports_a_failure()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.Forbidden, 10000, "Zone is not yours");

        var exception = await Assert.ThrowsAsync<CloudflareApiException>(() => harness.Client.Dnssec.DeleteAsync());

        Assert.True(exception.HasErrorCode(10000));
        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }

    [Fact]
    public async Task ListZoneSigningKeysAsync_reads_the_PascalCase_payload()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(ZskJson);

        var keys = await harness.Client.Dnssec.ListZoneSigningKeysAsync();

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Equal(ZskPath, harness.LastRequest.Path);
        Assert.Equal(string.Empty, harness.LastRequest.Query);

        var key = Assert.Single(keys);
        Assert.Equal(13, key.DnsKey!.Algorithm);
        Assert.Equal(257, key.DnsKey.Flags);
        Assert.True(key.DnsKey.IsKeySigningKey);

        // The header is the one nested object whose five fields are all published.
        Assert.Equal("example.com.", key.DnsKey.Hdr!.Name);
        Assert.Equal(1, key.DnsKey.Hdr.Class);
        Assert.Equal(48, key.DnsKey.Hdr.Rrtype);
        Assert.Equal(67, key.DnsKey.Hdr.Rdlength);
        Assert.Equal(3600, key.DnsKey.Hdr.Ttl);
        Assert.Equal("example.com. type=48 class=1 ttl=3600", key.DnsKey.Hdr.ToString());

        // This endpoint is spelled like a DNS library, not like the rest of the API, and the fields whose
        // types are not published stay untyped rather than guessed.
        Assert.True(key.AdditionalData!.ContainsKey("Location"));
        Assert.True(key.AdditionalData.ContainsKey("Name"));
        Assert.True(key.AdditionalData.ContainsKey("some_new_zsk_field"));
        Assert.True(key.DnsKey.AdditionalData!.ContainsKey("Protocol"));
        Assert.Equal("KSK flags=257 algorithm=13 name=example.com.", key.DnsKey.ToString());
        // Four, not two: the schema counts five fields at this level and only DNSKEY is typed, so
        // Location, Name, KeyTag and anything new all land in the extension data.
        Assert.Equal("algorithm 13, flags 257, 4 untyped field(s)", key.ToString());
    }

    [Fact]
    public async Task ListZoneSigningKeysAsync_returns_an_empty_list_and_never_pages()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");

        var keys = await harness.Client.Dnssec.ListZoneSigningKeysAsync();

        Assert.Empty(keys);
        Assert.DoesNotContain("per_page", harness.LastRequest.Query, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ZSK_with_the_other_flag_is_a_zone_signing_key_not_a_key_signing_one()
    {
        var zsk = new DnssecDnsKey { Flags = 256, Algorithm = 13 };

        Assert.False(zsk.IsKeySigningKey);
        Assert.Equal("ZSK flags=256 algorithm=13 name=?", zsk.ToString());
    }

    [Fact]
    public void The_reported_and_the_settable_statuses_stay_two_lists()
    {
        Assert.Equal(
            new[] { "active", "pending", "disabled", "pending-disabled", "error" },
            DnssecStatuses.All);
        Assert.Equal(new[] { "active", "disabled" }, DnssecSettableStatuses.All);

        // Every status that can be asked for can also be reported, and three cannot be asked for.
        Assert.All(DnssecSettableStatuses.All, status => Assert.Contains(status, DnssecStatuses.All));
        Assert.Equal(
            DnssecSettableStatuses.All.Count + 3,
            DnssecStatuses.All.Count);
        Assert.DoesNotContain("pending", DnssecSettableStatuses.All);
        Assert.DoesNotContain("error", DnssecSettableStatuses.All);
    }

    [Fact]
    public void The_state_and_the_requests_describe_themselves()
    {
        Assert.Equal("(no DNSSEC state)", new Dnssec().ToString());
        Assert.Equal("disabled", new Dnssec { Status = DnssecStatuses.Disabled }.ToString());
        Assert.Equal("active", new Dnssec { Status = DnssecStatuses.Active, Algorithm = "13" }.ToString());
        Assert.Equal("status active", UpdateDnssecRequest.Enable().ToString());
        Assert.Equal("status disabled", UpdateDnssecRequest.Disable().ToString());
        Assert.Equal("(no change)", new UpdateDnssecRequest().ToString());
        Assert.Equal(
            "multi-signer on, presigned off, NSEC3 on",
            UpdateDnssecRequest.SigningOptions(true, false, true).ToString());
        Assert.Equal(
            "status active, NSEC3 off",
            new UpdateDnssecRequest { Status = "active", DnssecUseNsec3 = false }.ToString());
        Assert.Equal("(empty)", new DnssecZoneSigningKey().ToString());
        Assert.Equal("2 untyped field(s)", new DnssecZoneSigningKey
        {
            AdditionalData = new Dictionary<string, JsonElement>
            {
                ["Location"] = default,
                ["Name"] = default,
            },
        }.ToString());
        Assert.Equal("? type=? class=? ttl=?", new DnssecRecordHeader().ToString());
    }

    [Fact]
    public void The_signing_switches_are_independent_of_the_status()
    {
        var request = UpdateDnssecRequest.SigningOptions(useNsec3: true);

        // Turning NSEC3 on does not enable DNSSEC, and the body says so by leaving the status out.
        Assert.Null(request.Status);
        Assert.True(request.DnssecUseNsec3);
        Assert.Null(request.DnssecMultiSigner);
        Assert.Null(request.DnssecPresigned);
        request.EnsureValid();
    }
}

/// <summary>
/// One zone setting is addressed by two different ids: the one in the path, and the one Cloudflare puts
/// in the response. Both are pinned here because they were discovered by a live run answering
/// <c>origin_tls_cmpl</c> to a request for <c>origin_tls_compliance_modes</c>, and nothing offline would
/// ever have caught it.
/// </summary>
public class ZoneSettingIdTests
{
    [Fact]
    public void The_compliance_modes_setting_has_a_path_id_and_a_reported_id()
    {
        Assert.Equal("origin_tls_compliance_modes", ZoneSettingIds.OriginTlsComplianceModes);
        Assert.Equal("origin_tls_cmpl", ZoneSettingIds.OriginTlsComplianceModesReportedId);
    }

    [Fact]
    public void The_reported_id_is_an_abbreviation_of_the_path_id_and_not_a_truncation_of_it()
    {
        // Worth pinning, because the tempting explanation is wrong. The reported id is fifteen characters
        // long, but the first fifteen of the path id are 'origin_tls_comp': Cloudflare compresses
        // 'compliance' to 'cmpl' rather than cutting the string. Anything that assumed a prefix match would
        // quietly stop working here, so the relationship is stated as what it is - same prefix, same length
        // as observed, different spelling.
        Assert.Equal(
            "origin_tls_",
            ZoneSettingIds.OriginTlsComplianceModesReportedId[.."origin_tls_".Length]);
        Assert.Equal(15, ZoneSettingIds.OriginTlsComplianceModesReportedId.Length);
        Assert.NotEqual(
            ZoneSettingIds.OriginTlsComplianceModesReportedId,
            ZoneSettingIds.OriginTlsComplianceModes[..15]);
        // And neither is a prefix of the other, which is what makes the two constants irreducible: no
        // amount of trimming or padding turns one into the other.
        Assert.False(ZoneSettingIds.OriginTlsComplianceModes
            .StartsWith(ZoneSettingIds.OriginTlsComplianceModesReportedId));
        Assert.False(ZoneSettingIds.OriginTlsComplianceModesReportedId
            .StartsWith(ZoneSettingIds.OriginTlsComplianceModes));
    }

    [Fact]
    public void A_setting_read_back_under_either_id_deserialises_into_the_same_model()
    {
        // The library does not rewrite the id, so a caller comparing it against the constant they sent has
        // to know which one Cloudflare used. Both spellings have to land in OriginTlsComplianceModes.
        foreach (var id in new[]
                 {
                     ZoneSettingIds.OriginTlsComplianceModes,
                     ZoneSettingIds.OriginTlsComplianceModesReportedId,
                 })
        {
            var modes = JsonSerializer.Deserialize<OriginTlsComplianceModes>(
                $$"""{"id":"{{id}}","editable":true,"value":["fips"]}""",
                CloudflareJson.Default);

            Assert.NotNull(modes);
            Assert.Equal(id, modes!.Id);
            Assert.Equal(new[] { "fips" }, modes.Value);
        }
    }

    [Fact]
    public void The_neighbouring_setting_ids_are_not_truncated_themselves()
    {
        // Both are longer than the compliance modes setting's reported id and were read back in the same
        // live run without complaint, so the abbreviation is specific to that one setting rather than a rule
        // about the settings API. Their reported ids are logged by the integration run, which is what would
        // show that changing.
        Assert.Equal("auto_origin_tls_kex", ZoneSettingIds.AutoOriginTlsKex);
        Assert.Equal("ssl_automatic_mode", ZoneSettingIds.SslAutomaticMode);
        Assert.True(ZoneSettingIds.AutoOriginTlsKex.Length > 15);
        Assert.True(ZoneSettingIds.SslAutomaticMode.Length > 15);
    }
}

/// <summary>
/// <c>POST /zones/{zone_id}/dns_records/batch</c> and <c>GET …/dns_records/scan/review</c>. The batch body
/// is the part Cloudflare documents; its successful result is not, which is why the service hands that back
/// raw and these tests assert on the request rather than inventing a response model.
/// </summary>
public class DnsRecordBatchTests
{
    [Fact]
    public async Task BatchAsync_sends_the_groups_it_was_given_in_one_post()
    {
        using var harness = CloudflareHarness.Create();
        // Stubbed with the answer a live run actually returned, because Cloudflare publishes the request
        // body and not this: groups come back as they went in, an unused group as an explicit null, a used
        // one as complete records carrying the id Cloudflare assigned.
        harness.RespondWithResult("""{"deletes":null,"patches":null,"puts":null,"posts":[{"id":"rec-new-1","name":"batch.test","type":"TXT","content":"cf-net","proxiable":false,"proxied":false,"ttl":120,"settings":{},"meta":{},"comment":null,"tags":[],"created_on":"2026-10-13T08:44:29.031191Z","modified_on":"2026-10-13T08:44:29.031191Z"}]}""");

        var batch = new DnsRecordBatch()
            .WithPosts(new DnsRecord
            {
                Name = "batch.test",
                Type = DnsRecordTypes.Txt,
                Content = "cf-net",
                Ttl = 120,
            })
            .WithDeletes("rec-1", "rec-2");

        var result = await harness.Client.DnsRecords.BatchAsync(batch);

        Assert.Equal(
            $"/client/v4/zones/{CloudflareHarness.TestZoneId}/dns_records/batch",
            harness.LastRequest.Path);
        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        // Asserted as separate fragments on purpose: which field a record serialises first is a property
        // declaration order, not part of the contract, and pinning it would make this test brittle.
        Assert.Contains("\"posts\":[{", harness.LastRequest.Body!, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"batch.test\"", harness.LastRequest.Body!, StringComparison.Ordinal);
        Assert.Contains("\"content\":\"cf-net\"", harness.LastRequest.Body!, StringComparison.Ordinal);
        Assert.Contains("\"deletes\":[{\"id\":\"rec-1\"},{\"id\":\"rec-2\"}]", harness.LastRequest.Body!, StringComparison.Ordinal);
        // Groups that were not used are left out entirely rather than sent as null.
        Assert.DoesNotContain("patches", harness.LastRequest.Body!, StringComparison.Ordinal);
        Assert.DoesNotContain("puts", harness.LastRequest.Body!, StringComparison.Ordinal);

        Assert.NotNull(result);
        Assert.Null(result!.Deletes);
        Assert.Null(result.Patches);
        var created = Assert.Single(result.Posts!);
        Assert.Equal("rec-new-1", created.Id);
        Assert.Equal("batch.test", created.Name);
        Assert.Equal(2026, created.CreatedOn?.Year);
        Assert.Equal(1, result.AffectedCount);
        Assert.Equal(new[] { "rec-new-1" }, result.AffectedIds);
    }

    [Fact]
    public async Task A_delete_batch_echoes_the_records_it_removed()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"deletes":[{"id":"rec-1","name":"batch.test","type":"TXT","content":"cf-net","proxiable":false,"proxied":false,"ttl":120,"settings":{},"meta":{},"comment":null,"tags":[],"created_on":"2026-10-13T08:44:29.031191Z","modified_on":"2026-10-13T08:44:29.031191Z"}],"patches":null,"puts":null,"posts":null}""");

        var result = await harness.Client.DnsRecords.BatchAsync(new DnsRecordBatch().WithDeletes("rec-1"));

        // Worth pinning: the answer to a delete carries the removed record in full - name, type, content,
        // ttl and timestamps - so this is the only place its content survives the delete.
        Assert.NotNull(result);
        var removed = Assert.Single(result!.Deletes!);
        Assert.Equal("rec-1", removed.Id);
        Assert.Equal("batch.test", removed.Name);
        Assert.Equal("cf-net", removed.Content);
        Assert.Null(result.Posts);
        Assert.Equal("batch result: 1 deleted, 0 patched, 0 replaced, 0 created", result.ToString());
    }

    [Fact]
    public async Task BatchAsync_adds_include_shadow_metadata_only_when_asked()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{}");
        harness.RespondWithResult("{}");

        var batch = new DnsRecordBatch().WithDeletes("rec-1");

        await harness.Client.DnsRecords.BatchAsync(batch);
        await harness.Client.DnsRecords.BatchAsync(batch, includeShadowMetadata: true);

        Assert.Equal(string.Empty, harness.Requests[0].Query);
        Assert.Equal("?include_shadow_metadata=true", harness.Requests[1].Query);
    }

    [Fact]
    public async Task BatchAsync_rejects_an_empty_batch_before_sending_anything()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(() =>
            harness.Client.DnsRecords.BatchAsync(new DnsRecordBatch()));

        // 9207 is what Cloudflare answers an empty body with, observed live, so it is worth quoting.
        Assert.Contains("9207", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task BatchAsync_names_every_delete_and_patch_that_has_no_id()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(() =>
            harness.Client.DnsRecords.BatchAsync(new DnsRecordBatch
            {
                Deletes = new List<DnsRecordBatchDelete> { new() { Id = "rec-1" }, new() },
                Patches = new List<DnsRecord> { new() { Comment = "no id here" } },
            }));

        Assert.Contains("deletes[1]", exception.Message, StringComparison.Ordinal);
        Assert.Contains("patches[0]", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("deletes[0]", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public void A_batch_counts_its_changes_and_says_what_it_is()
    {
        var empty = new DnsRecordBatch();
        Assert.True(empty.IsEmpty);
        Assert.Equal(0, empty.ChangeCount);
        Assert.Equal("(empty batch)", empty.ToString());

        var batch = new DnsRecordBatch
        {
            Deletes = new List<DnsRecordBatchDelete> { new() { Id = "rec-1" } },
            Patches = new List<DnsRecord> { new() { Id = "rec-2" } },
            Puts = new List<DnsRecord> { new() { Id = "rec-3" } },
            Posts = new List<DnsRecord> { new() { Name = "a.test" }, new() { Name = "b.test" } },
        };

        Assert.False(batch.IsEmpty);
        Assert.Equal(5, batch.ChangeCount);
        Assert.Equal("batch: 1 delete(s), 1 patch(es), 1 put(s), 2 post(s)", batch.ToString());
    }

    [Fact]
    public async Task ListScannedRecordsAsync_uses_the_scan_review_path()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""[{"id":"scanned-1","name":"www.test","type":"A","content":"198.51.100.4","ttl":1}]""");

        var page = await harness.Client.DnsRecords.ListScannedRecordsAsync();

        Assert.Equal(
            $"/client/v4/zones/{CloudflareHarness.TestZoneId}/dns_records/scan/review",
            harness.LastRequest.Path);
        Assert.Equal(string.Empty, harness.LastRequest.Query);
        var record = Assert.Single(page.Items);
        Assert.Equal("scanned-1", record.Id);
        Assert.Equal(DnsRecordTypes.A, record.Type);
    }

    [Fact]
    public async Task ListScannedRecordsAsync_passes_per_page_only_when_given()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");
        harness.RespondWithResult("[]");

        await harness.Client.DnsRecords.ListScannedRecordsAsync();
        await harness.Client.DnsRecords.ListScannedRecordsAsync(perPage: 5);

        Assert.Equal(string.Empty, harness.Requests[0].Query);
        Assert.Equal("?per_page=5", harness.Requests[1].Query);
    }

    [Fact]
    public async Task ListScannedRecordsAsync_answers_an_empty_page_for_a_zone_never_scanned()
    {
        using var harness = CloudflareHarness.Create();
        // The live API answers 200 with an empty list here, which is a normal answer and not a refusal.
        harness.RespondWithResult("[]");

        var page = await harness.Client.DnsRecords.ListScannedRecordsAsync();

        Assert.Equal(0, page.Count);
        Assert.False(page.HasMore);
        Assert.Null(page.TotalCount);
    }
}
