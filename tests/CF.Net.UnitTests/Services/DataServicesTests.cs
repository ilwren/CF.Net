using System.Net;
using CFNet.Exceptions;
using CFNet.Models.Common;
using CFNet.Models.D1;
using CFNet.Models.Kv;
using CFNet.Models.R2;
using CFNet.UnitTests.Testing;
using Xunit;

namespace CFNet.UnitTests.Services;

public class D1DatabaseServiceTests
{
    private const string DatabaseId = "db-1";

    [Fact]
    public async Task ListAsync_uses_the_singular_database_path_and_a_name_filter()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage(
            "[{\"uuid\":\"db-1\",\"name\":\"app\"},{\"uuid\":\"db-2\",\"name\":\"analytics\"}]",
            page: 1, totalPages: 1, count: 2, totalCount: 2);

        var page = await harness.Client.D1.ListAsync(new D1DatabaseListParameters { Name = "app", Page = 1, PerPage = 50 });

        Assert.Equal($"/client/v4/accounts/{CloudflareHarness.TestAccountId}/d1/database", harness.LastRequest.Path);
        Assert.Equal("page=1&per_page=50&name=app", harness.LastRequest.Query.TrimStart('?'));
        Assert.Equal(2, page.Count);
        Assert.Equal("db-1", page.Items[0].Uuid);
    }

    [Fact]
    public async Task ListAllAsync_walks_pages_and_stops_on_a_short_one()
    {
        using var harness = CloudflareHarness.Create();
        harness
            .RespondWithPage("[{\"uuid\":\"db-1\"},{\"uuid\":\"db-2\"}]", page: 1, totalPages: 2, count: 2, totalCount: 3)
            .RespondWithPage("[{\"uuid\":\"db-3\"}]", page: 2, totalPages: 2, count: 1, totalCount: 3);

        var databases = await harness.Client.D1.ListAllAsync().CollectAsync();

        Assert.Equal(3, databases.Count);
        Assert.Equal(2, harness.RequestCount);
    }

    [Fact]
    public async Task GetByNameAsync_matches_the_name_case_insensitively()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage("[{\"uuid\":\"db-2\",\"name\":\"APP\"},{\"uuid\":\"db-1\",\"name\":\"other\"}]", page: 1, totalPages: 1, count: 2, totalCount: 2);

        var database = await harness.Client.D1.GetByNameAsync("app");

        Assert.Equal("db-2", database!.Uuid);
        Assert.Equal("per_page=100&name=app", harness.LastRequest.Query.TrimStart('?'));
    }

    [Fact]
    public async Task GetByNameAsync_returns_null_when_nothing_matches()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");

        Assert.Null(await harness.Client.D1.GetByNameAsync("nope"));
    }

    [Fact]
    public async Task GetAsync_returns_null_when_the_database_is_gone()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.NotFound, 7403, "D1 database not found");

        Assert.Null(await harness.Client.D1.GetAsync(DatabaseId));
        Assert.Equal($"/client/v4/accounts/{CloudflareHarness.TestAccountId}/d1/database/{DatabaseId}", harness.LastRequest.Path);
    }

    [Fact]
    public async Task CreateAsync_sends_the_name_and_location_hint()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"uuid\":\"db-new\",\"name\":\"app\",\"primary_location_hint\":\"weur\"}");

        var database = await harness.Client.D1.CreateAsync("app", "weur");

        Assert.Equal("db-new", database.Uuid);
        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.Contains("{\"name\":\"app\",\"primary_location_hint\":\"weur\"}", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_omits_the_location_hint_when_none_is_given()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"uuid\":\"db-new\"}");

        await harness.Client.D1.CreateAsync("app");

        Assert.Equal("{\"name\":\"app\"}", harness.LastRequest.Body);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_missing_name()
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<CloudflareConfigurationException>(() => harness.Client.D1.CreateAsync(new CreateD1DatabaseRequest()));
        await Assert.ThrowsAsync<CloudflareConfigurationException>(() => harness.Client.D1.CreateAsync("  "));
        await Assert.ThrowsAsync<ArgumentNullException>(() => harness.Client.D1.CreateAsync((CreateD1DatabaseRequest)null!));
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task DeleteAsync_targets_the_database()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("null");

        await harness.Client.D1.DeleteAsync(DatabaseId);

        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.EndsWith($"/d1/database/{DatabaseId}", harness.LastRequest.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryAsync_sends_a_single_statement_and_returns_the_first_result()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""
        [ { "results": [ { "id": 1, "name": "alpha" } ], "success": true, "meta": { "rows_read": 1, "rows_written": 0, "duration": 0.4 } } ]
        """);

        var result = await harness.Client.D1.QueryAsync(DatabaseId, "SELECT * FROM t WHERE id = ?", new object?[] { 1 });

        Assert.True(result.Success);
        Assert.Equal(1, result.RowCount);
        Assert.Equal("alpha", result.Rows<Row>().Single().Name);
        Assert.Equal(1, result.Meta!.RowsRead);

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.EndsWith($"/d1/database/{DatabaseId}/query", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Contains("\"sql\":\"SELECT * FROM t WHERE id = ?\"", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"params\":[1]", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"batch\"", harness.LastRequest.Body, StringComparison.Ordinal);

        // D1 has no "action" field; a client that invents one gets a 400.
        Assert.DoesNotContain("action", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryAsync_omits_params_when_none_are_supplied()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");

        await harness.Client.D1.QueryAsync(DatabaseId, "SELECT 1");

        Assert.Equal("{\"sql\":\"SELECT 1\"}", harness.LastRequest.Body);
    }

    [Fact]
    public async Task QueryAsync_returns_a_failed_result_when_the_array_is_empty()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");

        var result = await harness.Client.D1.QueryAsync(DatabaseId, "SELECT 1");

        Assert.False(result.Success);
        Assert.Equal(0, result.RowCount);
    }

    [Fact]
    public async Task BatchAsync_sends_the_batch_shape_and_returns_every_result()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""
        [ { "success": true, "meta": { "changes": 1 } }, { "success": true, "meta": { "changes": 2 } } ]
        """);

        var results = await harness.Client.D1.BatchAsync(DatabaseId, new[]
        {
            new D1Query("INSERT INTO t VALUES (1)"),
            new D1Query("INSERT INTO t VALUES (?)", new object?[] { 2 }),
        });

        Assert.Equal(2, results.Count);
        Assert.Equal(1, results[0].Meta!.Changes);
        Assert.Equal(2, results[1].Meta!.Changes);

        Assert.StartsWith("{\"batch\":[", harness.LastRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"params\":[2]", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BatchAsync_rejects_an_empty_or_incomplete_batch()
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<CloudflareConfigurationException>(() => harness.Client.D1.BatchAsync(DatabaseId, Array.Empty<D1Query>()));
        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.D1.BatchAsync(DatabaseId, new[] { new D1Query("SELECT 1"), new D1Query() }));
        await Assert.ThrowsAsync<ArgumentNullException>(() => harness.Client.D1.BatchAsync(DatabaseId, null!));
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task ExecuteAsync_sends_a_prebuilt_request_verbatim()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[{\"success\":true}]");

        var results = await harness.Client.D1.ExecuteAsync(DatabaseId, D1QueryRequest.Single("PRAGMA table_info(t)"));

        Assert.Single(results);
        Assert.Contains("\"sql\":\"PRAGMA table_info(t)\"", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListBackupsAsync_and_GetBackupAsync_use_the_backup_sub_resource()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage("[{\"id\":\"b-1\",\"state\":\"complete\",\"size\":1024}]", page: 1, totalPages: 1, count: 1, totalCount: 1);

        var backups = await harness.Client.D1.ListBackupsAsync(DatabaseId);

        Assert.EndsWith($"/d1/database/{DatabaseId}/backup", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal("complete", Assert.Single(backups.Items).State);

        harness.RespondWithResult("{\"id\":\"b-1\"}");
        var backup = await harness.Client.D1.GetBackupAsync(DatabaseId, "b-1");

        Assert.Equal("b-1", backup!.Id);
        Assert.EndsWith($"/d1/database/{DatabaseId}/backup/b-1", harness.LastRequest.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetBackupAsync_returns_null_when_the_backup_is_gone()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.NotFound, 7403, "not found");

        Assert.Null(await harness.Client.D1.GetBackupAsync(DatabaseId, "b-x"));
    }

    [Fact]
    public async Task RestoreBackupAsync_posts_without_a_body()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("null");

        await harness.Client.D1.RestoreBackupAsync(DatabaseId, "b-1");

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.EndsWith($"/d1/database/{DatabaseId}/backup/b-1/restore", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Null(harness.LastRequest.Body);
    }

    [Fact]
    public async Task A_d1_failure_is_surfaced_with_its_error_code()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("""
        {"success":false,"errors":[{"code":7403,"message":"D1_DATABASE_NOT_FOUND: database 'nope' does not exist"}],"messages":[],"result":null}
        """, HttpStatusCode.NotFound);

        var exception = await Assert.ThrowsAsync<CloudflareApiException>(() => harness.Client.D1.QueryAsync("nope", "SELECT 1"));

        Assert.Equal(7403, exception.ErrorCode);
        Assert.True(exception.IsNotFound);
        Assert.Contains("D1_DATABASE_NOT_FOUND", exception.Message, StringComparison.Ordinal);
    }

    private sealed class Row
    {
        public int Id { get; set; }

        public string? Name { get; set; }
    }
}

