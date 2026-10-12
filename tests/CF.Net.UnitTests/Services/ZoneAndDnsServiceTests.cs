using System.Net;
using CFNet.Exceptions;
using CFNet.Http;
using CFNet.Models.Common;
using CFNet.Models.Dns;
using CFNet.Models.Ssl;
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
    public async Task A_field_the_model_does_not_name_is_kept_rather_than_dropped()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage($"[{PackJson}]", page: 1, totalPages: 1, count: 1, totalCount: 1);

        var pack = Assert.Single((await harness.Client.Ssl.ListAsync()).Items);

        Assert.NotNull(pack.AdditionalData);
        Assert.Equal("txt", pack.AdditionalData!["validation_method"].GetString());
        Assert.Equal(365, pack.AdditionalData["validity_days"].GetInt32());
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
        // geo_restrictions is not modelled, so it survives in the extension data instead of being dropped.
        Assert.Equal("eu", certificate.AdditionalData!["geo_restrictions"].GetProperty("label").GetString());
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
