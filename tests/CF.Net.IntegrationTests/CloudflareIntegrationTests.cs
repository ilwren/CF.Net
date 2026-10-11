using CFNet.Exceptions;
using CFNet.Models.Common;
using CFNet.Models.D1;
using CFNet.Models.Dns;
using CFNet.Models.Kv;
using CFNet.Models.Pages;
using CFNet.Models.R2;
using CFNet.Models.Workers;
using CFNet.Models.Zones;
using CFNet.Services;
using Xunit;
using Xunit.Abstractions;

namespace CFNet.IntegrationTests;

/// <summary>
/// Read-only checks against the live API. They cost nothing beyond a handful of requests and prove
/// that the URLs, headers, envelopes and models line up with what Cloudflare actually returns.
/// </summary>
[Trait("Category", "Integration")]
public class ReadOnlyIntegrationTests : CloudflareIntegrationTest
{
    public ReadOnlyIntegrationTests(ITestOutputHelper output)
        : base(output)
    {
    }

    [Fact]
    public async Task The_configured_credentials_are_accepted()
    {
        if (!SkipWithoutCredentials())
        {
            return;
        }

        var check = await Client.CheckConnectionAsync();

        Log(check.Message);
        Assert.True(check.IsSuccess, $"Cloudflare rejected the configured credentials: {check.Message}");
        Assert.True(check.Elapsed < TimeSpan.FromSeconds(30), $"The check took {check.Elapsed}.");
    }