public class KvServiceTests
{
    private const string NamespaceId = "ns-1";

    [Fact]
    public async Task ListNamespacesAsync_uses_the_storage_path()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage(
            "[{\"id\":\"ns-1\",\"title\":\"sessions\",\"supports_url_encoding\":true}]",
            page: 1, totalPages: 1, count: 1, totalCount: 1);

        var page = await harness.Client.Kv.ListNamespacesAsync(new KvNamespaceListParameters { Search = "sess", Page = 1, PerPage = 20 });

        Assert.Equal($"/client/v4/accounts/{CloudflareHarness.TestAccountId}/storage/kv/namespaces", harness.LastRequest.Path);
        Assert.Equal("page=1&per_page=20&search=sess", harness.LastRequest.Query.TrimStart('?'));
        Assert.Equal("sessions", Assert.Single(page.Items).Title);
        Assert.True(page.Items[0].SupportsUrlEncoding);
    }

    [Fact]
    public async Task GetNamespaceByTitleAsync_matches_the_title()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage("[{\"id\":\"ns-2\",\"title\":\"sessions-cache\"},{\"id\":\"ns-1\",\"title\":\"sessions\"}]", page: 1, totalPages: 1, count: 2, totalCount: 2);

        var ns = await harness.Client.Kv.GetNamespaceByTitleAsync("sessions");

        // "search" is a substring filter, so the service insists on an exact title match.
        Assert.Equal("ns-1", ns!.Id);
        Assert.Equal("per_page=100&search=sessions", harness.LastRequest.Query.TrimStart('?'));
    }

    [Fact]
    public async Task GetNamespaceAsync_returns_null_when_it_is_gone()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.NotFound, 10007, "not found");

        Assert.Null(await harness.Client.Kv.GetNamespaceAsync(NamespaceId));
        Assert.EndsWith($"/storage/kv/namespaces/{NamespaceId}", harness.LastRequest.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateNamespaceAsync_posts_the_title()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"ns-new\",\"title\":\"sessions\"}");

        var ns = await harness.Client.Kv.CreateNamespaceAsync("sessions");

        Assert.Equal("ns-new", ns.Id);
        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.Equal("{\"title\":\"sessions\"}", harness.LastRequest.Body);
    }

    [Fact]
    public async Task RenameNamespaceAsync_puts_the_new_title()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"id\":\"ns-1\",\"title\":\"renamed\"}");

        await harness.Client.Kv.RenameNamespaceAsync(NamespaceId, "renamed");

        Assert.Equal(HttpMethod.Put, harness.LastRequest.Method);
        Assert.EndsWith($"/storage/kv/namespaces/{NamespaceId}", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal("{\"title\":\"renamed\"}", harness.LastRequest.Body);
    }

    [Fact]
    public async Task DeleteNamespaceAsync_deletes()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("null");

        await harness.Client.Kv.DeleteNamespaceAsync(NamespaceId);

        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
    }

    [Fact]
    public async Task ListKeysAsync_uses_limit_and_cursor_not_per_page()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");

        await harness.Client.Kv.ListKeysAsync(NamespaceId, new KvKeyListParameters
        {
            Prefix = "users/",
            Limit = 500,
            Cursor = "abc",
        });

        Assert.EndsWith($"/storage/kv/namespaces/{NamespaceId}/keys", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal("prefix=users%2F&limit=500&cursor=abc", harness.LastRequest.Query.TrimStart('?'));
        Assert.DoesNotContain("per_page", harness.LastRequest.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListKeysAsync_parses_key_summaries_without_values()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("""
        {
          "success": true, "errors": [], "messages": [],
          "result": [
            { "name": "users/1", "expiration": 1893456000, "metadata": { "role": "admin" } },
            { "name": "users/2" }
          ],
          "result_info": { "count": 2, "cursor": "next-cursor" }
        }
        """);

        var page = await harness.Client.Kv.ListKeysAsync(NamespaceId);

        Assert.Equal(2, page.Count);
        Assert.Equal("users/1", page.Items[0].Name);
        Assert.Equal(1893456000, page.Items[0].Expiration);
        Assert.Equal(2030, page.Items[0].ExpiresAt!.Value.Year);
        Assert.Equal("admin", page.Items[0].GetMetadata<Meta>()!.Role);
        Assert.Null(page.Items[1].Metadata);
        Assert.Null(page.Items[1].GetMetadata<Meta>());
        Assert.Equal("next-cursor", page.NextCursor);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task ListAllKeysAsync_follows_the_cursor()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("""
        {"success":true,"errors":[],"messages":[],"result":[{"name":"a"},{"name":"b"}],"result_info":{"count":2,"cursor":"c-2"}}
        """);
        harness.Respond("""
        {"success":true,"errors":[],"messages":[],"result":[{"name":"c"}],"result_info":{"count":1,"cursor":""}}
        """);

        var keys = await harness.Client.Kv.ListAllKeysAsync(NamespaceId, prefix: "u/").CollectAsync();

        Assert.Equal(new[] { "a", "b", "c" }, keys.Select(key => key.Name));
        Assert.Equal(2, harness.RequestCount);
        Assert.Equal("prefix=u%2F", harness.Requests[0].Query.TrimStart('?'));
        Assert.Equal("prefix=u%2F&cursor=c-2", harness.Requests[1].Query.TrimStart('?'));
    }

    [Fact]
    public async Task GetValueAsync_reads_the_raw_body_and_the_metadata_header()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondRaw(
            "{\"text\":\"hello\"}"u8.ToArray(),
            "application/json",
            HttpStatusCode.OK,
            new Dictionary<string, string> { ["CF-KV-Metadata"] = "{\"role\":\"admin\",\"size\":19}" });

        var value = await harness.Client.Kv.GetValueAsync(NamespaceId, "users/1/profile");

        Assert.True(value.Found);
        Assert.Equal("users/1/profile", value.Key);
        Assert.Equal("application/json", value.ContentType);
        Assert.Equal("{\"text\":\"hello\"}", value.Text);
        Assert.Equal("admin", value.GetMetadata<Meta>()!.Role);
        Assert.Equal("hello", value.AsJson<Greeting>()!.Text);

        // The key is percent-encoded so a slash inside it stays data, not a path separator.
        Assert.Contains("/storage/kv/namespaces/ns-1/values/users%2F1%2Fprofile", harness.LastRequest.Uri!.OriginalString, StringComparison.Ordinal);
        Assert.Contains("values/users%2F1%2Fprofile", harness.LastRequest.Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
    }

    [Fact]
    public async Task GetValueAsync_reports_a_missing_key_instead_of_throwing()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondRaw(Array.Empty<byte>(), "text/plain", HttpStatusCode.NotFound);

        var value = await harness.Client.Kv.GetValueAsync(NamespaceId, "absent");

        Assert.False(value.Found);
        Assert.Equal(HttpStatusCode.NotFound, value.StatusCode);
        Assert.Empty(value.Content);
        Assert.Equal(string.Empty, value.Text);
        Assert.Null(value.Metadata);
    }

    [Fact]
    public async Task GetStringAsync_returns_null_for_a_missing_key()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondRaw(Array.Empty<byte>(), "text/plain", HttpStatusCode.NotFound);

        Assert.Null(await harness.Client.Kv.GetStringAsync(NamespaceId, "absent"));
    }

    [Fact]
    public async Task GetJsonAsync_deserialises_the_stored_payload()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondRaw("{\"text\":\"hi\"}"u8.ToArray(), "application/json");

        var value = await harness.Client.Kv.GetJsonAsync<Greeting>(NamespaceId, "g");

        Assert.Equal("hi", value!.Text);
    }

    [Fact]
    public async Task GetJsonAsync_returns_default_for_a_missing_key()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondRaw(Array.Empty<byte>(), "text/plain", HttpStatusCode.NotFound);

        Assert.Null(await harness.Client.Kv.GetJsonAsync<Greeting>(NamespaceId, "absent"));
    }

    [Fact]
    public async Task PutStringAsync_sends_the_value_as_the_raw_body()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("null");

        await harness.Client.Kv.PutStringAsync(NamespaceId, "greeting", "hello");

        Assert.Equal(HttpMethod.Put, harness.LastRequest.Method);
        Assert.Equal("hello", harness.LastRequest.Body);
        Assert.Equal("text/plain", harness.LastRequest.ContentType);
        Assert.EndsWith("/storage/kv/namespaces/ns-1/values/greeting", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal(string.Empty, harness.LastRequest.Query);
    }

    [Fact]
    public async Task PutStringAsync_sends_the_ttl_as_a_query_parameter()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("null");

        await harness.Client.Kv.PutStringAsync(NamespaceId, "greeting", "hello", expirationTtl: 300);

        Assert.Equal("?expiration_ttl=300", harness.LastRequest.Query);
    }

    [Fact]
    public async Task PutJsonAsync_serialises_the_value_and_uses_the_json_media_type()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("null");

        await harness.Client.Kv.PutJsonAsync(NamespaceId, "profile", new Greeting { Text = "hi" });

        Assert.Equal("{\"text\":\"hi\"}", harness.LastRequest.Body);
        Assert.Equal("application/json", harness.LastRequest.ContentType);
    }

    [Fact]
    public async Task PutValueAsync_sends_an_absolute_expiration_as_a_query_parameter()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("null");

        var expiry = DateTimeOffset.UtcNow.AddHours(1);
        await harness.Client.Kv.PutValueAsync(NamespaceId, "k", KvPutValueRequest.FromBytes(new byte[] { 1, 2, 3 }).WithExpiration(expiry));

        Assert.Equal($"?expiration={expiry.ToUnixTimeSeconds()}", harness.LastRequest.Query);
        Assert.Equal("application/octet-stream", harness.LastRequest.ContentType);
    }

    [Fact]
    public async Task PutValueAsync_rejects_both_expiry_forms()
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<CloudflareConfigurationException>(() => harness.Client.Kv.PutValueAsync(
            NamespaceId,
            "k",
            new KvPutValueRequest { Content = "x"u8.ToArray(), Expiration = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(), ExpirationTtl = 300 }));

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task PutValueAsync_rejects_a_ttl_below_sixty_seconds()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Kv.PutValueAsync(NamespaceId, "k", KvPutValueRequest.FromText("x").WithTtl(59)));

        Assert.Contains("at least 60", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PutValueAsync_rejects_an_already_expired_timestamp()
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<CloudflareConfigurationException>(() => harness.Client.Kv.PutValueAsync(
            NamespaceId,
            "k",
            new KvPutValueRequest { Content = "x"u8.ToArray(), Expiration = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds() }));
    }

    [Fact]
    public async Task PutStringAsync_rejects_a_null_value()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Kv.PutStringAsync(NamespaceId, "k", null!));

        Assert.Contains("DeleteValueAsync", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteValueAsync_removes_the_key()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("null");

        await harness.Client.Kv.DeleteValueAsync(NamespaceId, "users/1");

        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.Contains("/storage/kv/namespaces/ns-1/values/users%2F1", harness.LastRequest.Uri!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BulkWriteAsync_puts_and_deletes_in_one_call()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("null");

        await harness.Client.Kv.BulkWriteAsync(NamespaceId, new[]
        {
            KvBulkWriteOperation.Put("a", "1"),
            new KvBulkWriteOperation
            {
                Key = "b",
                Value = Convert.ToBase64String(new byte[] { 2 }),
                Base64 = true,
                ExpirationTtl = 300,
            },
            KvBulkWriteOperation.Remove("c"),
        });

        Assert.Equal(HttpMethod.Put, harness.LastRequest.Method);
        Assert.EndsWith($"/storage/kv/namespaces/{NamespaceId}/bulk", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal("[{\"key\":\"a\",\"value\":\"1\"},{\"key\":\"b\",\"value\":\"Ag==\",\"base64\":true,\"expiration_ttl\":300},{\"key\":\"c\",\"delete\":true}]", harness.LastRequest.Body);
    }

    [Fact]
    public async Task BulkWriteAsync_rejects_an_empty_or_incomplete_batch()
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Kv.BulkWriteAsync(NamespaceId, Array.Empty<KvBulkWriteOperation>()));
        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Kv.BulkWriteAsync(NamespaceId, new[] { new KvBulkWriteOperation { Key = "a" } }));
        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Kv.BulkWriteAsync(NamespaceId, new[] { KvBulkWriteOperation.Put("a", "1"), new KvBulkWriteOperation { Value = "no key" } }));
        await Assert.ThrowsAsync<ArgumentNullException>(() => harness.Client.Kv.BulkWriteAsync(NamespaceId, null!));
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public void Kv_put_request_factories_set_the_right_media_type()
    {
        Assert.Equal("text/plain", KvPutValueRequest.FromText("x").ContentType);
        Assert.Equal("application/octet-stream", KvPutValueRequest.FromBytes(new byte[] { 1 }).ContentType);
        Assert.Equal("image/png", KvPutValueRequest.FromBytes(new byte[] { 1 }, "image/png").ContentType);
        Assert.Equal("application/json", KvPutValueRequest.FromJson(new Greeting()).ContentType);
        Assert.Equal("x"u8.ToArray(), KvPutValueRequest.FromText("x").Content);
    }

    [Fact]
    public void A_kv_put_request_accepts_a_valid_expiry()
    {
        KvPutValueRequest.FromText("x").WithTtl(60).Validate();
        KvPutValueRequest.FromText("x").WithExpiration(DateTimeOffset.UtcNow.AddHours(1)).Validate();
        KvPutValueRequest.FromText("x").Validate();
    }

    private sealed class Greeting
    {
        public string? Text { get; set; }
    }

    private sealed class Meta
    {
        public string? Role { get; set; }

        public int? Size { get; set; }
    }
}

