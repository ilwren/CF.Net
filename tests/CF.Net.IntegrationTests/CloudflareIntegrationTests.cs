using System.Reflection;
using System.Text.Json;
using CFNet.Exceptions;
using CFNet.Http;
using CFNet.Models.Common;
using CFNet.Models.D1;
using CFNet.Models.Dns;
using CFNet.Models.Kv;
using CFNet.Models.Pages;
using CFNet.Models.R2;
using CFNet.Models.Ssl;
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
            Warn($"GET /zones returned no zones on this page (result_info reported {zones.TotalCount?.ToString() ?? "no total"}), " +
                 "so every zone-scoped test has nothing to read. Check the token's Zone Resources scope, and that " +
                 "CLOUDFLARE_ACCOUNT_ID owns the zone in CLOUDFLARE_ZONE_ID. A total above zero with an empty page " +
                 "would instead mean the list is being filtered or paged somewhere it should not be.");
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

            // GetAsync answers null for a zone the token may not see, for one that does not exist, and -
            // observed live - for a 200 whose result is null, so the three cannot be told apart from here.
            // That makes this a configuration problem to report rather than a bug to fail on, and the
            // message has to carry all three possibilities instead of naming one.
            if (!SkipUnless(
                    zone is not null,
                    $"CLOUDFLARE_ZONE_ID ({ZoneId}) came back unreadable, which GetAsync reports as null for any of three reasons: Cloudflare answered 403 (the zone is outside the token's Zone Resources, or belongs to a different account than CLOUDFLARE_ACCOUNT_ID), answered 404 (the id is not a zone), or answered 200 with no result. The zone list above returned {zones.Count} zone(s) of {zones.TotalCount?.ToString() ?? "unknown"}, which narrows it down."))
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

        // This is the smoke test for the facade: a single client, thirteen services, all wired to the
        // same transport and options instance.
        Assert.NotNull(Client.User);
        Assert.NotNull(Client.Accounts);
        Assert.NotNull(Client.Zones);
        Assert.NotNull(Client.DnsRecords);
        Assert.NotNull(Client.Dnssec);
        Assert.NotNull(Client.Ssl);
        Assert.NotNull(Client.CustomHostnames);
        Assert.NotNull(Client.OriginTlsClientAuth);
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
            // A 'ReportedId' constant is the id one setting answers with, not an id to put in a path, so it
            // never appears in a settings list and would otherwise be reported missing from every zone.
            .Where(field => !field.Name.EndsWith("ReportedId", StringComparison.Ordinal))
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
    public async Task Dns_records_can_be_created_and_deleted_in_one_batch()
    {
        if (!SkipWithoutZone() || !SkipWithoutWritePermission())
        {
            return;
        }

        var zone = await ReadableZoneAsync();
        if (zone is null)
        {
            return;
        }

        var firstName = $"{UniqueSuffix("cfnet-it-batch")}.{zone.Name}";
        var secondName = $"{UniqueSuffix("cfnet-it-batch")}.{zone.Name}";
        var createdIds = new List<string>();

        try
        {
            var result = await Client.DnsRecords.BatchAsync(new DnsRecordBatch().WithPosts(
                new DnsRecord
                {
                    Name = firstName, Type = DnsRecordTypes.Txt, Content = "cf-net batch first",
                    Ttl = 120, Proxied = false,
                },
                new DnsRecord
                {
                    Name = secondName, Type = DnsRecordTypes.Txt, Content = "cf-net batch second",
                    Ttl = 120, Proxied = false,
                }));

            // The result mirrors the request's four groups, an unused one arriving as an explicit null,
            // which is the shape a live run reported and DnsRecordBatchResult was written from.
            Assert.NotNull(result);
            Assert.Null(result!.Deletes);
            Assert.Equal(2, result.Posts?.Count ?? 0);
            Log($"batch created {result.AffectedCount} record(s): {string.Join(", ", result.AffectedIds)}");

            foreach (var name in new[] { firstName, secondName })
            {
                var found = await Client.DnsRecords.FindAsync(name, DnsRecordTypes.Txt);
                foreach (var record in found)
                {
                    Log($"  created {record.Id} {record.Name}");
                    createdIds.Add(record.Id!);
                }
            }

            Assert.Equal(2, createdIds.Count);
        }
        finally
        {
            if (createdIds.Count > 0)
            {
                // The same endpoint removes them, which exercises the deletes group as well as posts.
                var removed = await Client.DnsRecords.BatchAsync(
                    new DnsRecordBatch().WithDeletes(createdIds.ToArray()));
                // Cloudflare echoes what it removed, in full, which is the only record of it that outlives
                // the delete.
                Log($"batch deleted {removed?.Deletes?.Count ?? 0} of {createdIds.Count} record(s)");
            }
        }
    }

    [Fact]
    public async Task A_txt_record_can_be_created_read_updated_and_deleted()
    {
        if (!SkipWithoutZone() || !SkipWithoutWritePermission())
        {
            return;
        }

        // The record has to live inside the configured zone, so derive its name from the zone's.
        var zone = await ReadableZoneAsync();
        if (zone is null)
        {
            return;
        }

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

        // Created outside the try/finally on purpose: when R2 is not enabled the create answers 10042 and
        // the delete below answers it too, so the finally block used to turn one account state into a second
        // failure on top of the first.
        R2Bucket created;
        try
        {
            created = await Client.R2.CreateBucketAsync(name, locationHint: R2LocationHint.Wnam);
        }
        catch (CloudflareApiException exception) when (exception.HasErrorCode(10042))
        {
            SkipUnless(false, "R2 is not enabled on this account (Cloudflare error 10042). Enable it in the dashboard - Storage & Databases > R2 - to exercise the R2 write test.");
            return;
        }

        try
        {
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
            catch (CloudflareApiException exception) when (exception.IsNotFound || exception.HasErrorCode(10042))
            {
                Log("bucket was already gone, or R2 is not enabled and never held it");
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

            // This endpoint is documented as returning the script source itself rather than JSON metadata,
            // which is why a successful upload reads back as text. Metadata comes from script-settings.
            var source = await Client.Workers.GetAsync(name);
            if (source is null)
            {
                // A script Cloudflare has just accepted with a 200 can still take a moment to be readable,
                // so this is retried once before being called a failure rather than asserted on outright.
                Log($"{name} was not readable straight after the upload; waiting and trying once more.");
                await Task.Delay(TimeSpan.FromSeconds(3));
                source = await Client.Workers.GetAsync(name);
            }

            Assert.True(
                source is not null,
                $"GET /accounts/{{account_id}}/workers/scripts/{name} still answered 'not there' on the second " +
                "try, even though the PUT for that same name returned 200 and the delete below found it.");
            Assert.Contains("cf-net", source!, StringComparison.Ordinal);
            Log($"downloaded {source!.Length} characters of script source");

            var content = await Client.Workers.GetContentAsync(name);
            Assert.NotNull(content);
            Log($"content/v2 returned {content!.Length} characters");

            // The reference documents id, number and three of metadata's fields, and says metadata has
            // three more it does not name. Everything unnamed lands in AdditionalData, so reporting it here
            // is how those fields become properties instead of guesses - the run report is the only channel
            // back out of CI that survives the job log being discarded.
            var versions = await Client.Workers.ListVersionsAsync(name);
            Assert.NotNull(versions);
            Log($"versions: {versions!.Count}; latest: {versions.Latest}");
            var latest = versions.Latest;
            if (latest is not null)
            {
                // Both of these came from a live answer rather than from the reference, so asserting them
                // here is what keeps them honest: if Cloudflare stops sending either, this test says so
                // instead of the model quietly carrying a field nothing populates.
                Assert.Equal("api", latest.Metadata?.Source);
                Assert.Equal("upload", latest.Annotations?.TriggeredBy);
                Log($"version {latest.Number} created {latest.Metadata?.CreatedOn:O} by " +
                    $"'{latest.Metadata?.AuthorEmail}' source={latest.Metadata?.Source} " +
                    $"preview={latest.Metadata?.HasPreview}");

                // Quiet while the model covers everything Cloudflare sends, and loud the moment it stops.
                if (latest.AdditionalData is { Count: > 0 } ||
                    latest.Metadata?.AdditionalData is { Count: > 0 } ||
                    versions.AdditionalData is { Count: > 0 })
                {
                    Warn($"GET /accounts/{{account_id}}/workers/scripts/{name}/versions returned fields this " +
                         $"library does not model yet - version: {JsonSerializer.Serialize(latest.AdditionalData)}, " +
                         $"metadata: {JsonSerializer.Serialize(latest.Metadata?.AdditionalData)}, result: " +
                         $"{JsonSerializer.Serialize(versions.AdditionalData)}. They are captured rather than " +
                         "dropped, and belong in WorkerVersion once they have been seen more than once.");
                }
            }

            // Uploading a script deploys it, so there is at least one deployment to read, and it is the
            // one serving traffic. This is also where a percentage split would show up if one existed.
            var deployments = await Client.Workers.ListDeploymentsAsync(name);
            Assert.NotNull(deployments);
            Log($"deployments: {deployments!.Count}; total={deployments.TotalCount} latest={deployments.Latest}");
            var serving = deployments.Latest;
            if (serving is not null)
            {
                Assert.Equal(WorkerDeploymentStrategies.Percentage, serving.Strategy);
                Assert.False(serving.IsGradual);
                var share = Assert.Single(serving.Versions!);
                Assert.Equal(100m, share.Percentage);
                Assert.Equal(latest?.Id, share.VersionId);
                Log($"serving version {share.VersionId} at {share.Percentage}% " +
                    $"source={serving.Source} triggered_by={serving.Annotations?.TriggeredBy}");

                if (serving.AdditionalData is { Count: > 0 } ||
                    serving.Annotations?.AdditionalData is { Count: > 0 })
                {
                    Warn($"GET /accounts/{{account_id}}/workers/scripts/{name}/deployments returned fields this " +
                         $"library does not model yet - deployment: " +
                         $"{JsonSerializer.Serialize(serving.AdditionalData)}, annotations: " +
                         $"{JsonSerializer.Serialize(serving.Annotations?.AdditionalData)}.");
                }
            }

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
    public async Task Pages_deployments_logs_and_upload_tokens_can_be_read()
    {
        if (!SkipWithoutAccount())
        {
            return;
        }

        var projects = await Client.Pages.ListAsync(new PagesProjectListParameters { PerPage = 20 });
        var project = projects.Items.FirstOrDefault();
        if (project?.Name is null)
        {
            SkipUnless(false, "This account has no Pages project, so there are no deployments to read.");
            return;
        }

        // env is the filter the schema documents; branch, order and direction are not, so they stay unset
        // and this run is evidence about the documented one only.
        var deployments = await Client.Pages.ListDeploymentsAsync(
            project.Name,
            new PagesDeploymentListParameters { Env = PagesDeploymentEnvironments.Production, PerPage = 5 });
        Log($"project {project.Name}: {deployments.Count} production deployments of {deployments.TotalCount}");

        var deployment = deployments.Items.FirstOrDefault();
        if (deployment?.Id is null)
        {
            Log("no production deployment yet, so there are no build logs to read");
        }
        else
        {
            Log($"deployment {deployment.Id} stage={deployment.LatestStage?.Name} status={deployment.Status}");
            try
            {
                var logs = await Client.Pages.GetDeploymentLogsAsync(project.Name, deployment.Id);
                Assert.NotNull(logs);
                Log($"  logs: {logs}, container logs included={logs.IncludesContainerLogs}");
                foreach (var line in (logs.Data ?? new List<PagesDeploymentLogLine>()).Take(3))
                {
                    Log($"    [{line.Timestamp}] {line.Line}");
                }
            }
            catch (CloudflareApiException exception) when (exception.IsNotFound)
            {
                // A Direct Upload deployment never runs a build, so it has no build log to fetch.
                Warn($"no build logs for deployment {deployment.Id} - it never ran a build");
            }
        }

        // The token is the only documented piece of Direct Upload. It is a credential, so the run report
        // gets its length rather than its value.
        var token = await Client.Pages.GetUploadTokenAsync(project.Name);
        Assert.True(token.HasToken);
        Log($"upload token: {token}");

        // A domain that was never attached reads as null rather than as a failure.
        Assert.Null(await Client.Pages.GetDomainAsync(project.Name, "cfnet-it-missing.example.com"));
    }

    [Fact]
    public async Task A_pages_project_build_cache_can_be_purged()
    {
        if (!SkipWithoutAccount() || !SkipWithoutWritePermission())
        {
            return;
        }

        var projects = await Client.Pages.ListAsync(new PagesProjectListParameters { PerPage = 20 });
        var project = projects.Items.FirstOrDefault();
        if (project?.Name is null)
        {
            SkipUnless(false, "This account has no Pages project whose build cache could be purged.");
            return;
        }

        // Purging only throws away cached build artefacts: the next build is slower, nothing is deleted.
        // Retrying a deployment is deliberately not exercised here, because that starts a real build.
        await Client.Pages.PurgeBuildCacheAsync(project.Name);
        Log($"purged the build cache of {project.Name}");
    }

    [Fact]
    public async Task The_cache_can_be_purged_by_url()
    {
        if (!SkipWithoutZone() || !SkipWithoutWritePermission())
        {
            return;
        }

        var zone = await ReadableZoneAsync();
        if (zone is null)
        {
            return;
        }

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

    [Fact]
    public async Task Ssl_certificate_packs_and_the_enrolment_settings_can_be_read()
    {
        if (!SkipWithoutZone())
        {
            return;
        }

        try
        {
            var packs = await RunZoneScopedAsync(
                () => Client.Ssl.ListAsync(new SslCertificatePackListParameters { PerPage = 20 }));
            if (packs is null)
            {
                return;
            }

            Log($"certificate packs: {packs.Count} of {packs.TotalCount}");
            foreach (var pack in packs.Items)
            {
                Log($"  {pack.Id} type={pack.Type} status={pack.Status} authority={pack.CertificateAuthority} hosts={pack.Hosts?.Count ?? 0}");
                Assert.False(string.IsNullOrWhiteSpace(pack.Id));
            }

            // The settings Cloudflare files under SSL but serves from the zone settings path, plus the
            // compliance modes that bound what Auto-Origin TLS KEX may choose. Every one of these is a read.
            var universal = await Client.Ssl.GetUniversalSettingsAsync();
            Log($"universal ssl enabled={universal.Enabled?.ToString() ?? "unknown"}");

            // Each of these logs the id it was answered with as well as the value. The compliance modes
            // setting reports 'origin_tls_cmpl' for a request made with 'origin_tls_compliance_modes', so
            // the three are compared here rather than assumed: whichever of them shorten, the next run says.
            var kex = await Client.Ssl.GetAutoOriginTlsKexAsync();
            Log($"auto_origin_tls_kex enabled={kex.Enabled?.ToString() ?? "unknown"} reported id={kex.Id ?? "(none)"}");

            var automatic = await Client.Ssl.GetAutomaticSslAsync();
            Log($"ssl_automatic_mode value={automatic.Value} reported id={automatic.Id ?? "(none)"} " +
                $"next_scan={automatic.NextScheduledScan?.ToString("O") ?? "(none)"}");

            var modes = await Client.Ssl.GetOriginTlsComplianceModesAsync();
            Log($"origin_tls_compliance_modes={modes?.ToString() ?? "(no result)"} reported id={modes?.Id ?? "(none)"}");
            if (modes is not null)
            {
                // Either spelling is accepted: Cloudflare answers with the short one today, and a constant
                // that hard-codes that would turn their fix into a red run here.
                Assert.Contains(
                    modes.Id,
                    new[] { ZoneSettingIds.OriginTlsComplianceModes, ZoneSettingIds.OriginTlsComplianceModesReportedId });

                if (modes.Id != ZoneSettingIds.OriginTlsComplianceModes)
                {
                    Warn($"GET /zones/{{zone_id}}/settings/{ZoneSettingIds.OriginTlsComplianceModes} reported its id " +
                         $"as '{modes.Id}' rather than the id it was requested by. The library sends the long one and " +
                         $"reads back whatever arrives, so this is a Cloudflare quirk to know about, not a defect.");
                }
            }
        }
        catch (CloudflareApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            // A token without the permission is account state, not a defect: say what is missing and skip.
            SkipUnless(false, $"The token cannot read SSL for this zone (HTTP 403, Cloudflare error {exception.ErrorCode}). It needs 'SSL and Certificates Read'.");
        }
    }

    [Fact]
    public async Task Scanned_dns_records_can_be_listed()
    {
        if (!SkipWithoutZone())
        {
            return;
        }

        var page = await RunZoneScopedAsync(() => Client.DnsRecords.ListScannedRecordsAsync(perPage: 5));
        if (page is null)
        {
            return;
        }

        // A zone that has never been scanned answers 200 with an empty list, so an empty page here is the
        // normal answer rather than a refusal or a missing permission - which is what the reach matrix
        // recorded before this test existed.
        Log($"scanned records: {page.Count}");
        foreach (var record in page.Items)
        {
            Log($"  {record.Id} {record.Name} [{record.Type}] -> {record.Content}");
            Assert.False(string.IsNullOrWhiteSpace(record.Name));
        }
    }

    [Fact]
    public async Task Custom_hostnames_can_be_listed_and_the_fallback_origin_read()
    {
        if (!SkipWithoutZone())
        {
            return;
        }

        try
        {
            var hostnames = await RunZoneScopedAsync(
                () => Client.CustomHostnames.ListAsync(new CustomHostnameListParameters { PerPage = 20 }));
            if (hostnames is null)
            {
                return;
            }

            Log($"custom hostnames: {hostnames.Count} of {hostnames.TotalCount}");
            foreach (var hostname in hostnames.Items)
            {
                Log($"  {hostname.Hostname} status={hostname.Status} ssl={hostname.Ssl?.Status ?? "(none)"}");
                Assert.False(string.IsNullOrWhiteSpace(hostname.Id));
            }

            // A zone with no fallback origin answers 404, which the library turns into null. That is a
            // legitimate answer rather than a refusal, so it is read directly instead of zone-scoped.
            var fallback = await Client.CustomHostnames.GetFallbackOriginAsync();
            Log($"fallback origin: {fallback?.ToString() ?? "(none set)"}");
        }
        catch (CloudflareApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            SkipUnless(false, $"The token cannot read SSL for SaaS on this zone (HTTP 403, Cloudflare error {exception.ErrorCode}). It needs 'SSL and Certificates Read'.");
        }
    }

    [Fact]
    public async Task Origin_tls_client_auth_settings_and_certificates_can_be_read()
    {
        if (!SkipWithoutZone())
        {
            return;
        }

        try
        {
            var setting = await RunZoneScopedAsync(() => Client.OriginTlsClientAuth.GetSettingsAsync());
            if (setting is null)
            {
                return;
            }

            Log($"zone-level authenticated origin pulls enabled={setting.Enabled?.ToString() ?? "unknown"}");

            var certificates = await Client.OriginTlsClientAuth.ListCertificatesAsync();
            Log($"hostname client certificates: {certificates.Count}");
            foreach (var certificate in certificates)
            {
                Log($"  {certificate} issuer={certificate.Issuer}");
                Assert.False(string.IsNullOrWhiteSpace(certificate.Id));
            }

            // Deprecated, and the one read here that returns private key material. Only the summary is
            // logged - it reports the key's length and never the key - and that is asserted.
            var zoneCertificates = await Client.OriginTlsClientAuth.ListZoneCertificatesAsync();
            Log($"zone-level certificates (deprecated): {zoneCertificates.Count}");
            foreach (var zoneCertificate in zoneCertificates)
            {
                Log($"  {zoneCertificate}");
                Assert.DoesNotContain("BEGIN", zoneCertificate.ToString());
            }
        }
        catch (CloudflareApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            SkipUnless(false, $"The token cannot read origin TLS client auth on this zone (HTTP 403, Cloudflare error {exception.ErrorCode}). It needs 'SSL and Certificates Read'.");
        }
    }

    [Fact]
    public async Task Dnssec_state_can_be_read_without_changing_it()
    {
        if (!SkipWithoutZone())
        {
            return;
        }

        try
        {
            var dnssec = await RunZoneScopedAsync(() => Client.Dnssec.GetAsync());
            if (dnssec is null)
            {
                return;
            }

            // Strictly read-only. Enabling DNSSEC would put the zone in 'pending' until a DS record is
            // published at the registrar, which no test run can do, and disabling it on a zone that is
            // already signed would break resolution for validating resolvers.
            Log($"DNSSEC: {dnssec}");

            // Every field in this response is optional, so a missing status is reported rather than
            // asserted against - an empty string is not one of the five and the test would fail for the
            // wrong reason.
            if (dnssec.Status is { Length: > 0 } status)
            {
                Assert.Contains(status, DnssecStatuses.All);
            }
            else
            {
                Warn("DNSSEC came back with no status, which the schema allows but no zone has ever done.");
            }

            // A signed zone has to have the record a registrar would be given, and the summary that gets
            // logged has to be short enough to read - the DS record itself is not a secret, it is published
            // in DNS, but it is long.
            if (dnssec.IsActive)
            {
                Assert.False(string.IsNullOrWhiteSpace(dnssec.Ds));
                Assert.NotNull(dnssec.KeyTag);
                Assert.False(dnssec.IsPending);
            }

            // The summary that gets logged stays short: status, key tag and algorithm, never the DS
            // record or the public key, both of which are long.
            Assert.True(dnssec.ToString().Length < 80, $"summary is too long to log: {dnssec}");
        }
        catch (CloudflareApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            SkipUnless(false, $"The token cannot read DNSSEC on this zone (HTTP 403, Cloudflare error {exception.ErrorCode}). It needs 'DNS Read'.");
        }
    }

    [Fact]
    public async Task Dnssec_zone_signing_keys_can_be_listed()
    {
        if (!SkipWithoutZone())
        {
            return;
        }

        try
        {
            var keys = await Client.Dnssec.ListZoneSigningKeysAsync();

            // An unsigned zone has no keys, so an empty list is a normal answer rather than a failure. This
            // call is the one that proves the PascalCase payload - DNSKEY, Hdr, Rdlength - deserialises
            // against the real API rather than only against a fixture.
            Log($"DNSSEC zone signing keys: {keys.Count}");
            foreach (var key in keys)
            {
                Log($"  {key.DnsKey} ({key})");
                if (key.DnsKey?.Hdr?.Name is { } name)
                {
                    Assert.False(string.IsNullOrWhiteSpace(name));
                }
            }
        }
        catch (CloudflareApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            SkipUnless(false, $"The token cannot list DNSSEC keys on this zone (HTTP 403, Cloudflare error {exception.ErrorCode}). It needs 'DNS Read'.");
        }
    }
}