    [Fact]
    public async Task A_token_can_be_verified_directly()
    {
        if (!SkipWithoutCredentials())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ApiToken))
        {
            Log("SKIPPED: the /user/tokens/verify endpoint only applies to API tokens.");
            return;
        }

        var verification = await Client.User.VerifyTokenAsync();

        Log($"status={verification.Status} id={verification.Id}");
        Assert.True(verification.IsActive);
    }

    [Fact]
    public async Task The_authenticated_user_can_be_read()
    {
        if (!SkipWithoutCredentials())
        {
            return;
        }

        var user = await Client.User.GetAsync();

        Assert.False(string.IsNullOrWhiteSpace(user.Id));
        Log($"user id={user.Id} email={user.Email}");
    }

    [Fact]
    public async Task Accounts_can_be_listed()
    {
        if (!SkipWithoutCredentials())
        {
            return;
        }

        var accounts = await Client.Accounts.ListAsync(new AccountListParameters { PerPage = 20 });

        Log($"accounts: {accounts.Count} of {accounts.TotalCount}");
        foreach (var account in accounts.Items)
        {
            Log($"  {account.Id} {account.Name} ({account.Type})");
        }

        Assert.NotEmpty(accounts.Items);

        if (!string.IsNullOrWhiteSpace(AccountId))
        {
            Assert.Contains(accounts.Items, account => account.Id == AccountId);
        }
    }

    [Fact]
    public async Task Zones_can_be_listed_and_read()
    {
        if (!SkipWithoutCredentials())
        {
            return;
        }

        var zones = await Client.Zones.ListAsync(new ZoneListParameters { PerPage = 20 });

        Log($"zones: {zones.Count} of {zones.TotalCount}");
        foreach (var zone in zones.Items)
        {
            Log($"  {zone.Id} {zone.Name} [{zone.Status}] type={zone.Type} active={zone.IsActive}");
            Assert.False(string.IsNullOrWhiteSpace(zone.Id));
            Assert.False(string.IsNullOrWhiteSpace(zone.Name));
            Assert.False(string.IsNullOrWhiteSpace(zone.Status));
        }

        if (!string.IsNullOrWhiteSpace(ZoneId))
        {
            var zone = await Client.Zones.GetAsync(ZoneId);

            Assert.NotNull(zone);
            Assert.Equal(ZoneId, zone!.Id);
            Log($"zone {ZoneId} -> {zone.Name} plan={zone.Plan?.Name} nameservers={string.Join(",", zone.NameServers ?? new List<string>())}");
        }
    }

    [Fact]
    public async Task Every_zone_page_can_be_walked()
    {
        if (!SkipWithoutCredentials())
        {
            return;
        }

        var count = 0;
        await foreach (var zone in Client.Zones.ListAllAsync(new ZoneListParameters { PerPage = 5 }))
        {
            count++;
            Log($"  walked {zone.Name}");

            if (count >= 25)
            {
                break;
            }
        }

        Log($"walked {count} zone(s)");
    }

    [Fact]
    public async Task Dns_records_can_be_listed()
    {
        if (!SkipWithoutZone())
        {
            return;
        }

        var records = await Client.DnsRecords.ListAsync(new DnsRecordListParameters { PerPage = 20 });

        Log($"dns records: {records.Count} of {records.TotalCount}");
        foreach (var record in records.Items)
        {
            Log($"  {record.Type,-6} {record.Name,-40} -> {record.Content ?? "(structured)"} proxied={record.Proxied}");
            Assert.False(string.IsNullOrWhiteSpace(record.Id));
            Assert.False(string.IsNullOrWhiteSpace(record.Type));
            Assert.False(string.IsNullOrWhiteSpace(record.Name));
        }
    }

    [Fact]
    public async Task Dns_records_can_be_filtered_by_type()
    {
        if (!SkipWithoutZone())
        {
            return;
        }

        var records = await Client.DnsRecords.ListAsync(new DnsRecordListParameters
        {
            Type = DnsRecordTypes.A,
            PerPage = 10,
        });

        Log($"A records: {records.Count}");
        Assert.All(records.Items, record => Assert.Equal(DnsRecordTypes.A, record.Type));
    }

    [Fact]
    public async Task An_unknown_zone_reports_not_found_rather_than_throwing_a_generic_error()
    {
        if (!SkipWithoutCredentials())
        {
            return;
        }

        var zone = await Client.Zones.GetAsync("00000000000000000000000000000000");

        Assert.Null(zone);
    }

    [Fact]
    public async Task An_unknown_dns_record_reports_not_found()
    {
        if (!SkipWithoutZone())
        {
            return;
        }

        var record = await Client.DnsRecords.GetAsync("00000000000000000000000000000000");

        Assert.Null(record);
    }

    [Fact]
    public async Task Worker_scripts_can_be_listed()
    {
        if (!SkipWithoutAccount())
        {
            return;
        }

        var scripts = await Client.Workers.ListAsync(new WorkerScriptListParameters { PerPage = 20 });

        Log($"worker scripts: {scripts.Count}");
        foreach (var script in scripts.Items)
        {
            Log($"  {script.ScriptName} modified={script.ModifiedOn:O} handlers={string.Join(",", script.Handlers ?? new List<string>())}");
        }
    }

    [Fact]
    public async Task The_workers_dev_subdomain_can_be_read()
    {
        if (!SkipWithoutAccount())
        {
            return;
        }

        var subdomain = await Client.Workers.GetSubdomainAsync();

        Log($"subdomain={subdomain?.Subdomain} enabled={subdomain?.Enabled}");
        Assert.NotNull(subdomain);
    }

    [Fact]
    public async Task Pages_projects_can_be_listed()
    {
        if (!SkipWithoutAccount())
        {
            return;
        }

        var projects = await Client.Pages.ListAsync(new PagesProjectListParameters { PerPage = 20 });

        Log($"pages projects: {projects.Count} of {projects.TotalCount}");
        foreach (var project in projects.Items)
        {
            Log($"  {project.Name} branch={project.ProductionBranch} subdomain={project.Subdomain} latest={project.LatestDeployment?.Status}");
        }
    }

    [Fact]
    public async Task D1_databases_can_be_listed_and_queried()
    {
        if (!SkipWithoutAccount())
        {
            return;
        }

        var databases = await Client.D1.ListAsync(new D1DatabaseListParameters { PerPage = 20 });

        Log($"d1 databases: {databases.Count} of {databases.TotalCount}");
        foreach (var database in databases.Items)
        {
            Log($"  {database.Uuid} {database.Name} tables={database.NumTables} size={database.FileSize} jurisdiction={database.Jurisdiction}");
        }

        if (databases.Items.Count == 0)
        {
            Log("SKIPPED query: the account has no D1 database.");
            return;
        }

        var first = databases.Items[0];
        var result = await Client.D1.QueryAsync(first.Uuid!, "SELECT 1 AS answer");

        Log($"query rows={result.RowCount} duration={result.Meta?.Duration} served_by={result.Meta?.ServedByColo}");
        Assert.Equal(1, result.RowCount);
        Assert.Equal(1, result.FirstColumn<int>("answer"));
    }

    [Fact]
    public async Task Kv_namespaces_can_be_listed()
    {
        if (!SkipWithoutAccount())
        {
            return;
        }

        var namespaces = await Client.Kv.ListNamespacesAsync(new KvNamespaceListParameters { PerPage = 20 });

        Log($"kv namespaces: {namespaces.Count} of {namespaces.TotalCount}");
        foreach (var ns in namespaces.Items)
        {
            Log($"  {ns.Id} {ns.Title} url-encoded-keys={ns.SupportsUrlEncoding}");
        }
    }

    [Fact]
    public async Task R2_buckets_can_be_listed()
    {
        if (!SkipWithoutAccount())
        {
            return;
        }

        var buckets = await Client.R2.ListBucketsAsync(new R2BucketListParameters { PerPage = 20 });

        Log($"r2 buckets: {buckets.Count}");
        foreach (var bucket in buckets.Items)
        {
            Log($"  {bucket.Name} location={bucket.Location} class={bucket.StorageClass} jurisdiction={bucket.Jurisdiction} created={bucket.CreationDate:O}");
        }
    }

    [Fact]
    public async Task Every_product_area_is_reachable_through_the_client_facade()
    {
        if (!SkipWithoutCredentials())
        {
            return;
        }

        // This is the smoke test for the facade: a single client, nine services, all wired to the
        // same transport and options instance.
        Assert.NotNull(Client.User);
        Assert.NotNull(Client.Accounts);
        Assert.NotNull(Client.Zones);
        Assert.NotNull(Client.DnsRecords);
        Assert.NotNull(Client.Workers);
        Assert.NotNull(Client.Pages);
        Assert.NotNull(Client.D1);
        Assert.NotNull(Client.Kv);
        Assert.NotNull(Client.R2);

        Assert.False(string.IsNullOrWhiteSpace(Client.Options.UserAgent));
        Assert.True(Client.Options.HasCredentials);

        var user = await Client.User.GetAsync();
        var accounts = await Client.Accounts.ListAsync();

        Assert.False(string.IsNullOrWhiteSpace(user.Id));
        Assert.NotEmpty(accounts.Items);
    }

    [Fact]
    public async Task An_insufficiently_scoped_token_is_reported_as_an_authentication_failure()
    {
        if (!SkipWithoutCredentials())
        {
            return;
        }

        // A zone id that is a well formed but definitely not ours: Cloudflare answers 403/404 rather
        // than leaking whether it exists.
        try
        {
            var zone = await Client.Zones.GetAsync("11111111111111111111111111111111");
            Log($"unexpectedly resolved to {zone?.Name}");
        }
        catch (CloudflareApiException exception)
        {
            Log($"status={(int?)exception.StatusCode} code={exception.ErrorCode} ray={exception.RayId}");
            Assert.True(exception.IsNotFound || exception.IsAuthenticationFailure || exception.IsValidationFailure);
        }
    }
}