public class R2ServiceTests
{
    [Fact]
    public async Task ListBucketsAsync_unwraps_the_buckets_object()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("""
        {
          "success": true, "errors": [], "messages": [],
          "result": {
            "buckets": [
              { "name": "assets", "creation_date": "2024-01-01T00:00:00Z", "location": "weur", "jurisdiction": "default", "storage_class": "Standard" },
              { "name": "logs", "creation_date": "2024-02-01T00:00:00Z" }
            ]
          },
          "result_info": { "cursor": "next", "per_page": 100 }
        }
        """);

        var page = await harness.Client.R2.ListBucketsAsync();

        Assert.Equal($"/client/v4/accounts/{CloudflareHarness.TestAccountId}/r2/buckets", harness.LastRequest.Path);
        Assert.Equal(2, page.Count);
        Assert.Equal("assets", page.Items[0].Name);
        Assert.Equal(R2LocationHint.Weur, page.Items[0].Location);
        Assert.Equal(R2Jurisdiction.Default, page.Items[0].Jurisdiction);
        Assert.Equal(R2StorageClass.Standard, page.Items[0].StorageClass);
        Assert.Equal(2024, page.Items[0].CreationDate?.Year);
        Assert.Null(page.Items[1].Location);
        Assert.Equal("next", page.NextCursor);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task ListBucketsAsync_applies_the_documented_filters()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"buckets\":[]}");

        await harness.Client.R2.ListBucketsAsync(new R2BucketListParameters
        {
            PerPage = 25,
            Cursor = "c-1",
            NameContains = "log",
            Order = "name",
            Direction = SortDirection.Ascending,
            StartAfter = "abc",
        });

        Assert.Equal(
            "cursor=c-1&per_page=25&name_contains=log&order=name&direction=asc&start_after=abc",
            harness.LastRequest.Query.TrimStart('?'));
    }

    [Fact]
    public async Task ListAllBucketsAsync_follows_the_cursor()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("""
        {"success":true,"errors":[],"messages":[],"result":{"buckets":[{"name":"a"}]},"result_info":{"cursor":"c-2"}}
        """);
        harness.Respond("""
        {"success":true,"errors":[],"messages":[],"result":{"buckets":[{"name":"b"}]},"result_info":{"cursor":""}}
        """);

        var buckets = await harness.Client.R2.ListAllBucketsAsync().CollectAsync();

        Assert.Equal(new[] { "a", "b" }, buckets.Select(bucket => bucket.Name));
        Assert.Equal(2, harness.RequestCount);
        Assert.Equal("cursor=c-2", harness.Requests[1].Query.TrimStart('?'));
    }

    [Fact]
    public async Task GetBucketAsync_returns_null_when_the_bucket_is_gone()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.NotFound, 10007, "The specified bucket does not exist");

        Assert.Null(await harness.Client.R2.GetBucketAsync("missing"));
        Assert.EndsWith("/r2/buckets/missing", harness.LastRequest.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateBucketAsync_uses_camel_case_in_the_body()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"name\":\"assets\",\"location\":\"weur\",\"storage_class\":\"Standard\"}");

        var bucket = await harness.Client.R2.CreateBucketAsync(
            "assets",
            locationHint: R2LocationHint.Weur,
            storageClass: R2StorageClass.Standard);

        Assert.Equal("assets", bucket.Name);
        Assert.Equal(R2LocationHint.Weur, bucket.Location);

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.Equal("{\"name\":\"assets\",\"locationHint\":\"weur\",\"storageClass\":\"Standard\"}", harness.LastRequest.Body);

        // No jurisdiction was requested, so no header.
        Assert.Null(harness.LastRequest.Header("cf-r2-jurisdiction"));
    }

    [Fact]
    public async Task CreateBucketAsync_sends_the_jurisdiction_as_a_header_not_in_the_body()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"name\":\"eu-assets\"}");

        await harness.Client.R2.CreateBucketAsync("eu-assets", jurisdiction: R2Jurisdiction.Eu);

        Assert.Equal("eu", harness.LastRequest.Header("cf-r2-jurisdiction"));
        Assert.Equal("{\"name\":\"eu-assets\"}", harness.LastRequest.Body);
    }

    [Fact]
    public async Task The_jurisdiction_header_is_sent_on_every_bucket_call()
    {
        using var harness = CloudflareHarness.Create();

        harness.RespondWithResult("{\"name\":\"eu-assets\"}");
        await harness.Client.R2.GetBucketAsync("eu-assets", R2Jurisdiction.FedrampHigh);
        Assert.Equal("fedramp-high", harness.LastRequest.Header("cf-r2-jurisdiction"));

        harness.RespondWithResult("{\"name\":\"eu-assets\"}");
        await harness.Client.R2.UpdateBucketAsync("eu-assets", R2StorageClass.InfrequentAccess, R2Jurisdiction.Us);
        Assert.Equal("us", harness.LastRequest.Header("cf-r2-jurisdiction"));
        Assert.Equal("{\"storageClass\":\"InfrequentAccess\"}", harness.LastRequest.Body);

        harness.RespondWithResult("null");
        await harness.Client.R2.DeleteBucketAsync("eu-assets", R2Jurisdiction.Default);
        Assert.Equal("default", harness.LastRequest.Header("cf-r2-jurisdiction"));
    }

    [Fact]
    public async Task The_unknown_jurisdiction_sentinel_is_never_sent_as_a_header()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"name\":\"assets\"}");

        await harness.Client.R2.GetBucketAsync("assets", R2Jurisdiction.Unknown);

        Assert.Null(harness.LastRequest.Header("cf-r2-jurisdiction"));
    }

    [Fact]
    public async Task UpdateBucketAsync_only_changes_the_storage_class()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"name\":\"assets\"}");

        await harness.Client.R2.UpdateBucketAsync("assets", R2StorageClass.InfrequentAccess);

        Assert.Equal(HttpMethod.Put, harness.LastRequest.Method);
        Assert.EndsWith("/r2/buckets/assets", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal("{\"storageClass\":\"InfrequentAccess\"}", harness.LastRequest.Body);
    }

    [Fact]
    public async Task DeleteBucketAsync_deletes()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("null");

        await harness.Client.R2.DeleteBucketAsync("assets");

        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.EndsWith("/r2/buckets/assets", harness.LastRequest.Path, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("assets", true)]
    [InlineData("my-bucket-2", true)]
    [InlineData("9lives", true)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("ab", false)]
    [InlineData("Assets", false)]
    [InlineData("-assets", false)]
    [InlineData("assets-", false)]
    [InlineData("my--bucket", false)]
    [InlineData("my_bucket", false)]
    [InlineData("my.bucket", false)]
    public async Task Bucket_names_are_validated_locally(string name, bool valid)
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"name\":\"whatever\"}");

        if (valid)
        {
            await harness.Client.R2.CreateBucketAsync(name);
            Assert.Equal(1, harness.RequestCount);
        }
        else
        {
            await Assert.ThrowsAsync<CloudflareConfigurationException>(() => harness.Client.R2.CreateBucketAsync(name));
            Assert.Equal(0, harness.RequestCount);
        }
    }

    [Fact]
    public async Task A_bucket_name_longer_than_63_characters_is_rejected()
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.R2.CreateBucketAsync(new string('a', 64)));
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task A_bucket_name_of_exactly_63_characters_is_accepted()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"name\":\"x\"}");

        await harness.Client.R2.CreateBucketAsync(new string('a', 63));

        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task A_cloudflare_name_conflict_is_surfaced_with_its_code()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.Conflict, 10052, "A bucket with this name already exists");

        var exception = await Assert.ThrowsAsync<CloudflareApiException>(() => harness.Client.R2.CreateBucketAsync("assets"));

        Assert.Equal(10052, exception.ErrorCode);
        Assert.False(exception.IsTransient);
    }
}
