using System.Net;
using CFNet.Exceptions;
using CFNet.Http;
using CFNet.Models.Common;
using CFNet.Models.Dns;
using CFNet.Models.Zones;
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
}
