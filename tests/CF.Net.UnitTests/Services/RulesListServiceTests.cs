using System.Net;
using System.Text;
using CFNet.Exceptions;
using CFNet.Models.Rules;
using CFNet.UnitTests.Testing;
using Xunit;

namespace CFNet.UnitTests.Services;

/// <summary>
/// Account Lists - <c>/accounts/{account_id}/rules/lists</c> - and the bulk operations their item changes
/// answer with. The shapes asserted here come from the published API schema rather than from a guess: a
/// list carries <c>num_referencing_filters</c>, a redirect entry carries
/// <c>preserve_query_string</c> and <c>subpath_matching</c>, a hostname entry carries
/// <c>url_hostname</c>, and the update endpoint is a <c>PUT</c> that only takes a description.
/// </summary>
public class RulesListServiceTests
{
    /// <summary>Two lists shaped like the live ones, one of each interesting kind.</summary>
    private const string TwoLists = """
        [
          {"id":"list-1","name":"my_redirects","kind":"redirect","num_items":2,"num_referencing_filters":1,"created_on":"2026-01-02T03:04:05Z","modified_on":"2026-01-03T03:04:05Z","description":"old to new"},
          {"id":"list-2","name":"blocked_ips","kind":"ip","num_items":17,"num_referencing_filters":0,"created_on":"2026-01-02T03:04:05Z","modified_on":"2026-01-02T03:04:05Z"}
        ]
        """;

