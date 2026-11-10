using CFNet.Exceptions;
using CFNet.Http;
using CFNet.Models.DurableObjects;
using CFNet.UnitTests.Testing;
using Xunit;

namespace CFNet.UnitTests.Services;

/// <summary>
/// Durable Objects: the namespaces an account owns and the objects inside one. Both endpoints are read-only, and
/// they disagree about pagination - the namespaces list takes <c>page</c> and <c>per_page</c>, the objects list
/// takes a cursor and <c>limit</c> - which is the part worth pinning, because a caller who assumed one style for
/// both would silently ask for nothing.
/// </summary>
public class DurableObjectsServiceTests
{
    /// <summary>The namespace id every object fact lists.</summary>
    private const string NamespaceId = "0a65e2ea92e4ce5f4a9e1f40c9d4d2cf";

    [Fact]
    public async Task ListNamespacesAsync_reads_the_namespaces_an_account_owns()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":[{"id":"0a65e2ea92e4ce5f4a9e1f40c9d4d2cf","class":"Counter","name":"Counter","script":"my-worker","use_sqlite":false},{"id":"1b76f3fb03f5df6a5b0f2g51dae5e3dg","class":"Session","name":"Session","script":"my-worker","use_sqlite":true}],"result_info":{"page":1,"per_page":2,"total_pages":1,"count":2,"total_count":2}}""");

        var page = await harness.Client.DurableObjects.ListNamespacesAsync();

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Equal(
            "/client/v4/accounts/acct-1234/workers/durable_objects/namespaces",
            harness.LastRequest.Path);
        Assert.Equal(string.Empty, harness.LastRequest.Query.TrimStart('?'));

        Assert.Equal(2, page.Count);
        Assert.Equal(2, page.TotalCount);
        Assert.False(page.HasMore);

        var first = page.Items[0];
        Assert.Equal("0a65e2ea92e4ce5f4a9e1f40c9d4d2cf", first.Id);
        Assert.Equal("Counter", first.Class);
        Assert.Equal("Counter", first.Name);
        Assert.Equal("my-worker", first.Script);
        Assert.False(first.UseSqlite);
        Assert.Equal("Counter on my-worker", first.ToString());
        Assert.True(first.AdditionalData is not { Count: > 0 });

        // The SQLite storage choice is what tells a caller which API an object answers.
        var second = page.Items[1];
        Assert.True(second.UseSqlite);
        Assert.Equal("Session on my-worker (sqlite)", second.ToString());
    }

    [Fact]
    public async Task ListNamespacesAsync_sends_only_the_two_parameters_the_endpoint_publishes()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage("[]", 2, 4, 0, 71);

        var page = await harness.Client.DurableObjects.ListNamespacesAsync(
            new DurableObjectNamespaceListParameters { Page = 2, PerPage = 1000 });

        Assert.Equal("page=2&per_page=1000", harness.LastRequest.Query.TrimStart('?'));

        // Page-number pagination, so no cursor is sent and none is read back.
        Assert.DoesNotContain("cursor", harness.LastRequest.Query);
        Assert.Equal(0, page.Count);
        Assert.Equal(71, page.TotalCount);
        Assert.True(page.HasMore);
        Assert.Equal(3, page.NextPage);
    }

    [Fact]
    public async Task ListNamespacesAsync_uses_the_account_it_is_given_instead_of_the_configured_one()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage("[]", 1, 1, 0, 0);

        await harness.Client.DurableObjects.ListNamespacesAsync(accountId: "another-account");

        Assert.Equal(
            "/client/v4/accounts/another-account/workers/durable_objects/namespaces",
            harness.LastRequest.Path);
    }

    [Fact]
    public async Task ListAllNamespacesAsync_walks_every_page_and_asks_for_the_next_one_by_number()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":[{"class":"Counter","script":"w1"},{"class":"Session","script":"w1"}],"result_info":{"page":1,"per_page":2,"total_pages":2,"count":2,"total_count":3}}""");
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":[{"class":"Queue","script":"w2"}],"result_info":{"page":2,"per_page":2,"total_pages":2,"count":1,"total_count":3}}""");

        var seen = new List<string?>();
        await foreach (var item in harness.Client.DurableObjects.ListAllNamespacesAsync(pageSize: 2))
        {
            seen.Add(item.Class);
        }

        Assert.Equal(new[] { "Counter", "Session", "Queue" }, seen);
        Assert.Equal(2, harness.RequestCount);
        Assert.Equal("page=2&per_page=2", harness.LastRequest.Query.TrimStart('?'));
    }

    [Fact]
    public async Task ListAllNamespacesAsync_stops_when_a_page_Cloudflare_promised_comes_back_empty()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":[],"result_info":{"page":1,"per_page":50,"total_pages":9,"count":0,"total_count":400}}""");

        var seen = new List<DurableObjectNamespace>();
        await foreach (var item in harness.Client.DurableObjects.ListAllNamespacesAsync())
        {
            seen.Add(item);
        }

        // result_info says there is more; an empty page says there is not, and trusting the first would loop.
        Assert.Empty(seen);
        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task ListObjectsAsync_pages_by_cursor_and_sends_limit_rather_than_per_page()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":[{"id":"0044e6cd9c1b44d9a5e0f0e6f3d1c2b3","hasStoredData":true}],"result_info":{"cursors":{"before":"","after":"cur-next"}}}""");

        var page = await harness.Client.DurableObjects.ListObjectsAsync(
            NamespaceId,
            new DurableObjectListParameters { Limit = 50 });

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Equal(
            $"/client/v4/accounts/acct-1234/workers/durable_objects/namespaces/{NamespaceId}/objects",
            harness.LastRequest.Path);
        Assert.Equal("limit=50", harness.LastRequest.Query.TrimStart('?'));
        Assert.DoesNotContain("per_page", harness.LastRequest.Query);

        // This endpoint is the one place in the API this library wraps that publishes a camelCase field.
        var item = page.Items[0];
        Assert.Equal("0044e6cd9c1b44d9a5e0f0e6f3d1c2b3", item.Id);
        Assert.True(item.HasStoredData);
        Assert.Equal("0044e6cd9c1b44d9a5e0f0e6f3d1c2b3 (has stored data)", item.ToString());
        Assert.Equal("cur-next", page.NextCursor);
    }

    [Fact]
    public async Task ListObjectsAsync_escapes_a_namespace_id_it_is_handed()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("""{"success":true,"errors":[],"messages":[],"result":[]}""");

        await harness.Client.DurableObjects.ListObjectsAsync("ns id/../other");

        // Uri.AbsolutePath decodes %2F, so the encoding is checked on the Uri itself.
        Assert.Contains(
            "/accounts/acct-1234/workers/durable_objects/namespaces/ns%20id%2F..%2Fother/objects",
            harness.LastRequest.Uri!.OriginalString,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListObjectsAsync_refuses_a_namespace_id_that_is_not_there()
    {
        using var harness = CloudflareHarness.Create();

        var error = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.DurableObjects.ListObjectsAsync("   "));

        Assert.Contains("namespaceId", error.Message);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task ListAllObjectsAsync_follows_the_cursors_Cloudflare_answers_with()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":[{"id":"a1","hasStoredData":true},{"id":"a2","hasStoredData":false}],"result_info":{"cursors":{"after":"cur-2"}}}""");
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":[{"id":"a3"}],"result_info":{"cursors":{"after":""}}}""");

        var seen = new List<string?>();
        await foreach (var item in harness.Client.DurableObjects.ListAllObjectsAsync(NamespaceId, pageSize: 2))
        {
            seen.Add(item.Id);
        }

        Assert.Equal(new[] { "a1", "a2", "a3" }, seen);
        Assert.Equal(2, harness.RequestCount);
        Assert.Equal("cursor=cur-2&limit=2", harness.LastRequest.Query.TrimStart('?'));
    }

    [Fact]
    public async Task ListAllObjectsAsync_stops_when_Cloudflare_hands_back_the_cursor_it_was_given()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":[{"id":"a1"}],"result_info":{"cursors":{"after":"cur-1"}}}""");
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":[{"id":"a1"}],"result_info":{"cursors":{"after":"cur-1"}}}""");

        var seen = new List<string?>();
        await foreach (var item in harness.Client.DurableObjects.ListAllObjectsAsync(NamespaceId))
        {
            seen.Add(item.Id);
        }

        // The second page repeats both the cursor and the object, so an unchanged cursor is the end of the walk.
        Assert.Equal(new[] { "a1", "a1" }, seen);
        Assert.Equal(2, harness.RequestCount);
    }

    [Fact]
    public async Task ListAllObjectsAsync_stops_when_a_page_carries_no_objects_even_though_it_carries_a_cursor()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(
            """{"success":true,"errors":[],"messages":[],"result":[],"result_info":{"cursors":{"after":"cur-1"}}}""");

        var seen = new List<DurableObject>();
        await foreach (var item in harness.Client.DurableObjects.ListAllObjectsAsync(NamespaceId))
        {
            seen.Add(item);
        }

        Assert.Empty(seen);
        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public void The_object_parameters_keep_their_limit_when_a_walk_moves_to_the_next_cursor()
    {
        var parameters = new DurableObjectListParameters { Cursor = "cur-1", Limit = 25 };

        var next = parameters.WithCursor("cur-2");

        Assert.Equal("cur-1", parameters.Cursor);
        Assert.Equal("cur-2", next.Cursor);
        Assert.Equal(25, next.Limit);

        var query = new QueryStringBuilder();
        next.Apply(query);
        Assert.Equal("cursor=cur-2&limit=25", query.ToString().TrimStart('?'));
    }
}