/// <summary>
/// Lifecycle tests that create and then remove a resource. They are self-cleaning, but they do write
/// to the account, so they are additionally gated behind <c>CLOUDFLARE_IT_ENABLE_WRITES=true</c>.
/// </summary>
[Trait("Category", "Integration")]
public class WriteIntegrationTests : CloudflareIntegrationTest
{
    public WriteIntegrationTests(ITestOutputHelper output)
        : base(output)
    {
    }

    [Fact]
    public async Task A_txt_record_can_be_created_read_updated_and_deleted()
    {
        if (!SkipWithoutZone() || !SkipWithoutWritePermission())
        {
            return;
        }

        // The record has to live inside the configured zone, so derive its name from the zone's.
        var zone = await Client.Zones.GetAsync(ZoneId);
        Assert.NotNull(zone);

        var fullName = $"{UniqueSuffix("cfnet-it")}.{zone!.Name}";
        DnsRecord? created = null;
        try
        {
            created = await Client.DnsRecords.CreateTxtRecordAsync(fullName, "cf-net integration test", ttl: 120);

            Log($"created {created.Id} {created.Name}");
            Assert.False(string.IsNullOrWhiteSpace(created.Id));
            Assert.Equal(DnsRecordTypes.Txt, created.Type);
            Assert.Equal(fullName, created.Name);
            Assert.False(created.Proxied);

            var read = await Client.DnsRecords.GetAsync(created.Id!, ZoneId);
            Assert.NotNull(read);
            Assert.Equal(created.Id, read!.Id);

            var found = await Client.DnsRecords.FindAsync(fullName, DnsRecordTypes.Txt, ZoneId);
            Assert.Contains(found, record => record.Id == created.Id);

            var updated = await Client.DnsRecords.UpdateAsync(created.Id!, new DnsRecord { Comment = "updated by cf-net" }, ZoneId);
            Log($"updated comment={updated.Comment}");

            var replaced = await Client.DnsRecords.ReplaceAsync(created.Id!, new DnsRecord
            {
                Type = DnsRecordTypes.Txt,
                Name = fullName,
                Content = "cf-net integration test (replaced)",
                Ttl = 120,
                Proxied = false,
            }, ZoneId);
            Assert.Equal(DnsRecordTypes.Txt, replaced.Type);
            Assert.Contains("replaced", replaced.Content, StringComparison.Ordinal);
        }
        finally
        {
            if (created?.Id is { } id)
            {
                await Client.DnsRecords.DeleteAsync(id, ZoneId);
                Log($"deleted {id}");
            }
        }

        // DNS propagation can lag, so the absence of the record is asserted in the list instead.
        var remaining = await Client.DnsRecords.FindAsync(fullName, DnsRecordTypes.Txt, ZoneId);
        Log($"records still matching {fullName}: {remaining.Count}");
    }