    /// <summary>Builds a 200 carrying an envelope with the given result JSON.</summary>
    private static HttpResponseMessage Ok(string resultJson) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            "{\"success\":true,\"errors\":[],\"messages\":[],\"result\":" + resultJson + "}",
            Encoding.UTF8,
            "application/json"),
    };

    // ---------------------------------------------------------------- the list itself

    [Fact]
    public async Task ListAsync_reads_every_list_the_account_has()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("{\"success\":true,\"errors\":[],\"messages\":[],\"result\":" + TwoLists + "}");

        var lists = await harness.Client.Lists.ListAsync();

        // No result_info comes back, so there is nothing to page through: this is the whole set.
        Assert.Contains("/accounts/acct-1234/rules/lists", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal(string.Empty, harness.LastRequest.Query);
        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Equal(2, lists.Count);
        Assert.Equal("my_redirects", lists[0].Name);
        Assert.Equal(2, lists[0].NumItems);
        Assert.Equal(1, lists[0].NumReferencingFilters);
        Assert.True(lists[0].IsRedirectList);
        Assert.Equal(new DateTimeOffset(2026, 1, 3, 3, 4, 5, TimeSpan.Zero), lists[0].ModifiedOn);
        Assert.Equal("my_redirects (redirect, 2 item(s))", lists[0].ToString());
        Assert.False(lists[1].IsRedirectList);
        Assert.Null(lists[1].Description);
    }

    [Fact]
    public async Task ListAsync_returns_an_empty_set_rather_than_null()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("{\"success\":true,\"errors\":[],\"messages\":[],\"result\":[]}");

        var lists = await harness.Client.Lists.ListAsync();

        Assert.Empty(lists);
    }

    [Fact]
    public async Task GetAsync_reads_one_list()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """{"id":"list-1","name":"my_redirects","kind":"redirect","num_items":2,"num_referencing_filters":0}""");

        var list = await harness.Client.Lists.GetAsync("list-1");

        Assert.Contains("/accounts/acct-1234/rules/lists/list-1", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal("list-1", list!.Id);
        Assert.Equal(RulesListKind.Redirect, list.Kind);
    }

    [Fact]
    public async Task GetAsync_returns_null_when_there_is_no_such_list()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.NotFound, 7003, "List not found");

        var list = await harness.Client.Lists.GetAsync("nope");

        Assert.Null(list);
    }

    [Fact]
    public async Task CreateAsync_sends_the_name_and_kind_the_list_will_be_stuck_with()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """{"id":"list-9","name":"my_redirects","kind":"redirect","num_items":0,"num_referencing_filters":0}""");

        var list = await harness.Client.Lists.CreateAsync(new CreateRulesListRequest
        {
            Name = "my_redirects",
            Kind = RulesListKind.Redirect,
            Description = "old to new",
        });

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.EndsWith("/accounts/acct-1234/rules/lists", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal(
            """{"name":"my_redirects","kind":"redirect","description":"old to new"}""",
            harness.LastRequest.Body);
        Assert.Equal("list-9", list.Id);
    }

    [Fact]
    public async Task CreateAsync_refuses_a_list_with_no_name_or_kind()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Lists.CreateAsync(new CreateRulesListRequest()));

        // The kind cannot be changed afterwards, so both gaps are named before anything is sent.
        Assert.Equal(2, exception.Problems.Count);
        Assert.Contains("name", exception.Problems[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("kind", exception.Problems[1], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task UpdateAsync_puts_a_description_and_nothing_else()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """{"id":"list-1","name":"my_redirects","kind":"redirect","num_items":2,"description":"new purpose"}""");

        var list = await harness.Client.Lists.UpdateAsync(
            "list-1",
            new UpdateRulesListRequest { Description = "new purpose" });

        // A PUT, not a PATCH, and the description is the whole of what it accepts.
        Assert.Equal(HttpMethod.Put, harness.LastRequest.Method);
        Assert.Contains("/rules/lists/list-1", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal("""{"description":"new purpose"}""", harness.LastRequest.Body);
        Assert.Equal("new purpose", list.Description);
    }

    [Fact]
    public async Task DeleteAsync_returns_the_id_cloudflare_echoes()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"id":"list-1"}""");

        var deleted = await harness.Client.Lists.DeleteAsync("list-1");

        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.Equal("list-1", deleted);
    }

    // ---------------------------------------------------------------- items

    [Fact]
    public async Task ListItemsAsync_pages_by_cursor_and_filters_by_search()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("""
            {"success":true,"errors":[],"messages":[],
             "result":[{"id":"item-1","ip":"198.51.100.7","created_on":"2026-01-02T03:04:05Z","modified_on":"2026-01-02T03:04:05Z"}],
             "result_info":{"cursors":{"after":"cur-2"}}}
            """);

        var page = await harness.Client.Lists.ListItemsAsync(
            "list-2",
            new RulesListItemListParameters { After = "cur-1", PerPage = 50, Search = "198.51" });

        Assert.Contains("/rules/lists/list-2/items", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal("?after=cur-1&per_page=50&search=198.51", harness.LastRequest.Query);

        var item = Assert.Single(page.Items);
        Assert.Equal("198.51.100.7", item.Ip);
        Assert.Equal(RulesListKind.Ip, item.ValueKind);
        Assert.Equal("198.51.100.7", item.ToString());

        // The cursor is what the next call takes, not a page number.
        Assert.Equal("cur-2", page.NextCursor);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task ListItemsAsync_reads_a_redirect_entry_and_its_flags()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("""
            {"success":true,"errors":[],"messages":[],
             "result":[{"id":"item-3","created_on":"2026-01-02T03:04:05Z","modified_on":"2026-01-02T03:04:05Z",
                        "comment":"launch redirect",
                        "redirect":{"source_url":"example.com/old","target_url":"example.com/new","status_code":301,
                                    "include_subdomains":false,"preserve_query_string":true,"preserve_path_suffix":true,
                                    "subpath_matching":false}}]}
            """);

        var item = Assert.Single((await harness.Client.Lists.ListItemsAsync("list-1")).Items);

        Assert.Equal(RulesListKind.Redirect, item.ValueKind);
        Assert.Equal("launch redirect", item.Comment);
        Assert.Equal("example.com/old", item.Redirect!.SourceUrl);
        Assert.Equal("example.com/new", item.Redirect.TargetUrl);
        Assert.Equal(301, item.Redirect.StatusCode);
        Assert.True(item.Redirect.PreserveQueryString);
        Assert.True(item.Redirect.PreservePathSuffix);
        Assert.False(item.Redirect.IncludeSubdomains);
        Assert.False(item.Redirect.SubpathMatching);
        Assert.Equal("example.com/old -301-> example.com/new", item.ToString());
    }

    [Fact]
    public async Task ListItemsAsync_reads_hostname_and_asn_entries()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("""
            {"success":true,"errors":[],"messages":[],
             "result":[
               {"id":"item-4","hostname":{"url_hostname":"example.com","exclude_exact_hostname":true}},
               {"id":"item-5","asn":13335}
             ]}
            """);

        var items = (await harness.Client.Lists.ListItemsAsync("list-3")).Items;

        Assert.Equal(RulesListKind.Hostname, items[0].ValueKind);
        Assert.Equal("example.com", items[0].Hostname!.UrlHostname);
        Assert.True(items[0].Hostname!.ExcludeExactHostname);
        // The exact hostname is excluded, which is what the label has to say.
        Assert.Equal("*.example.com (not example.com)", items[0].ToString());

        Assert.Equal(RulesListKind.Asn, items[1].ValueKind);
        Assert.Equal(13335, items[1].Asn);
        Assert.Equal("13335", items[1].ToString());
    }

    [Fact]
    public async Task GetItemAsync_reads_one_item_and_returns_null_when_it_is_gone()
    {
        using var found = CloudflareHarness.Create();
        found.RespondWithResult("""{"id":"item-1","ip":"198.51.100.7"}""");
        var item = await found.Client.Lists.GetItemAsync("list-2", "item-1");
        Assert.Contains("/rules/lists/list-2/items/item-1", found.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal("198.51.100.7", item!.Ip);

        using var missing = CloudflareHarness.Create();
        missing.RespondWithError(HttpStatusCode.NotFound, 7003, "Item not found");
        Assert.Null(await missing.Client.Lists.GetItemAsync("list-2", "gone"));
    }

    [Fact]
    public async Task CreateItemsAsync_sends_the_items_as_a_bare_array()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"operation_id":"op-1"}""");

        var receipt = await harness.Client.Lists.CreateItemsAsync(
            "list-1",
            new[]
            {
                CreateRulesListItemRequest.ForRedirect("example.com/old", "example.com/new", 301),
                CreateRulesListItemRequest.ForRedirect("a.test/x", "b.test/y"),
            });

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.EndsWith("/rules/lists/list-1/items", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal(
            """[{"redirect":{"source_url":"example.com/old","target_url":"example.com/new","status_code":301}},{"redirect":{"source_url":"a.test/x","target_url":"b.test/y"}}]""",
            harness.LastRequest.Body);
        Assert.Equal("op-1", receipt.OperationId);
        Assert.Equal("op-1", receipt.ToString());
    }

    [Fact]
    public async Task CreateItemsAsync_refuses_a_batch_cloudflare_could_not_apply()
    {
        // Nothing is sent for any of these: an empty batch is a mistake rather than a no-op, an item with
        // no value has nothing to store, and a mixed batch cannot fit one list whichever kind it is.
        using var empty = CloudflareHarness.Create();
        var noItems = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => empty.Client.Lists.CreateItemsAsync("list-1", Array.Empty<CreateRulesListItemRequest>()));
        Assert.Contains("no items", noItems.Problems[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, empty.RequestCount);

        using var valueless = CloudflareHarness.Create();
        var nothingToStore = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => valueless.Client.Lists.CreateItemsAsync(
                "list-1",
                new[] { new CreateRulesListItemRequest(), new CreateRulesListItemRequest { Comment = "only a comment" } }));
        Assert.Contains("2 item(s) carry no value", nothingToStore.Problems[0], StringComparison.Ordinal);
        Assert.Equal(0, valueless.RequestCount);

        using var mixed = CloudflareHarness.Create();
        var mixedKinds = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => mixed.Client.Lists.CreateItemsAsync(
                "list-1",
                new[]
                {
                    CreateRulesListItemRequest.ForIp("198.51.100.7"),
                    CreateRulesListItemRequest.ForRedirect("a.test/x", "b.test/y"),
                }));
        Assert.Contains("ip, redirect", mixedKinds.Problems[0], StringComparison.Ordinal);
        Assert.Equal(0, mixed.RequestCount);
    }

    [Fact]
    public async Task ReplaceItemsAsync_puts_the_whole_set_and_can_clear_it()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"operation_id":"op-2"}""");

        await harness.Client.Lists.ReplaceItemsAsync(
            "list-1",
            new[] { CreateRulesListItemRequest.ForRedirect("only.test/a", "only.test/b", 302) });

        Assert.Equal(HttpMethod.Put, harness.LastRequest.Method);
        Assert.Equal(
            """[{"redirect":{"source_url":"only.test/a","target_url":"only.test/b","status_code":302}}]""",
            harness.LastRequest.Body);

        // A PUT with nothing in it empties the list, which is the one call that may send an empty array.
        await harness.Client.Lists.ReplaceItemsAsync("list-1", Array.Empty<CreateRulesListItemRequest>());
        Assert.Equal("[]", harness.LastRequest.Body);
    }

    [Fact]
    public async Task DeleteItemsAsync_sends_the_ids_inside_a_body()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"operation_id":"op-3"}""");

        var receipt = await harness.Client.Lists.DeleteItemsAsync(
            "list-1",
            new[] { "item-1", " item-2 ", "item-2" });

        // A DELETE with a body, and the ids are trimmed and de-duplicated before they go.
        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.Equal("""{"items":[{"id":"item-1"},{"id":"item-2"}]}""", harness.LastRequest.Body);
        Assert.Equal("op-3", receipt.OperationId);
    }

    [Fact]
    public async Task DeleteItemsAsync_needs_an_id_to_remove()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Lists.DeleteItemsAsync("list-1", new[] { "  " }));

        // Clearing a list is a replacement, not a deletion of nothing, and the message says which call.
        Assert.Contains("ReplaceItemsAsync", exception.Problems[0], StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    // ---------------------------------------------------------------- bulk operations

    [Fact]
    public async Task GetBulkOperationAsync_reads_each_shape_cloudflare_answers_with()
    {
        using var pending = CloudflareHarness.Create();
        pending.RespondWithResult("""{"id":"op-1","status":"pending"}""");
        var queued = await pending.Client.Lists.GetBulkOperationAsync("op-1");
        Assert.Contains("/rules/lists/bulk_operations/op-1", pending.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal(RulesListBulkOperationStatus.Pending, queued!.Status);
        Assert.False(queued.IsFinished);
        Assert.False(queued.HasFailed);
        Assert.Null(queued.Completed);
        Assert.Equal("op-1: pending", queued.ToString());

        using var done = CloudflareHarness.Create();
        done.RespondWithResult("""{"id":"op-1","status":"completed","completed":"2026-01-02T03:04:05Z"}""");
        var finished = await done.Client.Lists.GetBulkOperationAsync("op-1");
        Assert.True(finished!.IsFinished);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), finished.Completed);
        Assert.Null(finished.Error);

        using var failed = CloudflareHarness.Create();
        failed.RespondWithResult(
            """{"id":"op-1","status":"failed","completed":"2026-01-02T03:04:05Z","error":"invalid CIDR"}""");
        var broken = await failed.Client.Lists.GetBulkOperationAsync("op-1");
        Assert.True(broken!.IsFinished);
        Assert.True(broken.HasFailed);
        Assert.Equal("invalid CIDR", broken.Error);
        Assert.Equal("op-1: failed (invalid CIDR)", broken.ToString());
    }

    [Fact]
    public async Task GetBulkOperationAsync_returns_null_when_cloudflare_does_not_know_the_id()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.NotFound, 7003, "Bulk operation not found");

        // An operation that aged out looks the same as one that never existed, and neither is an error
        // worth throwing on when the caller only asked what state it is in.
        Assert.Null(await harness.Client.Lists.GetBulkOperationAsync("op-old"));
    }

    [Fact]
    public async Task WaitForBulkOperationAsync_polls_until_the_operation_finishes()
    {
        using var harness = CloudflareHarness.CreateWithResponder((_, index) =>
            index == 0
                ? Ok("""{"id":"op-1","status":"running"}""")
                : Ok("""{"id":"op-1","status":"completed","completed":"2026-01-02T03:04:05Z"}"""));

        var operation = await harness.Client.Lists.WaitForBulkOperationAsync(
            "op-1",
            timeout: TimeSpan.FromSeconds(10),
            pollInterval: TimeSpan.FromMilliseconds(1));

        Assert.True(operation.IsFinished);
        Assert.False(operation.HasFailed);
        Assert.Equal(2, harness.RequestCount);
    }

    [Fact]
    public async Task WaitForBulkOperationAsync_stops_at_once_on_a_failure()
    {
        using var harness = CloudflareHarness.CreateWithResponder(
            (_, _) => Ok("""{"id":"op-1","status":"failed","error":"invalid CIDR","completed":"2026-01-02T03:04:05Z"}"""));

        var operation = await harness.Client.Lists.WaitForBulkOperationAsync(
            "op-1",
            timeout: TimeSpan.FromSeconds(10),
            pollInterval: TimeSpan.FromMilliseconds(1));

        // A failed operation is finished, so there is nothing left to wait for.
        Assert.True(operation.HasFailed);
        Assert.Equal("invalid CIDR", operation.Error);
        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task WaitForBulkOperationAsync_returns_the_last_state_when_the_time_runs_out()
    {
        using var harness = CloudflareHarness.CreateWithResponder(
            (_, _) => Ok("""{"id":"op-1","status":"running"}"""));

        var operation = await harness.Client.Lists.WaitForBulkOperationAsync(
            "op-1",
            timeout: TimeSpan.FromMilliseconds(40),
            pollInterval: TimeSpan.FromMilliseconds(10));

        // Not an error: the caller gets the state it was in when the wait ended and decides what to do.
        Assert.False(operation.IsFinished);
        Assert.Equal(RulesListBulkOperationStatus.Running, operation.Status);
        Assert.True(harness.RequestCount > 1, $"expected more than one poll, saw {harness.RequestCount}");
    }

    [Fact]
    public async Task WaitForBulkOperationAsync_reports_an_unknown_operation_instead_of_polling_it()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.NotFound, 7003, "Bulk operation not found");

        // Waiting on an id Cloudflare never issued would spin until the timeout, so the miss is an error
        // here even though the read on its own returns null.
        var exception = await Assert.ThrowsAsync<CloudflareApiException>(
            () => harness.Client.Lists.WaitForBulkOperationAsync(
                "op-never",
                timeout: TimeSpan.FromSeconds(5),
                pollInterval: TimeSpan.FromMilliseconds(1)));

        Assert.Contains("7003", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, harness.RequestCount);
    }

    // ---------------------------------------------------------------- the request helpers

    [Fact]
    public void The_item_helpers_each_build_one_kind_of_value()
    {
        Assert.Equal(RulesListKind.Ip, CreateRulesListItemRequest.ForIp("198.51.100.7", "one host").ValueKind);
        Assert.Equal("one host", CreateRulesListItemRequest.ForIp("198.51.100.7", "one host").Comment);
        Assert.Equal(
            RulesListKind.Redirect,
            CreateRulesListItemRequest.ForRedirect("a.test/x", "b.test/y", 308).ValueKind);
        Assert.Equal(
            RulesListKind.Hostname,
            CreateRulesListItemRequest.ForHostname("example.com", excludeExactHostname: true).ValueKind);
        Assert.Equal(RulesListKind.Asn, CreateRulesListItemRequest.ForAsn(13335).ValueKind);
        Assert.Null(new CreateRulesListItemRequest().ValueKind);
    }

    [Fact]
    public void A_list_without_a_name_says_so_rather_than_printing_an_empty_label()
    {
        Assert.Equal("(unnamed list)", new RulesList().ToString());
        Assert.Equal("(empty)", new RulesListItem().ToString());
        Assert.Equal("a.test -> b.test", new RulesListRedirect
        {
            SourceUrl = "a.test",
            TargetUrl = "b.test",
        }.ToString());
        Assert.Equal(string.Empty, new RulesListHostname().ToString());
    }
}
