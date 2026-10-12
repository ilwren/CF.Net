using System.Net;
using System.Text.Json;
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