    [Fact]
    public async Task A_kv_namespace_and_value_can_be_created_and_removed()
    {
        if (!SkipWithoutAccount() || !SkipWithoutWritePermission())
        {
            return;
        }

        var title = UniqueSuffix("cfnet-it");
        var namespaceId = string.Empty;

        try
        {
            var created = await Client.Kv.CreateNamespaceAsync(title);
            namespaceId = created.Id!;
            Log($"namespace {namespaceId} ({created.Title})");

            // Cloudflare needs a moment before a fresh namespace accepts writes.
            await Task.Delay(TimeSpan.FromSeconds(2));

            await Client.Kv.PutStringAsync(namespaceId, "greeting/hello", "hi there", expirationTtl: 300);
            var value = await Client.Kv.GetValueAsync(namespaceId, "greeting/hello");

            Assert.True(value.Found);
            Assert.Equal("hi there", value.Text);

            var keys = await Client.Kv.ListKeysAsync(namespaceId, new KvKeyListParameters { Prefix = "greeting/", Limit = 10 });
            Log($"keys: {string.Join(", ", keys.Items.Select(key => key.Name))}");
            Assert.Contains(keys.Items, key => key.Name == "greeting/hello");

            await Client.Kv.BulkWriteAsync(namespaceId, new[]
            {
                KvBulkWriteOperation.Put("bulk/a", "1"),
                KvBulkWriteOperation.Put("bulk/b", "2"),
            });

            var renamed = await Client.Kv.RenameNamespaceAsync(namespaceId, $"{title}-renamed");
            Assert.Equal($"{title}-renamed", renamed.Title);
        }
        finally
        {
            if (!string.IsNullOrEmpty(namespaceId))
            {
                await Client.Kv.DeleteNamespaceAsync(namespaceId);
                Log($"deleted namespace {namespaceId}");
            }
        }

        Assert.Null(await Client.Kv.GetNamespaceAsync(namespaceId));
    }

    [Fact]
    public async Task An_r2_bucket_can_be_created_and_removed()
    {
        if (!SkipWithoutAccount() || !SkipWithoutWritePermission())
        {
            return;
        }

        // R2 names allow only lowercase letters, digits and hyphens.
        var name = $"cfnet-it-{DateTime.UtcNow:yyyyMMddhhmmss}-{Guid.NewGuid().ToString("N")[..6]}";

        try
        {
            var created = await Client.R2.CreateBucketAsync(name, locationHint: R2LocationHint.Wnam);
            Log($"bucket {created.Name} location={created.Location} class={created.StorageClass}");
            Assert.Equal(name, created.Name);

            var read = await Client.R2.GetBucketAsync(name);
            Assert.NotNull(read);
            Assert.Equal(name, read!.Name);
        }
        finally
        {
            try
            {
                await Client.R2.DeleteBucketAsync(name);
                Log($"deleted bucket {name}");
            }
            catch (CloudflareApiException exception) when (exception.IsNotFound)
            {
                Log("bucket was already gone");
            }
        }
    }

