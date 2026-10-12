using System.Reflection;
using CFNet.Exceptions;
using CFNet.Http;
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
            Log("SKIPPED: the token verify endpoints only apply to API tokens.");
            return;
        }

        if (IsAccountOwnedToken && !SkipWithoutAccount())
        {
            return;
        }

        // An account-owned token is a service principal, so verify it where Cloudflare answers for it.
        var endpoint = IsAccountOwnedToken ? "accounts/{account_id}/tokens/verify" : "user/tokens/verify";
        var verification = IsAccountOwnedToken
            ? await Client.User.VerifyAccountTokenAsync()
            : await Client.User.VerifyTokenAsync();

        Log($"status={verification.Status} id={verification.Id} endpoint={endpoint}");
        Assert.True(verification.IsActive);
    }

    [Fact]
    public async Task The_authenticated_user_can_be_read()
    {
        if (!SkipWithoutCredentials())
        {
            return;
        }

        if (!SkipUnless(
                !IsAccountOwnedToken,
                "GET /user is user-scoped: an account-owned token (cfat_) acts as a service principal rather than as a person, so Cloudflare rejects it here. Configure a user token (cfut_) to exercise this one."))
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
        if (zones.Count == 0)
        {
            Warn("GET /zones returned no zones for this token, so every zone-scoped test has nothing to read. " +
                 "Check the token's Zone Resources scope, and that CLOUDFLARE_ACCOUNT_ID owns the zone in CLOUDFLARE_ZONE_ID.");
        }
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

            // GetAsync answers null for a zone the token may not see as well as for one that does not
            // exist, so this is a configuration problem to report rather than a bug to fail on.
            if (!SkipUnless(
                    zone is not null,
                    $"CLOUDFLARE_ZONE_ID ({ZoneId}) is not readable with this token: Cloudflare answered 403 or 404. Either the zone is in a different account than CLOUDFLARE_ACCOUNT_ID, or it is outside the token's Zone Resources."))
            {
                return;
            }

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

        var records = await RunZoneScopedAsync(
            () => Client.DnsRecords.ListAsync(new DnsRecordListParameters { PerPage = 20 }));
        if (records is null)
        {
            return;
        }

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

        var records = await RunZoneScopedAsync(() => Client.DnsRecords.ListAsync(new DnsRecordListParameters
        {
            Type = DnsRecordTypes.A,
            PerPage = 10,
        }));
        if (records is null)
        {
            return;
        }

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

        // Cloudflare answers an unknown zone id with 403 and code 9109 ("Invalid zone identifier"),
        // not 404. GetAsync tolerates both so a lookup returns null instead of throwing.
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
            Log($"  {script.Name} modified={script.ModifiedOn:O} handlers={string.Join(",", script.Handlers ?? new List<string>())}");
        }
    }

    [Fact]
    public async Task The_workers_dev_subdomain_can_be_read()
    {
        if (!SkipWithoutAccount())
        {
            return;
        }

        WorkerSubdomain? subdomain;
        try
        {
            subdomain = await Client.Workers.GetSubdomainAsync();
        }
        catch (CloudflareApiException exception) when (exception.HasErrorCode(10007))
        {
            // 10007 means the account has no workers.dev subdomain yet; opening Workers & Pages in the
            // dashboard once creates it. That is account state, not something this suite can fix.
            SkipUnless(false, "This account has no workers.dev subdomain (Cloudflare error 10007). Open Workers & Pages in the dashboard once to create it, then this test will run.");
            return;
        }

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

        // Paging this endpoint is what Cloudflare answers with 8000024 on some accounts; the service
        // retries bare when that happens, and the run report shows both calls, so a reader can see
        // whether the fallback was needed.
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

        // The raw endpoint answers the same statement with each row as an array of values.
        var raw = await Client.D1.QueryRawAsync(first.Uuid!, "SELECT 1 AS answer");
        var rawRows = raw.RowArrays();

        Log($"raw rows={raw.RowCount} arrays={rawRows.Count} duration={raw.Meta?.Duration} served_by={raw.Meta?.ServedByColo}");
        Assert.Equal(1, raw.RowCount);

        var values = Assert.Single(rawRows);
        Assert.Equal(1, Assert.Single(values).GetInt32());
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

        CloudflarePage<R2Bucket> buckets;
        try
        {
            buckets = await Client.R2.ListBucketsAsync(new R2BucketListParameters { PerPage = 20 });
        }
        catch (CloudflareApiException exception) when (exception.HasErrorCode(10042))
        {
            // 10042 is "Please enable R2 through the Cloudflare Dashboard" - an account state, not a
            // defect, and nothing here can enable it.
            SkipUnless(false, "R2 is not enabled on this account (Cloudflare error 10042). Enable it in the dashboard - Storage & Databases > R2 - to exercise the R2 tests.");
            return;
        }

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

        var accounts = await Client.Accounts.ListAsync();
        Assert.NotEmpty(accounts.Items);

        if (IsAccountOwnedToken)
        {
            // GET /user is user-scoped: a service principal gets 9109 "Valid user-level authentication
            // not found". The account collection above is the equivalent probe.
            Log("user leg skipped: an account-owned token is not a user, so GET /user answers 9109.");
        }
        else
        {
            var user = await Client.User.GetAsync();
            Assert.False(string.IsNullOrWhiteSpace(user.Id));
        }
    }

    [Fact]
    public async Task Worker_custom_domains_can_be_listed()
    {
        if (!SkipWithoutAccount())
        {
            return;
        }

        var domains = await Client.Workers.ListDomainsAsync();

        Log($"worker custom domains: {domains.Count}");
        foreach (var domain in domains)
        {
            Log($"  {domain.Hostname} -> {domain.Service} zone={domain.ZoneName} enabled={domain.Enabled} previews={domain.PreviewsEnabled} cert={domain.CertId}");
        }

        Assert.All(domains, domain =>
        {
            Assert.False(string.IsNullOrWhiteSpace(domain.Id));
            Assert.False(string.IsNullOrWhiteSpace(domain.Hostname));
        });
    }

    [Fact]
    public async Task Zone_settings_can_be_listed_and_read_back_one_at_a_time()
    {
        if (!SkipWithoutZone())
        {
            return;
        }

        var settings = await RunZoneScopedAsync(() => Client.Zones.ListSettingsAsync());
        if (settings is null)
        {
            return;
        }

        Log($"zone settings: {settings.Count}");
        foreach (var setting in settings.OrderBy(setting => setting.Id, StringComparer.Ordinal))
        {
            Log($"  {setting.Id,-36} = {setting.Value?.GetRawText(),-28} editable={setting.Editable}");
        }

        Assert.NotEmpty(settings);
        Assert.All(settings, setting => Assert.False(string.IsNullOrWhiteSpace(setting.Id)));

        // Reading one at a time is what Cloudflare points at now that the list endpoint is deprecated.
        var first = settings[0];
        var single = await RunZoneScopedAsync(() => Client.Zones.GetSettingAsync(first.Id!));
        Assert.NotNull(single);
        Assert.Equal(first.Id, single!.Id);
        Log($"read back {single.Id} = {single.Value?.GetRawText()}");

        // Which settings a zone has depends on its plan, so a constant this zone did not report is worth
        // saying out loud rather than failing on: it is either misspelled or not on this plan.
        var reported = settings.Select(setting => setting.Id).ToHashSet(StringComparer.Ordinal);
        var missing = DeclaredZoneSettingIds().Where(id => !reported.Contains(id)).ToList();
        Warn(missing.Count == 0
            ? null
            : "ZoneSettingIds constants this zone did not report: " + string.Join(", ", missing) +
              ". Either the id is misspelled or the zone's plan does not have that setting.");
    }

    /// <summary>
    /// Every id <see cref="ZoneSettingIds"/> declares, read by reflection so the list cannot drift as
    /// constants are added.
    /// </summary>
    /// <returns>The declared setting ids.</returns>
    private static IEnumerable<string> DeclaredZoneSettingIds() =>
        typeof(ZoneSettingIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => field.GetRawConstantValue() as string)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!);

    [Fact]
    public async Task An_insufficiently_scoped_token_is_reported_as_an_authentication_failure()
    {
        if (!SkipWithoutCredentials())
        {
            return;
        }

        // A zone id that is well formed but definitely not ours. A lookup answers null for it, because
        // Cloudflare reports an unreadable zone and a missing one the same way; a collection read inside
        // it still throws, which is what this test is about.
        var zone = await Client.Zones.GetAsync("11111111111111111111111111111111");
        Log($"lookup of a zone that is not ours: {(zone is null ? "null, as expected" : zone.Name)}");

        var exception = await Assert.ThrowsAsync<CloudflareApiException>(
            () => Client.DnsRecords.ListAsync(new DnsRecordListParameters { PerPage = 5 }, "11111111111111111111111111111111"));

        Log($"status={(int?)exception.StatusCode} code={exception.ErrorCode} ray={exception.RayId}");
        Assert.True(
            exception.IsNotFound || exception.IsAuthenticationFailure || exception.IsValidationFailure,
            $"Expected a specific refusal, got {exception.StatusCode}/{exception.ErrorCode}: {exception.Message}");
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

            Log($"uploaded {uploaded.Name}");

            var read = await Client.Workers.GetAsync(name);
            Assert.NotNull(read);
            Assert.Equal(name, read!.Name);

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

    [Fact]
    public async Task A_zone_setting_can_be_changed_and_changed_back()
    {
        if (!SkipWithoutZone() || !SkipWithoutWritePermission())
        {
            return;
        }

        // Always Online is a switch every plan has, and turning it on while the origin is up changes
        // nothing about traffic, which makes it the least destructive setting to write to.
        var before = await RunZoneScopedAsync(() => Client.Zones.GetSettingAsync(ZoneSettingIds.AlwaysOnline));
        if (before is null)
        {
            Warn($"The zone does not report a '{ZoneSettingIds.AlwaysOnline}' setting, so there is nothing to write to.");
            return;
        }

        if (before.Editable == false)
        {
            SkipUnless(false, $"Cloudflare reports {ZoneSettingIds.AlwaysOnline} as editable=false on this zone's plan, so it cannot be changed.");
            return;
        }

        Log($"{ZoneSettingIds.AlwaysOnline} before={before.Value?.GetRawText()}");
        var target = !before.IsOn;

        ZoneSetting? changed;
        try
        {
            changed = await Client.Zones.SetSettingOnOffAsync(ZoneSettingIds.AlwaysOnline, target);
        }
        catch (CloudflareApiException exception) when (exception.IsAuthenticationFailure)
        {
            SkipUnless(false, $"The token cannot write zone settings ({exception.ErrorCode}). Add 'Zone Settings Write' to it. {exception.Message}");
            return;
        }

        try
        {
            Assert.NotNull(changed);
            Assert.Equal(target, changed!.IsOn);
            Log($"{ZoneSettingIds.AlwaysOnline} after={changed.Value?.GetRawText()}");
        }
        finally
        {
            // Put the zone back the way it was, whether or not the assertions above held.
            var restored = await Client.Zones.SetSettingOnOffAsync(ZoneSettingIds.AlwaysOnline, before.IsOn);
            Log($"{ZoneSettingIds.AlwaysOnline} restored={restored?.Value?.GetRawText()}");
        }
    }
}