    [Fact]
    public async Task A_d1_database_can_be_created_queried_and_removed()
    {
        if (!SkipWithoutAccount() || !SkipWithoutWritePermission())
        {
            return;
        }

        var name = $"cfnet-it-{Guid.NewGuid().ToString("N")[..10]}";
        var databaseId = string.Empty;

        try
        {
            var created = await Client.D1.CreateAsync(name);
            databaseId = created.Uuid!;
            Log($"database {databaseId} ({created.Name})");

            await Client.D1.ExecuteAsync(databaseId, D1QueryRequest.BatchOf(new[]
            {
                new D1Query("CREATE TABLE items (id INTEGER PRIMARY KEY, label TEXT)"),
                new D1Query("INSERT INTO items (label) VALUES (?), (?)", new object?[] { "alpha", "beta" }),
            }));

            var count = await Client.D1.QueryAsync(databaseId, "SELECT COUNT(*) AS total FROM items");
            Log($"rows={count.FirstColumn<int>("total")}");
            Assert.Equal(2, count.FirstColumn<int>("total"));

            var found = await Client.D1.GetByNameAsync(name);
            Assert.NotNull(found);
            Assert.Equal(databaseId, found!.Uuid);
        }
        finally
        {
            if (!string.IsNullOrEmpty(databaseId))
            {
                await Client.D1.DeleteAsync(databaseId);
                Log($"deleted database {databaseId}");
            }
        }
    }

    [Fact]
    public async Task A_worker_script_can_be_uploaded_and_removed()
    {
        if (!SkipWithoutAccount() || !SkipWithoutWritePermission())
        {
            return;
        }

        var name = $"cfnet-it-{Guid.NewGuid().ToString("N")[..10]}";

        try
        {
            var uploaded = await Client.Workers.UploadAsync(
                name,
                "export default { async fetch(request) { return new Response('cf-net'); } }",
                "2025-01-01",
                new[] { WorkerBinding.PlainText("GREETING", "hello") });

            Log($"uploaded {uploaded.ScriptName}");

            var read = await Client.Workers.GetAsync(name);
            Assert.NotNull(read);
            Assert.Equal(name, read!.ScriptName);

            var content = await Client.Workers.GetContentAsync(name);
            Assert.NotNull(content);
            Log($"main module={content!.MainModule} modules={content.Modules?.Count}");

            var settings = await Client.Workers.GetSettingsAsync(name);
            Log($"logpush={settings?.Logpush} tags={string.Join(",", settings?.Tags ?? new List<string>())}");
        }
        finally
        {
            try
            {
                await Client.Workers.DeleteAsync(name);
                Log($"deleted worker {name}");
            }
            catch (CloudflareApiException exception) when (exception.IsNotFound)
            {
                Log("worker was already gone");
            }
        }
    }

    [Fact]
    public async Task A_pages_project_can_be_created_and_removed()
    {
        if (!SkipWithoutAccount() || !SkipWithoutWritePermission())
        {
            return;
        }

        var name = $"cfnet-it-{Guid.NewGuid().ToString("N")[..10]}";

        try
        {
            var created = await Client.Pages.CreateAsync(name, "main");
            Log($"project {created.Name} ({created.Id})");
            Assert.Equal(name, created.Name);
            Assert.Equal("main", created.ProductionBranch);

            var read = await Client.Pages.GetAsync(name);
            Assert.NotNull(read);
            Assert.Equal(created.Id, read!.Id);
        }
        finally
        {
            try
            {
                await Client.Pages.DeleteAsync(name);
                Log($"deleted project {name}");
            }
            catch (CloudflareApiException exception) when (exception.IsNotFound)
            {
                Log("project was already gone");
            }
        }
    }

    [Fact]
    public async Task The_cache_can_be_purged_by_url()
    {
        if (!SkipWithoutZone() || !SkipWithoutWritePermission())
        {
            return;
        }

        var zone = await Client.Zones.GetAsync(ZoneId);
        Assert.NotNull(zone);

        var result = await Client.Zones.PurgeCacheAsync(
            PurgeCacheRequest.ForFiles(new[] { $"https://{zone!.Name}/cf-net-integration-probe" }));

        Log($"purge_time={result.PurgeTime}");
    }
}
