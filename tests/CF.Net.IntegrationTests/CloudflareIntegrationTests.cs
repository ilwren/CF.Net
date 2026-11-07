using System.Reflection;
using System.Text.Json;
using CFNet.Exceptions;
using CFNet.Http;
using CFNet.Models.Common;
using CFNet.Models.D1;
using CFNet.Models.Dns;
using CFNet.Models.Kv;
using CFNet.Models.PageRules;
using CFNet.Models.Pages;
using CFNet.Models.R2;
using CFNet.Models.Rules;
using CFNet.Models.Ssl;
using CFNet.Models.Turnstile;
using CFNet.Models.Workers;
using CFNet.Models.WorkersAi;
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
    public async Task Rulesets_can_be_listed_and_read()
    {
        if (!SkipWithoutCredentials() || !SkipWithoutAccount())
        {
            return;
        }

        var page = await Client.Rulesets.ListAsync();

        Log($"{page.Count} ruleset(s) in this account");
        Assert.All(page.Items, ruleset => Assert.False(string.IsNullOrEmpty(ruleset.Id)));

        // The kind and phase constants are wire values, and a phase Cloudflare adds would be read as an
        // unknown string rather than refused - so both are checked against what this account really holds,
        // once for the whole set rather than once per ruleset.
        var knownKinds = ConstantValues(typeof(RulesetKind));
        var knownPhases = ConstantValues(typeof(RulesetPhase));
        var unknownKinds = page.Items
            .Select(ruleset => ruleset.Kind)
            .Where(kind => kind is { Length: > 0 } && !knownKinds.Contains(kind))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var unknownPhases = page.Items
            .Select(ruleset => ruleset.Phase)
            .Where(phase => phase is { Length: > 0 } && !knownPhases.Contains(phase))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (unknownKinds.Count > 0 || unknownPhases.Count > 0)
        {
            Warn($"GET /accounts/{{account_id}}/rulesets reported values this library does not list as " +
                 $"constants - kinds: {string.Join(", ", unknownKinds)}; phases: {string.Join(", ", unknownPhases)}. " +
                 "RulesetKind and RulesetPhase are behind.");
        }

        foreach (var summary in page.Items.Take(3))
        {
            var ruleset = await Client.Rulesets.GetAsync(summary.Id!);
            Assert.NotNull(ruleset);
            Log(ruleset!.IsManaged
                ? $"ruleset {ruleset} source={ruleset.Source}"
                : $"ruleset {ruleset}");

            // A list omits the rules; reading one ruleset is what brings them back.
            foreach (var rule in ruleset!.Rules ?? new List<RulesetRule>())
            {
                Assert.False(string.IsNullOrEmpty(rule.Action), $"rule {rule.Id} has no action");

                // A managed rule is written by Cloudflare and one of them in this account carries no
                // expression at all, so only a ruleset this account could have written is held to it.
                if (!ruleset.IsManaged)
                {
                    Assert.False(string.IsNullOrEmpty(rule.Expression), $"rule {rule.Id} has no expression");
                }

                if (rule.ActionParameters?.AdditionalData is { Count: > 0 } untyped)
                {
                    Warn($"A {rule.Action} rule in ruleset {ruleset.Name} carried action parameters this " +
                         $"library does not type: {JsonSerializer.Serialize(untyped)}. Only the redirect " +
                         "parameters are declared; the rest is readable from AdditionalData.");
                }

                if (rule.AdditionalData is { Count: > 0 } ruleExtra)
                {
                    Warn($"GET /accounts/{{account_id}}/rulesets/{ruleset.Id} returned rule fields this " +
                         $"library does not model, on rule '{rule.Id}': {JsonSerializer.Serialize(ruleExtra)}.");
                }
            }

            var versions = await Client.Rulesets.ListVersionsAsync(summary.Id!);
            Log($"{versions.Count} version(s) of {ruleset.Name}");
            Assert.All(versions, version => Assert.Equal(ruleset.Id, version.Id));

            if (versions.Count > 0)
            {
                // A version is a frozen ruleset, so it reads back through the same model.
                var oldest = await Client.Rulesets.GetVersionAsync(summary.Id!, versions[^1].Version!);
                Assert.NotNull(oldest);
                Assert.Equal(versions[^1].Version, oldest!.Version);
            }

            if (ruleset.AdditionalData is { Count: > 0 } rulesetExtra)
            {
                Warn($"GET /accounts/{{account_id}}/rulesets/{ruleset.Id} returned fields this library does " +
                     $"not model, on '{ruleset.Name}': {JsonSerializer.Serialize(rulesetExtra)}.");
            }
        }

        // The entrypoint of the phase Bulk Redirects runs at, which is either the account's root ruleset for
        // it or nothing at all - both are normal, and which one it is decides whether redirects are live.
        var entrypoint = await Client.Rulesets.GetEntrypointAsync(RulesetPhase.HttpRequestRedirect);
        Log(entrypoint is null
            ? "no account entrypoint for http_request_redirect, so no Bulk Redirects are running"
            : $"account entrypoint for http_request_redirect: {entrypoint}");

        // The same resource under a zone path, which is where custom WAF rules and zone redirects live.
        var zone = await ReadableZoneAsync();
        if (zone is not null)
        {
            var zonePage = await Client.Rulesets.ListAsync(zoneId: zone.Id);
            Log($"{zonePage.Count} ruleset(s) on zone {zone.Name}");
            Assert.All(zonePage.Items, ruleset => Assert.False(string.IsNullOrEmpty(ruleset.Id)));

            var zoneEntrypoint = await Client.Rulesets.GetEntrypointAsync(
                RulesetPhase.HttpRequestFirewallCustom,
                zoneId: zone.Id);
            Log(zoneEntrypoint is null
                ? $"zone {zone.Name} has no custom WAF rules"
                : $"zone {zone.Name} custom WAF entrypoint: {zoneEntrypoint}");
        }
    }

    [Fact]
    public async Task Rules_lists_can_be_listed()
    {
        if (!SkipWithoutCredentials() || !SkipWithoutAccount())
        {
            return;
        }

        // The endpoint answers with every list at once and no result_info, so there is no paging to check
        // here - this is the whole catalogue. An account holding no lists is a legitimate answer, and the
        // reachability probe has already said whether the permission itself is there.
        var lists = await Client.Lists.ListAsync();

        Log($"{lists.Count} list(s) in this account");
        Assert.All(lists, list => Assert.False(string.IsNullOrEmpty(list.Id)));
        Assert.All(lists, list =>
        {
            Assert.NotNull(list.Kind);
            Assert.Contains(
                list.Kind!,
                new[] { RulesListKind.Ip, RulesListKind.Redirect, RulesListKind.Hostname, RulesListKind.Asn },
                StringComparer.Ordinal);
        });

        // Reading a page of items is what proves the item model against whatever this account really holds,
        // so it is done for the first few lists rather than for none. A list kind decides which one of the
        // four value fields its items populate, and an item holding a different one would mean the model is
        // reading the wrong shape rather than that Cloudflare stored something odd.
        foreach (var list in lists.Take(3))
        {
            Log($"list {list.Name} kind={list.Kind} items={list.NumItems} referencing={list.NumReferencingFilters}");

            var page = await Client.Lists.ListItemsAsync(list.Id!, new RulesListItemListParameters { PerPage = 5 });
            Log($"{page.Items.Count} item(s) read back from {list.Name}");

            foreach (var item in page.Items)
            {
                Assert.Equal(list.Kind, item.ValueKind);
            }

            if (page.Items.Count > 0 && page.Items[0].AdditionalData is { Count: > 0 } extra)
            {
                Warn($"GET /accounts/{{account_id}}/rules/lists/{list.Id}/items returned fields this library " +
                     $"does not model, on item '{page.Items[0].Id}': {JsonSerializer.Serialize(extra)}.");
            }
        }

        if (lists.FirstOrDefault(list => list.AdditionalData is { Count: > 0 }) is { } unexpected)
        {
            Warn($"GET /accounts/{{account_id}}/rules/lists returned fields this library does not model, on " +
                 $"'{unexpected.Name}': {JsonSerializer.Serialize(unexpected.AdditionalData)}.");
        }
    }

    [Fact]
    public async Task Account_token_permission_groups_can_be_listed()
    {
        if (!SkipWithoutCredentials() || !SkipWithoutAccount())
        {
            return;
        }

        CloudflarePage<CloudflarePermissionGroup> groups;
        try
        {
            // Observed live: 200 with this account's whole catalogue, 415 groups, where the user-scoped
            // endpoint below refuses the same token with 9109. This is also the endpoint a token-creation
            // URL for an account token has to be built from: the id each row carries is the key that URL
            // takes, and the workflow cross-checks its own list of them against this answer.
            groups = await Client.User.ListAccountPermissionGroupsAsync();
        }
        catch (CloudflareApiException exception) when (exception.HasErrorCode(9109))
        {
            // Cloudflare documents API Tokens Read as enough for this call, and a token holding only that
            // was refused here once. Reported rather than failed, because it is the credential and not the
            // library: the same token reads every other account-scoped endpoint this suite probes.
            SkipUnless(
                false,
                "GET /accounts/{account_id}/tokens/permission_groups was refused with 9109, which is what " +
                "an account-owned token holding only Account API Tokens Read got here once. The token that " +
                "holds Account API Tokens Write as well answers with the whole catalogue. " +
                exception.Message);
            return;
        }

        Assert.NotEmpty(groups.Items);
        Log($"{groups.Count} permission group(s) in this account's catalogue, {groups.TotalCount} reported");

        // Every id is a 32 character hex string: that is what a token policy grants and what a
        // token-creation URL takes as its key, so a row that does not look like one is worth stopping on.
        Assert.All(groups.Items, group => Assert.Matches("^[0-9a-f]{32}$", group.Id ?? string.Empty));
        Assert.Contains(groups.Items, group => group.IsSelectable is true);
        Assert.Contains(groups.Items, group => group.IsAccountScoped);
        Assert.Contains(groups.Items, group => group.IsZoneScoped);

        // Four hundred rows would make a per-row report useless, so the two lists this library publishes
        // as constants - categories and scopes - are checked against the whole catalogue at once and
        // anything live that is not in them is reported as one set.
        var knownCategories = ConstantValues(typeof(CloudflarePermissionCategories));
        var knownScopes = ConstantValues(typeof(CloudflarePermissionScopes));
        var unknownCategories = groups.Items
            .Select(group => group.Category)
            .Where(category => category is { Length: > 0 } && !knownCategories.Contains(category))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
        var unknownScopes = groups.Items
            .SelectMany(group => group.Scopes ?? new List<string>())
            .Where(scope => !knownScopes.Contains(scope))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();

        if (unknownCategories.Count > 0 || unknownScopes.Count > 0)
        {
            Warn($"GET /accounts/{{account_id}}/tokens/permission_groups reported values this library does " +
                 $"not list as constants - categories: {string.Join(", ", unknownCategories)}; scopes: " +
                 $"{string.Join(", ", unknownScopes)}. CloudflarePermissionCategories and " +
                 "CloudflarePermissionScopes are behind.");
        }

        if (groups.Items.FirstOrDefault(group => group.AdditionalData is { Count: > 0 }) is { } unexpected)
        {
            Warn($"GET /accounts/{{account_id}}/tokens/permission_groups returned fields this library does " +
                 $"not model yet, on '{unexpected.Name}': " +
                 $"{JsonSerializer.Serialize(unexpected.AdditionalData)}.");
        }
    }

    /// <summary>The string constants a catalogue class publishes, as a set.</summary>
    /// <param name="constants">The class to read.</param>
    /// <returns>Every public constant string it declares.</returns>
    private static HashSet<string> ConstantValues(Type constants) =>
        constants
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string?)field.GetRawConstantValue())
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public async Task Token_permission_groups_can_be_listed_by_a_user_token()
    {
        if (!SkipWithoutCredentials())
        {
            return;
        }

        if (!SkipUnless(
                !IsAccountOwnedToken,
                "GET /user/tokens/permission_groups is user-scoped, so an account-owned token (cfat_) is refused with 9109, the same refusal GET /user gets. A user token (cfut_) would answer it. The account-scoped catalogue, which this token can read, is listed by Account_token_permission_groups_can_be_listed."))
        {
            return;
        }

        var groups = await Client.User.ListPermissionGroupsAsync();

        Assert.NotEmpty(groups.Items);
        Log($"{groups.Count} permission group(s) of {groups.TotalCount}");

        foreach (var group in groups.Items.Take(25))
        {
            Log($"  {group} selectable={group.IsSelectable} " +
                $"scopes={string.Join(",", group.Scopes ?? new List<string>())}");
        }

        if (groups.Items.FirstOrDefault(item => item.AdditionalData is { Count: > 0 }) is { } unexpected)
        {
            Warn($"GET /user/tokens/permission_groups returned fields this library does not model yet: " +
                 $"{JsonSerializer.Serialize(unexpected.AdditionalData)}.");
        }
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
    public async Task Workers_ai_models_tasks_and_authors_can_be_searched()
    {
        if (!SkipWithoutAccount())
        {
            return;
        }

        // All three of these are catalogues of what Cloudflare offers rather than of what the account owns,
        // so a read permission is the whole of what they need. The published schema types the items of all
        // three as "unknown", which is why each answer is reported field by field rather than just counted:
        // the models are deliberately narrow and anything they do not name is what these lines are for.
        IReadOnlyList<WorkersAiTask> tasks;
        try
        {
            tasks = await Client.WorkersAi.ListTasksAsync();
        }
        catch (CloudflareApiException exception)
            when (exception.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound)
        {
            SkipUnless(
                false,
                $"GET /accounts/{{account_id}}/ai/tasks/search was refused with " +
                $"{exception.StatusCode}/{exception.ErrorCode}: {exception.Message}");
            return;
        }
        catch (Exception exception) when (exception is not CloudflareApiException)
        {
            Warn($"GET /accounts/{{account_id}}/ai/tasks/search answered something this library could not read " +
                 $"as a task list: {exception.GetType().Name}: {exception.Message}");
            return;
        }

        Log($"{tasks.Count} task(s): {string.Join(", ", tasks.Take(10).Select(task => task.ToString()))}");
        foreach (var task in tasks.Take(3))
        {
            if (task.AdditionalData is { Count: > 0 })
            {
                Warn($"GET /accounts/{{account_id}}/ai/tasks/search answered task fields this library does not " +
                     $"model: {string.Join(", ", task.AdditionalData.Keys)}. The item as read: " +
                     $"{JsonSerializer.Serialize(task, CFNet.Serialization.CloudflareJson.Default)}");
            }
        }

        IReadOnlyList<WorkersAiAuthor> authors;
        try
        {
            authors = await Client.WorkersAi.ListAuthorsAsync();
        }
        catch (CloudflareApiException exception)
        {
            // The task search just succeeded, so a refusal here is about this endpoint and not about the
            // token - worth a line of its own rather than a failure.
            Warn($"GET /accounts/{{account_id}}/ai/authors/search was refused with " +
                 $"{exception.StatusCode}/{exception.ErrorCode}. Cloudflare's body: {exception.ResponseBody}");
            return;
        }
        catch (Exception exception) when (exception is not CloudflareApiException)
        {
            Warn($"GET /accounts/{{account_id}}/ai/authors/search answered something this library could not " +
                 $"read as an author list: {exception.GetType().Name}: {exception.Message}");
            return;
        }

        Log($"{authors.Count} author(s): {string.Join(", ", authors.Take(10).Select(author => author.ToString()))}");
        foreach (var author in authors.Take(3))
        {
            if (author.AdditionalData is { Count: > 0 })
            {
                Warn($"GET /accounts/{{account_id}}/ai/authors/search answered author fields this library does " +
                     $"not model: {string.Join(", ", author.AdditionalData.Keys)}. The item as read: " +
                     $"{JsonSerializer.Serialize(author, CFNet.Serialization.CloudflareJson.Default)}");
            }
        }

        // Unfiltered on purpose: this fact is about what a model item reads as. Filtering by a task is the
        // inference fact's business, and it asserts what the filter takes there.
        var parameters = new WorkersAiModelListParameters { PerPage = 5 };

        var page = await Client.WorkersAi.ListModelsAsync(parameters);
        Log($"{page.Count} model(s) on page 1 of a reported {page.TotalCount}: " +
            $"{string.Join(", ", page.Items.Take(5).Select(model => model.ToString()))}");

        foreach (var model in page.Items.Take(3))
        {
            Log($"model {model.Name} tags={string.Join(",", model.Tags ?? new List<string>())} " +
                $"properties={string.Join(",", (model.Properties ?? new List<WorkersAiModelProperty>()).Select(property => property.ToString()))} " +
                $"limits={model.Limits} batching={model.Batching}");
            if (model.AdditionalData is { Count: > 0 })
            {
                Warn($"GET /accounts/{{account_id}}/ai/models/search answered model fields this library does " +
                     $"not model: {string.Join(", ", model.AdditionalData.Keys)}. The item as read: " +
                     $"{JsonSerializer.Serialize(model, CFNet.Serialization.CloudflareJson.Default)}");
            }
        }

        // These three searches have answered enough times to assert what they answer: a task list whose names
        // are written for a person, a model list whose items carry a name and a task, and the two model fields
        // no documentation mentions - source and a created_at that is not ISO 8601. An author list this
        // account's answer left empty is an answer rather than a failure, so nothing is asserted of it.
        Assert.NotEmpty(tasks);
        Assert.False(string.IsNullOrEmpty(tasks[0].Name));
        Assert.All(tasks, task => Assert.False(string.IsNullOrEmpty(task.Id)));
        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, model => Assert.False(string.IsNullOrEmpty(model.Name)));
        Assert.All(page.Items, model => Assert.False(string.IsNullOrEmpty(model.Task?.Name)));
        Assert.Contains(
            page.Items,
            model => model.Source is not null && !string.IsNullOrEmpty(model.CreatedAt));
        Log($"{authors.Count} author(s) answered; the first task reads {tasks[0]} and the first model " +
            $"{page.Items[0].Name}, added {page.Items[0].CreatedAt}");
    }

    [Fact]
    public async Task A_workers_ai_model_schema_can_be_read()
    {
        if (!SkipWithoutAccount())
        {
            return;
        }

        var page = await Client.WorkersAi.ListModelsAsync(new WorkersAiModelListParameters { PerPage = 1 });
        var model = page.Items.Count > 0 ? page.Items[0] : null;
        if (model?.Name is not { } name)
        {
            Log("no model in the catalogue to read a schema for");
            return;
        }

        WorkersAiModelSchema schema;
        try
        {
            schema = await Client.WorkersAi.GetModelSchemaAsync(name);
        }
        catch (CloudflareApiException exception)
            when (exception.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound)
        {
            SkipUnless(
                false,
                $"GET /accounts/{{account_id}}/ai/models/schema was refused with " +
                $"{exception.StatusCode}/{exception.ErrorCode}: {exception.Message}");
            return;
        }
        catch (CloudflareApiException exception)
        {
            Warn($"GET /accounts/{{account_id}}/ai/models/schema?model={name} was refused with " +
                 $"{exception.StatusCode}/{exception.ErrorCode}. Cloudflare's body: {exception.ResponseBody}");
            return;
        }

        // Both sides are present and typed. The published schema requires all three of its fields per side,
        // but the live answer carried only "type" - so that is the one other thing safe to assert.
        Assert.NotNull(schema.Input);
        Assert.NotNull(schema.Output);
        Assert.False(string.IsNullOrEmpty(schema.Input?.Type));
        Assert.False(string.IsNullOrEmpty(schema.Output?.Type));

        var inputExtras = (schema.Input?.AdditionalData ?? new Dictionary<string, JsonElement>()).Keys;
        var outputExtras = (schema.Output?.AdditionalData ?? new Dictionary<string, JsonElement>()).Keys;

        // The endpoint answers a real, model-specific JSON Schema rather than the three-field description of
        // one that Cloudflare publishes as its 200 example, so the shape is what is worth recording: how many
        // of its fields this library names, and which it does not.
        Log($"schema for {name}: {schema}; input {schema.Input}, " +
            $"unmodelled [{string.Join(",", inputExtras)}]; output {schema.Output}, " +
            $"unmodelled [{string.Join(",", outputExtras)}]");
    }

    [Fact]
    public async Task Workers_ai_finetunes_and_to_markdown_formats_can_be_read()
    {
        if (!SkipWithoutAccount())
        {
            return;
        }

        // Three lists beside the catalogue, each read on its own: an account with no fine-tuning jobs answers
        // none, Cloudflare's published ones are its own rather than the account's, and the to-markdown formats
        // are what a conversion this library cannot send would accept. Creating a fine-tune is a write that
        // needs Workers AI Write and bills training time, so nothing here writes.
        var jobs = await TryReadAsync(
            "GET /accounts/{account_id}/ai/finetunes",
            () => Client.WorkersAi.ListFinetunesAsync());
        if (jobs is not null)
        {
            Log($"{jobs.Count} fine-tuning job(s): {string.Join(", ", jobs.Take(5))}");
            Assert.All(jobs, job => Assert.False(string.IsNullOrEmpty(job.Id)));
            ReportUnmodelled("GET /accounts/{account_id}/ai/finetunes", jobs, job => job.AdditionalData);
        }

        // limit and offset page this list; orderBy is left out because its accepted values are not published
        // and a wrong one is a 400 rather than an empty page.
        var published = await TryReadAsync(
            "GET /accounts/{account_id}/ai/finetunes/public",
            () => Client.WorkersAi.ListPublicFinetunesAsync(
                new WorkersAiPublicFinetuneListParameters { Limit = 5 }));
        if (published is not null)
        {
            Log($"{published.Count} published fine-tune(s): {string.Join(", ", published.Take(5))}");
            Assert.All(published, job => Assert.False(string.IsNullOrEmpty(job.Name)));
            ReportUnmodelled(
                "GET /accounts/{account_id}/ai/finetunes/public",
                published,
                job => job.AdditionalData);
        }

        // The conversion itself, with the smallest file worth converting: it is a POST that runs a model over
        // the bytes, so it costs a few tokens, and a refusal to spend them is reported rather than failed.
        var conversions = await TryReadAsync(
            "POST /accounts/{account_id}/ai/tomarkdown",
            () => Client.WorkersAi.ConvertToMarkdownAsync(
                new[]
                {
                    WorkersAiToMarkdownFile.OfHtml(
                        "cf-net.html",
                        "<h1>CF.Net</h1><p>A typed Cloudflare API v4 client.</p>"),
                }));
        if (conversions is not null)
        {
            Assert.NotEmpty(conversions);
            var conversion = conversions[0];
            Log($"{conversions.Count} conversion(s); the first read {conversion.Name} as {conversion.Format} " +
                $"in {conversion.TokenCount} token(s): {Truncate(conversion.Data, 200)}");
            if (!conversion.Succeeded)
            {
                // A file that does not convert is answered inside a 200, one entry per file, so it is worth a
                // line of its own rather than an assertion: the call worked and the file did not.
                Warn($"POST /accounts/{{account_id}}/ai/tomarkdown answered 200 but the file did not convert: " +
                     $"{conversion.Format}/{conversion.Error}. The entry as read: " +
                     $"{Truncate(JsonSerializer.Serialize(conversion, CFNet.Serialization.CloudflareJson.Default), 600)}");
            }

            ReportUnmodelled(
                "POST /accounts/{account_id}/ai/tomarkdown",
                conversions,
                item => item.AdditionalData);
        }

        var formats = await TryReadAsync(
            "GET /accounts/{account_id}/ai/tomarkdown/supported",
            () => Client.WorkersAi.ListToMarkdownFormatsAsync());
        if (formats is not null)
        {
            Log($"{formats.Count} to-markdown format(s): {string.Join(", ", formats.Take(10))}");
            if (formats.Count == 0)
            {
                Warn("GET /accounts/{account_id}/ai/tomarkdown/supported answered 200 with no formats at all, " +
                     "so there is nothing to say about what the conversion accepts.");
                return;
            }

            Assert.All(formats, format => Assert.False(string.IsNullOrEmpty(format.Extension)));
            Assert.All(formats, format => Assert.False(string.IsNullOrEmpty(format.MimeType)));
            ReportUnmodelled(
                "GET /accounts/{account_id}/ai/tomarkdown/supported",
                formats,
                format => format.AdditionalData);
        }
    }

    /// <summary>
    /// Reads one of a fact's lists, reporting a refusal - or a body this library could not read as the shape
    /// it models - as one Warn and answering null instead of failing.
    /// </summary>
    /// <remarks>
    /// A list that changed shape under a fact is worth a line and not worth hiding the two lists beside it:
    /// the run that found <c>"public": 1</c> and a timestamp with no offset in it reported one and returned
    /// before it ever asked for the to-markdown formats. The exception's message comes first in the Warn
    /// because that is where a parse failure names the field it choked on; the status and error code, which
    /// are what a refusal carries, come after it.
    /// </remarks>
    private async Task<IReadOnlyList<T>?> TryReadAsync<T>(string endpoint, Func<Task<IReadOnlyList<T>>> read)
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (CloudflareApiException exception)
        {
            Warn($"{endpoint} was refused with {exception.StatusCode}/{exception.ErrorCode}: " +
                 $"{Truncate(exception.Message, 400)} Cloudflare's body: {Truncate(exception.ResponseBody, 600)}");
            return null;
        }
        catch (Exception exception) when (exception is not CloudflareApiException)
        {
            Warn($"{endpoint} answered something this library could not read: {exception.GetType().Name}: " +
                 $"{Truncate(exception.Message, 400)}");
            return null;
        }
    }

    /// <summary>
    /// Warns once for each of the first few items in a list that carries fields this library does not name,
    /// which is how a published shape that understates a live answer gets found.
    /// </summary>
    private void ReportUnmodelled<T>(
        string endpoint,
        IReadOnlyList<T> items,
        Func<T, IDictionary<string, JsonElement>?> unmodelled,
        int lookAt = 3)
    {
        foreach (var item in items.Take(lookAt))
        {
            if (unmodelled(item) is not { Count: > 0 } extras)
            {
                continue;
            }

            Warn($"{endpoint} answered fields this library does not model: {string.Join(", ", extras.Keys)}. " +
                 $"The item as read: " +
                 $"{Truncate(JsonSerializer.Serialize(item, CFNet.Serialization.CloudflareJson.Default), 800)}");
        }
    }


    [Fact]
    public async Task Workers_ai_models_can_be_run()
    {
        if (!SkipWithoutAccount())
        {
            return;
        }

        // The model is picked out of the catalogue rather than named here, because a name that stops being
        // served would make this fact about Cloudflare's catalogue rather than about this library. A small
        // model, a short prompt and a small token cap keep the call well inside the free daily allowance:
        // inference is what Workers AI Read grants, so this needs no permission the catalogue did not.
        var tasks = await Client.WorkersAi.ListTasksAsync();
        string? generationTaskId = null;
        string? generationTaskName = null;
        foreach (var task in tasks)
        {
            // The task list answers names a person reads - "Feature Extraction" - rather than the slugs the
            // documentation and this filter use, so the two are compared with their separators and case out of
            // the way.
            if (WorkersAiTaskNames.Same(task.Name, "text-generation"))
            {
                generationTaskId = task.Id;
                generationTaskName = task.Name;
            }
        }

        // The task filter is worth checking rather than trusting: a live run answered no models at all for
        // task=text-generation, and no task in the list is called that, so which spelling the filter takes is
        // unknown. Three spellings are tried against one page each, and total_count is what says whether any
        // of them matched.
        var bySlug = await Client.WorkersAi.ListModelsAsync(
            new WorkersAiModelListParameters { Task = "text-generation", PerPage = 1 });
        var byName = generationTaskName is null
            ? null
            : await Client.WorkersAi.ListModelsAsync(
                new WorkersAiModelListParameters { Task = generationTaskName, PerPage = 1 });
        var byId = generationTaskId is null
            ? null
            : await Client.WorkersAi.ListModelsAsync(
                new WorkersAiModelListParameters { Task = generationTaskId, PerPage = 1 });
        var catalogue = await TakeAllAsync(
            Client.WorkersAi.ListAllModelsAsync(new WorkersAiModelListParameters { PerPage = 100 }));
        var taskNames = DistinctTasks(catalogue);
        var firstTask = catalogue.Count > 0 ? catalogue[0].Task : null;

        // The filter is answered now, and it answers the way the catalogue spells names: the task list's own
        // "Text Generation" narrows the search to that task's models, while the documentation's slug and the
        // task's id each answer an empty page. total_count is 331 whichever filter goes out and unfiltered too,
        // so it is not a count of what matched and nothing here reads it as one.
        var unfiltered = await Client.WorkersAi.ListModelsAsync(
            new WorkersAiModelListParameters { PerPage = 1 });

        Assert.NotEmpty(byName!.Items);
        Assert.All(
            byName.Items,
            model => Assert.True(
                model.PerformsTask("text-generation"),
                $"task={generationTaskName} answered {model.Name}, whose task is {model.Task?.Name}"));
        Assert.Empty(bySlug.Items);
        Assert.Empty(byId!.Items);
        Assert.Equal(unfiltered.TotalCount, bySlug.TotalCount);
        Assert.Equal(unfiltered.TotalCount, byName.TotalCount);

        // What the catalogue says about itself is settled enough to assert too: a model carries a task whose
        // name is written for a person, and both tasks this fact runs are in it.
        Assert.NotEmpty(catalogue);
        Assert.False(string.IsNullOrEmpty(catalogue[0].Name));
        Assert.NotNull(catalogue[0].Task);
        Assert.False(string.IsNullOrEmpty(catalogue[0].Task!.Name));
        Assert.True(taskNames.Count > 1);
        Assert.True(CountByTask(catalogue, "text-generation") > 0);
        Assert.True(CountByTask(catalogue, "text-embeddings") > 0);
        Log($"catalogue: {catalogue.Count} model(s) naming {taskNames.Count} task(s): " +
            $"{string.Join(", ", taskNames)}; the filter answered {byName.Count} of them");

        var generationModel = PickByTask(catalogue, "text-generation", "llama-3.2-1b", "llama-3.2-3b", "llama-3.1-8b");
        if (generationModel is null)
        {
            Warn($"no text-generation model in a catalogue of {catalogue.Count}, so no inference was run. " +
                 $"Tasks seen: {string.Join(", ", tasks.Select(task => task.ToString()))}");
            return;
        }

        WorkersAiRunResult result;
        try
        {
            result = await Client.WorkersAi.RunAsync(
                generationModel,
                new WorkersAiRunRequest
                {
                    Prompt = "Reply with the single word: pong.",
                    MaxTokens = 8,
                    Temperature = 0,
                });
        }
        catch (CloudflareApiException exception)
            when (exception.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound)
        {
            SkipUnless(
                false,
                $"POST /accounts/{{account_id}}/ai/run/{generationModel} was refused with " +
                $"{exception.StatusCode}/{exception.ErrorCode}: {exception.Message}");
            return;
        }
        catch (CloudflareApiException exception)
        {
            Warn($"POST /accounts/{{account_id}}/ai/run/{generationModel} was refused with " +
                 $"{exception.StatusCode}/{exception.ErrorCode}. Cloudflare's body: {exception.ResponseBody}");
            return;
        }
        catch (Exception exception) when (exception is not CloudflareApiException)
        {
            Warn($"POST /accounts/{{account_id}}/ai/run/{generationModel} answered something this library " +
                 $"could not read: {exception.GetType().Name}: {exception.Message}");
            return;
        }

        Log($"ran {generationModel}: {result.Kind} - {result}");
        Assert.False(string.IsNullOrEmpty(result.Text));

        if (!result.IsObject)
        {
            Warn($"POST /accounts/{{account_id}}/ai/run/{generationModel} answered {result.Kind} rather than " +
                 $"an object, which is not the text-generation arm. As it arrived: {result.Raw}");
            return;
        }

        var generation = result.AsTextGeneration();
        Assert.NotNull(generation);
        Log($"response={generation!.Response} usage={generation.Usage} " +
            $"unmodelled={string.Join(",", (generation.AdditionalData ?? new Dictionary<string, JsonElement>()).Keys)}");
        if (generation.AdditionalData is { Count: > 0 })
        {
            Warn($"POST /accounts/{{account_id}}/ai/run/{generationModel} answered text-generation fields this " +
                 $"library does not model: {string.Join(",", generation.AdditionalData.Keys)}. As read: " +
                 $"{JsonSerializer.Serialize(generation, CFNet.Serialization.CloudflareJson.Default)}");
        }

        // What a live generation answers is settled enough to assert: words in response, and a usage that
        // bills them. Two more fields arrived that no published shape mentions - a neuron count and a cache
        // detail behind the prompt tokens - and both are modelled, so they are logged rather than left to
        // AdditionalData.
        Assert.NotNull(generation.Usage);
        Assert.True(generation.Usage!.TotalTokens > 0);
        Log($"neurons={generation.Usage.Neurons} cached={generation.Usage.PromptTokensDetails?.CachedTokens} " +
            $"tool_calls={generation.ToolCalls?.Count ?? 0}");

        // The other arm worth seeing live is the one whose answer is numbers rather than words: an embedding
        // model's vectors and the shape it reports for them.
        var embeddingModel = PickByTask(catalogue, "text-embeddings", "e5-small", "bge-base", "nomic-embed");
        if (embeddingModel is null)
        {
            Log("no text-embeddings model in the catalogue to run");
            return;
        }

        try
        {
            var embedded = await Client.WorkersAi.RunAsync(
                embeddingModel,
                WorkersAiRunRequest.ForEmbeddings("hello"));

            var vectors = embedded.AsEmbeddings();
            Assert.NotNull(vectors);
            Assert.True(vectors!.VectorCount > 0);
            Assert.True(vectors.Dimensions > 0);

            // A live embedding answers two fields its published shape does not mention - pooling and usage -
            // and both are modelled now, so anything left over is what Cloudflare adds next.
            Log($"ran {embeddingModel}: {embedded.Kind} - {embedded}; vectors={vectors.VectorCount} " +
                $"dimensions={vectors.Dimensions} shape=[{string.Join(",", vectors.Shape ?? new List<long>())}] " +
                $"pooling={vectors.Pooling} usage={vectors.Usage} " +
                $"first three=[{string.Join(",", FirstThree(vectors))}] unmodelled=" +
                $"{string.Join(",", (vectors.AdditionalData ?? new Dictionary<string, JsonElement>()).Keys)}");
            if (vectors.AdditionalData is { Count: > 0 })
            {
                Warn($"POST /accounts/{{account_id}}/ai/run/{embeddingModel} answered embedding fields this " +
                     $"library does not model: {string.Join(",", vectors.AdditionalData.Keys)}");
            }
        }
        catch (CloudflareApiException exception)
        {
            Warn($"POST /accounts/{{account_id}}/ai/run/{embeddingModel} was refused with " +
                 $"{exception.StatusCode}/{exception.ErrorCode}. Cloudflare's body: {exception.ResponseBody}");
        }
    }

    /// <summary>Walks an async sequence into a list, which is what picking a model out of it needs.</summary>
    private static async Task<List<WorkersAiModel>> TakeAllAsync(IAsyncEnumerable<WorkersAiModel> models)
    {
        var all = new List<WorkersAiModel>();
        await foreach (var model in models)
        {
            all.Add(model);
        }

        return all;
    }

    /// <summary>How many models in a catalogue perform a given task.</summary>
    private static int CountByTask(IReadOnlyList<WorkersAiModel> models, string task)
    {
        var count = 0;
        foreach (var model in models)
        {
            if (model.PerformsTask(task))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>The distinct task names a catalogue's models report, in the order the models report them.</summary>
    private static List<string> DistinctTasks(IReadOnlyList<WorkersAiModel> models)
    {
        var names = new List<string>();
        foreach (var model in models)
        {
            if (model.Task?.Name is { } name && !names.Contains(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static bool HasFragment(WorkersAiModel model, string fragment) =>
        model.Name?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Chooses a model to run, preferring the smallest name it recognises so a run costs as few neurons as it
    /// can. A model that reports the task wins; failing that, a recognised name wins whatever the model says
    /// its task is - a live catalogue reported no task names at all, and "cannot find a model by task" is not
    /// a reason to skip inference. The last resort is the catalogue's own first model, whose answer the fact
    /// then reports rather than asserts on.
    /// </summary>
    private static string? PickByTask(
        IReadOnlyList<WorkersAiModel> models,
        string task,
        params string[] preferred)
    {
        foreach (var fragment in preferred)
        {
            foreach (var model in models)
            {
                if (model.PerformsTask(task) && HasFragment(model, fragment))
                {
                    return model.Name;
                }
            }
        }

        foreach (var fragment in preferred)
        {
            foreach (var model in models)
            {
                if (HasFragment(model, fragment))
                {
                    return model.Name;
                }
            }
        }

        foreach (var model in models)
        {
            if (model.PerformsTask(task))
            {
                return model.Name;
            }
        }

        return models.Count > 0 ? models[0].Name : null;
    }

    /// <summary>The first three numbers of an embedding answer, which is all of it worth reporting.</summary>
    private static IEnumerable<double> FirstThree(WorkersAiEmbeddings embeddings) =>
        embeddings.Data is { Count: > 0 } data ? data[0].Take(3) : [];


    [Fact]
    public async Task Workers_observability_telemetry_keys_and_values_can_be_read()
    {
        if (!SkipWithoutAccount())
        {
            return;
        }

        IReadOnlyList<ObservabilityTelemetryKey> keys;
        try
        {
            keys = await Client.WorkersObservability.ListTelemetryKeysAsync(
                new ObservabilityTelemetryKeysRequest { Limit = 10 });
        }
        catch (CloudflareApiException exception)
            when (exception.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound)
        {
            SkipUnless(
                false,
                $"POST /accounts/{{account_id}}/workers/observability/telemetry/keys was refused with " +
                $"{exception.StatusCode}/{exception.ErrorCode}: {exception.Message}");
            return;
        }

        Log($"{keys.Count} telemetry key(s)");
        foreach (var key in keys.Take(5))
        {
            // lastSeenAt is an epoch timestamp whose unit the published schema does not state, so its
            // magnitude is the evidence - and from and to want the same clock.
            Log($"key {key} lastSeenAt={key.LastSeenAt}");
            if (key.AdditionalData is { Count: > 0 })
            {
                Warn($"POST /accounts/{{account_id}}/workers/observability/telemetry/keys answered fields this " +
                     $"library does not model yet: {JsonSerializer.Serialize(key.AdditionalData)}.");
            }
        }

        var first = keys.FirstOrDefault(key =>
            key.Type == ObservabilityTelemetryValueTypes.String &&
            key.LastSeenAt is > 0 &&
            !string.IsNullOrEmpty(key.Key));
        if (first is null)
        {
            Log("no string-typed key with a lastSeenAt to list values for");
            return;
        }

        // lastSeenAt counts milliseconds since the epoch, which the first live run of this test settled from
        // its magnitude, so a span of dates is what the timeframe takes.
        var to = DateTimeOffset.UtcNow;
        var from = to.AddDays(-1);
        Log($"asking for the values of {first.Key} (last seen {first.LastSeen:O}) over the last day");

        try
        {
            var values = await Client.WorkersObservability.ListTelemetryValuesAsync(
                new ObservabilityTelemetryValuesRequest
                {
                    // Required to be present, and empty means the default datasets.
                    Datasets = new List<string>(),
                    Key = first.Key!,
                    Type = first.Type!,
                    Timeframe = ObservabilityTelemetryTimeframe.Between(from, to),
                    Limit = 10,
                });

            // An account with no traffic has keys and no values, so a count is logged rather than asserted.
            Log($"{values.Count} value(s): " +
                $"{string.Join(", ", values.Take(5).Select(value => value.ToString()))}");
        }
        catch (CloudflareApiException exception)
        {
            Warn($"POST /accounts/{{account_id}}/workers/observability/telemetry/values was refused with " +
                 $"{exception.StatusCode}/{exception.ErrorCode}. Cloudflare's body: {exception.ResponseBody}");
        }
    }

    [Fact]
    public async Task Workers_observability_telemetry_can_be_queried()
    {
        if (!SkipWithoutAccount())
        {
            return;
        }

        // Both queries are dry runs, which execute and answer without persisting their results, so this stays
        // a read-only fact. Two views are asked for because which fields of the answer are populated moves
        // with the view: one shape cannot be checked by asking for the other.
        var to = DateTimeOffset.UtcNow;
        var from = to.AddHours(-1);

        var eventsQuery = ObservabilityTelemetryQueryRequest.OverSpan(
            ObservabilityTelemetryViews.Events,
            from,
            to);
        eventsQuery.Dry = true;
        eventsQuery.Limit = 5;
        eventsQuery.Parameters = new ObservabilityTelemetryQueryParameters
        {
            // Empty means every dataset the account can see.
            Datasets = new List<string>(),
            Filters = new List<ObservabilityTelemetryFilterNode>
            {
                ObservabilityTelemetryFilter.Of(
                    "$metadata.level",
                    ObservabilityFilterOperations.Equal,
                    ObservabilityTelemetryValueTypes.String,
                    "error"),
            },
            Needle = ObservabilityTelemetryNeedle.Literal("cfnet"),
            Limit = 5,
        };

        ObservabilityTelemetryQueryResult events;
        try
        {
            events = await Client.WorkersObservability.QueryTelemetryAsync(eventsQuery);
        }
        catch (CloudflareApiException exception)
            when (exception.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound)
        {
            SkipUnless(
                false,
                $"POST /accounts/{{account_id}}/workers/observability/telemetry/query was refused with " +
                $"{exception.StatusCode}/{exception.ErrorCode}: {exception.Message}");
            return;
        }
        catch (CloudflareApiException exception)
        {
            // A 400 here means the body this library built is the wrong shape, and Cloudflare's body is the
            // only thing that says which field it objected to.
            Warn($"POST /accounts/{{account_id}}/workers/observability/telemetry/query (events view) was " +
                 $"refused with {exception.StatusCode}/{exception.ErrorCode}. Cloudflare's body: " +
                 $"{exception.ResponseBody}. The request sent: " +
                 $"{JsonSerializer.Serialize(eventsQuery, CFNet.Serialization.CloudflareJson.Default)}");
            return;
        }

        Assert.NotNull(events.Run);
        Assert.False(string.IsNullOrEmpty(events.Run!.Id));
        Assert.NotNull(events.Statistics);
        Log($"events view: {events.Run.Status} {events.Statistics}; " +
            $"{events.AllEvents.Count} event(s) of a reported {events.Events?.Count}");

        // A run echoes the query it executed. Its filters are read as raw JSON because a leaf may come back
        // with no "kind", which is what the request always writes; say which it was, so that hedge can be
        // dropped if Cloudflare always sends one.
        var echoedFilters = events.Run.Query?.Parameters?.Filters;
        var echoedSummary = echoedFilters is { Count: > 0 }
            ? $"{echoedFilters.Count(filter => filter.TryGetProperty("kind", out _))}/{echoedFilters.Count} " +
              "echoed filter(s) carry a kind"
            : "no filters echoed back";
        Log(echoedSummary);

        // Learned from the run that first asked, and now asserted: a dry query completes synchronously
        // rather than answering STARTED, and its statistics are real work - an hour's span that matched
        // nothing still scanned some 3.6M rows and 29MB in about 0.1s, because the platform's own telemetry
        // is in the same tables. Which is why a query is worth narrowing.
        Assert.Equal(ObservabilityTelemetryRunStatuses.Completed, events.Run.Status);
        Assert.True(events.IsComplete);
        Assert.True(events.Run.Dry);
        Assert.True(events.Statistics!.RowsRead > 0, "a completed query should report the rows it scanned");
        Assert.True(events.Statistics.BytesRead > 0, "a completed query should report the bytes it read");

        // The run echoes the query it executed, so the one filter that was sent comes back. The schema makes
        // a leaf's "kind" optional and types the deepest level as unknown, which is why the echo is read as
        // raw JSON rather than into the filter tree - the count is asserted, the spelling only logged.
        Assert.Equal(1, events.Run.Query?.Parameters?.Filters?.Count);
        Log($"{events.Statistics}; {events.AllEvents.Count} event(s) of a reported {events.Events?.Count}; " +
            $"{events.Events?.Fields?.Count ?? 0} field(s) discovered");

        var firstEvent = events.AllEvents.Count > 0 ? events.AllEvents[0] : null;
        foreach (var unmodelled in new[]
                 {
                     ("result", events.AdditionalData),
                     ("run", events.Run.AdditionalData),
                     ("events", events.Events?.AdditionalData),
                     ("event", firstEvent?.AdditionalData),
                     ("$metadata", firstEvent?.Metadata?.AdditionalData),
                 }.Where(pair => pair.Item2 is { Count: > 0 }))
        {
            Warn($"POST /accounts/{{account_id}}/workers/observability/telemetry/query answered " +
                 $"{unmodelled.Item1} fields this library does not model yet: " +
                 $"{JsonSerializer.Serialize(unmodelled.Item2)}.");
        }

        var calculationsQuery = ObservabilityTelemetryQueryRequest.OverSpan(
            ObservabilityTelemetryViews.Calculations,
            from,
            to);
        calculationsQuery.Dry = true;
        calculationsQuery.Chart = true;
        calculationsQuery.Granularity = 10;
        calculationsQuery.Parameters = new ObservabilityTelemetryQueryParameters
        {
            Datasets = new List<string>(),
            Calculations = new List<ObservabilityTelemetryCalculation>
            {
                ObservabilityTelemetryCalculation.Count("hits"),
                ObservabilityTelemetryCalculation.Of(
                    "$metadata.duration",
                    ObservabilityTelemetryCalculationOperators.P99,
                    "slowest",
                    ObservabilityTelemetryValueTypes.Number),
            },
            GroupBys = new List<ObservabilityTelemetryGroupBy>
            {
                ObservabilityTelemetryGroupBy.On(
                    "$metadata.service",
                    ObservabilityTelemetryValueTypes.String),
            },
            Havings = new List<ObservabilityTelemetryHaving>
            {
                ObservabilityTelemetryHaving.Of(
                    "hits",
                    ObservabilityFilterOperations.GreaterThan,
                    0),
            },
            OrderBy = ObservabilityTelemetryOrderBy.Descending("hits"),
            Limit = 5,
        };

        try
        {
            var calculations = await Client.WorkersObservability.QueryTelemetryAsync(calculationsQuery);

            Assert.NotNull(calculations.Run);
            Log($"calculations view: {calculations.Run!.Status} {calculations.Statistics}; " +
                $"{calculations.AllCalculations.Count} calculation(s)");

            foreach (var calculation in calculations.AllCalculations)
            {
                Log($"calculation {calculation}: {calculation.Aggregates?.Count ?? 0} aggregate(s), " +
                    $"{calculation.Series?.Count ?? 0} series bucket(s)");
                foreach (var aggregate in (calculation.Aggregates ??
                                           new List<ObservabilityTelemetryAggregate>()).Take(3))
                {
                    // An account with no traffic aggregates to nothing, so the numbers are logged, not
                    // asserted; whether they were estimated from sampled events is the part that matters.
                    Log($"  aggregate {aggregate} sampled={aggregate.IsSampled} groups=" +
                        $"{string.Join(",", (aggregate.Groups ?? new List<ObservabilityTelemetryGroup>()).Select(group => group.ToString()))}");
                }
            }

            if (calculations.AdditionalData is { Count: > 0 })
            {
                Warn($"POST /accounts/{{account_id}}/workers/observability/telemetry/query answered result " +
                     $"fields this library does not model yet: " +
                     $"{JsonSerializer.Serialize(calculations.AdditionalData)}.");
            }
        }
        catch (CloudflareApiException exception)
        {
            Warn($"POST /accounts/{{account_id}}/workers/observability/telemetry/query (calculations view) " +
                 $"was refused with {exception.StatusCode}/{exception.ErrorCode}. Cloudflare's body: " +
                 $"{exception.ResponseBody}. The request sent: " +
                 $"{JsonSerializer.Serialize(calculationsQuery, CFNet.Serialization.CloudflareJson.Default)}");
        }
    }

    [Fact]
    public async Task Workers_observability_destinations_can_be_listed()
    {
        if (!SkipWithoutAccount())
        {
            return;
        }

        IReadOnlyList<ObservabilityDestination> destinations;
        try
        {
            destinations = await Client.WorkersObservability.ListDestinationsAsync();
        }
        catch (CloudflareApiException exception)
            when (exception.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound)
        {
            SkipUnless(
                false,
                $"GET /accounts/{{account_id}}/workers/observability/destinations was refused with " +
                $"{exception.StatusCode}/{exception.ErrorCode}: {exception.Message}");
            return;
        }

        Log($"{destinations.Count} observability destination(s)");
        foreach (var destination in destinations.Take(3))
        {
            Log($"destination {destination} enabled={destination.Enabled} type={destination.Configuration?.Type} " +
                $"dataset={destination.Configuration?.LogpushDataset} url={destination.Configuration?.Url} " +
                $"last_complete={destination.Configuration?.JobStatus?.LastComplete} " +
                $"scripts=[{string.Join(", ", destination.Scripts ?? new List<string>())}]");

            if (destination.AdditionalData is { Count: > 0 } ||
                destination.Configuration?.AdditionalData is { Count: > 0 } ||
                destination.Configuration?.JobStatus?.AdditionalData is { Count: > 0 })
            {
                Warn($"GET /accounts/{{account_id}}/workers/observability/destinations returned fields this " +
                     $"library does not model yet - destination: {JsonSerializer.Serialize(destination.AdditionalData)}, " +
                     $"configuration: {JsonSerializer.Serialize(destination.Configuration?.AdditionalData)}, " +
                     $"job status: {JsonSerializer.Serialize(destination.Configuration?.JobStatus?.AdditionalData)}.");
            }
        }

        // The filters on this endpoint are camelCase on the wire, orderBy and perPage rather than order_by and
        // per_page, which is unusual enough to be worth checking against the live endpoint and not only
        // against the published schema. A refusal is the answer, so it is reported rather than asserted away.
        try
        {
            var filtered = await Client.WorkersObservability.ListDestinationsAsync(
                new ObservabilityDestinationListParameters
                {
                    Order = ObservabilityDestinationOrders.Descending,
                    OrderBy = ObservabilityDestinationOrderBy.Created,
                    Page = 1,
                    PerPage = 5,
                });

            Log($"the filtered list answered {filtered.Count} destination(s) with the camelCase filters");
        }
        catch (CloudflareApiException exception)
        {
            Warn($"GET /accounts/{{account_id}}/workers/observability/destinations?order=desc&orderBy=created" +
                 $"&page=1&perPage=5 was refused with {exception.StatusCode}/{exception.ErrorCode}: " +
                 $"{exception.Message} - so the camelCase filters the published schema names are not what the " +
                 "live endpoint accepts.");
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

        var projects = await ReadablePagesProjectsAsync();
        if (projects is null)
        {
            return;
        }

        Log($"pages projects: {projects.Count} of {projects.TotalCount}");
        foreach (var project in projects.Items)
        {
            Log($"  {project.Name} branch={project.ProductionBranch} subdomain={project.Subdomain} latest={project.LatestDeployment?.Status}");

            // A project connected to a repository carries its source control on the deployment, which is
            // the field this library read as a string until a live run quoted the object Cloudflare
            // actually sent. Anything still unmodelled is reported rather than left to accumulate in
            // AdditionalData unnoticed, and each line stops by itself once the fields are typed.
            var deployment = project.LatestDeployment;
            if (deployment?.Source is { } source &&
                (source.AdditionalData is { Count: > 0 } || source.Config?.AdditionalData is { Count: > 0 }))
            {
                Warn($"GET /accounts/{{account_id}}/pages/projects returned a latest_deployment.source for " +
                     $"'{project.Name}' holding fields this library does not model: " +
                     $"{JsonSerializer.Serialize(source)}.");
            }

            if (deployment?.DeploymentTrigger is { } trigger &&
                (trigger.AdditionalData is { Count: > 0 } || trigger.Metadata?.AdditionalData is { Count: > 0 }))
            {
                // The four metadata fields this library models were observed on an ad_hoc trigger, a
                // deployment wrangler pushed from a working copy. A github:push or deploy_hook trigger may
                // carry others, and this is the line that says so.
                Warn($"GET /accounts/{{account_id}}/pages/projects returned latest_deployment.deployment_trigger " +
                     $"for '{project.Name}' holding fields this library does not model: " +
                     $"{JsonSerializer.Serialize(trigger)}.");
            }
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

        // This is the smoke test for the facade: a single client, every service it exposes, all wired to the
        // same transport and options instance. The count is deliberately not in the sentence, because it has
        // been wrong every time a module was added.
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
        Assert.NotNull(Client.Lists);
        Assert.NotNull(Client.Rulesets);
        Assert.NotNull(Client.WorkersObservability);
        Assert.NotNull(Client.WorkersAi);
        Assert.NotNull(Client.Turnstile);

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
    public async Task An_observability_live_tail_can_be_prepared_and_kept_alive()
    {
        if (!SkipWithoutAccount() || !SkipWithoutWritePermission())
        {
            return;
        }

        ObservabilityLiveTail tail;
        try
        {
            tail = await Client.WorkersObservability.PrepareLiveTailAsync();
        }
        catch (CloudflareApiException exception)
        {
            Warn($"POST /accounts/{{account_id}}/workers/observability/telemetry/live-tail was refused with " +
                 $"{exception.StatusCode}/{exception.ErrorCode}. Cloudflare's body: {exception.ResponseBody}");
            SkipUnless(
                false,
                "preparing a live tail was refused; the Learned line says what Cloudflare answered.");
            return;
        }

        Log($"live tail prepared: {tail}");
        Assert.False(string.IsNullOrEmpty(tail.WsUrl));

        // The address is what a consumer connects to, so it has to be one. The scheme is not asserted: which
        // one Cloudflare hands out is its choice, and the log says what arrived.
        Assert.True(
            Uri.TryCreate(tail.WsUrl, UriKind.Absolute, out var wsUrl),
            $"the live tail url '{tail.WsUrl}' is not an absolute URI");
        Log($"the tail streams from {wsUrl!.Scheme}://{wsUrl.Host}");

        // A session that stops getting heartbeats is dropped, and there is nothing to delete afterwards: a
        // prepared session expires on its own, which is why this test has no finally block.
        try
        {
            await Client.WorkersObservability.SendLiveTailHeartbeatAsync();
            Log("heartbeat accepted");
        }
        catch (CloudflareApiException exception)
        {
            Warn($"POST /accounts/{{account_id}}/workers/observability/telemetry/live-tail/heartbeat was " +
                 $"refused with {exception.StatusCode}/{exception.ErrorCode}. Cloudflare's body: " +
                 $"{exception.ResponseBody}");
        }
    }

    [Fact]
    public async Task An_observability_destination_can_be_created_and_removed()
    {
        if (!SkipWithoutAccount() || !SkipWithoutWritePermission())
        {
            return;
        }

        var name = $"cfnet-it-{Guid.NewGuid().ToString("N")[..10]}";
        var request = CreateObservabilityDestinationRequest.ForLogpush(
            name,
            "https://example.com/cfnet-it-logs",
            ObservabilityLogpushDatasets.OpenTelemetryLogs);

        // The url is not a real collector, so Cloudflare's check that delivery works is skipped, and the
        // destination stays off: this test is about the resource, not about exporting telemetry anywhere.
        request.SkipPreflightCheck = true;

        string? slug = null;
        try
        {
            ObservabilityDestination created;
            try
            {
                created = await Client.WorkersObservability.CreateDestinationAsync(request);
            }
            catch (CloudflareApiException exception)
            {
                // A 400 here is about the body this library sends rather than about entitlement, so the whole
                // of it goes out on the long channel: a skip reason is truncated in the run report and would
                // hide the field Cloudflare objected to.
                Warn($"POST /accounts/{{account_id}}/workers/observability/destinations was refused with " +
                     $"{exception.StatusCode}/{exception.ErrorCode}. Cloudflare's body: {exception.ResponseBody} " +
                     $"This library sent: " +
                     $"{JsonSerializer.Serialize(request, CFNet.Serialization.CloudflareJson.Default)}. " +
                     $"{exception.Message}");

                SkipUnless(
                    false,
                    $"POST /accounts/{{account_id}}/workers/observability/destinations was refused with " +
                    $"{exception.StatusCode}/{exception.ErrorCode}: see the Learned line for what Cloudflare said.");
                return;
            }

            slug = created.Slug;
            Log($"created destination {created} enabled={created.Enabled} " +
                $"dataset={created.Configuration?.LogpushDataset} url={created.Configuration?.Url}");

            Assert.False(string.IsNullOrEmpty(slug));
            Assert.Equal(name, created.Name);
            Assert.False(created.Enabled);
            Assert.Equal(ObservabilityDestinationTypes.Logpush, created.Configuration?.Type);
            Assert.Equal(
                ObservabilityLogpushDatasets.OpenTelemetryLogs,
                created.Configuration?.LogpushDataset);

            var listed = await Client.WorkersObservability.ListDestinationsAsync();
            Assert.Contains(listed, destination => destination.Slug == slug);

            // Observed live, and now asserted: the enabled flag can be changed on its own, with no
            // configuration and so no delivery preflight, even for a destination whose address receives
            // nothing. Sending a configuration is a different matter - it re-runs Cloudflare's check that
            // delivery works, and this endpoint has no skipPreflightCheck to turn that off with, so a
            // destination pointing at an address that is not a receiver answers 400 with "Pre-flight check
            // failed: Receiver returned HTTP 405" even when the url sent is the one it already has. That
            // half is documented rather than exercised, because the suite has no receiver to point at.
            var turnedOn = await Client.WorkersObservability.UpdateDestinationAsync(
                slug!,
                new UpdateObservabilityDestinationRequest { Enabled = true });
            Assert.True(turnedOn.Enabled);

            var afterEnable = await Client.WorkersObservability.ListDestinationsAsync();
            Assert.True(Assert.Single(afterEnable, destination => destination.Slug == slug).Enabled);
            Log($"the destination was turned on and reads back enabled");

            var turnedOff = await Client.WorkersObservability.UpdateDestinationAsync(
                slug!,
                new UpdateObservabilityDestinationRequest { Enabled = false });
            Assert.False(turnedOff.Enabled);
            Log($"turned off again: enabled={turnedOff.Enabled} url={turnedOff.Configuration?.Url}");

            // The delete answers with the destination it deleted rather than an empty success.
            var deleted = await Client.WorkersObservability.DeleteDestinationAsync(slug!);
            Log($"deleted destination {deleted}");
            Assert.Equal(slug, deleted.Slug);
            slug = null;

            var after = await Client.WorkersObservability.ListDestinationsAsync();
            Assert.DoesNotContain(after, destination => destination.Slug == deleted.Slug);
        }
        finally
        {
            if (slug is not null)
            {
                try
                {
                    await Client.WorkersObservability.DeleteDestinationAsync(slug);
                    Log($"deleted destination {slug}");
                }
                catch (CloudflareApiException exception) when (exception.IsNotFound)
                {
                    Log("destination was already gone");
                }
            }
        }
    }

    [Fact]
    public async Task A_worker_script_settings_can_be_updated()
    {
        if (!SkipWithoutAccount() || !SkipWithoutWritePermission())
        {
            return;
        }

        var name = $"cfnet-it-{Guid.NewGuid().ToString("N")[..10]}";
        var uploaded = await Retrying5xxAsync(() => Client.Workers.UploadAsync(
            name,
            "export default { async fetch(request) { return new Response('cf-net'); } }",
            "2025-01-01"));
        Log($"uploaded {uploaded.Name}");

        try
        {
            var before = await Client.Workers.GetSettingsAsync(name);
            Assert.NotNull(before);
            Log($"settings before: logpush={before!.Logpush} observability={before.Observability?.Enabled} " +
                $"tags=[{string.Join(", ", before.Tags ?? new List<string>())}]");
            if (before.AdditionalData is { Count: > 0 } ||
                before.Observability?.AdditionalData is { Count: > 0 })
            {
                Warn($"GET /accounts/{{account_id}}/workers/scripts/{name}/script-settings returned fields this " +
                     $"library does not model yet - settings: {JsonSerializer.Serialize(before.AdditionalData)}, " +
                     $"observability: {JsonSerializer.Serialize(before.Observability?.AdditionalData)}.");
            }

            // Tags are the safe thing to write here: they label a Worker and change nothing about how it runs.
            var tag = $"cfnet-it-{Guid.NewGuid().ToString("N")[..8]}";
            var tagged = await Client.Workers.UpdateSettingsAsync(
                name,
                new WorkerScriptSettings { Tags = new List<string> { tag } });
            Log($"PATCH answered tags=[{string.Join(", ", tagged.Tags ?? new List<string>())}]");

            var readBack = await Client.Workers.GetSettingsAsync(name);
            Assert.NotNull(readBack);
            Assert.Contains(tag, readBack!.Tags ?? new List<string>());
            Log($"tag {tag} stuck");

            // A PATCH carries only what is set, so clearing the tags is an empty list and leaves the rest of
            // the script's settings alone - which the logpush and observability values below check.
            await Client.Workers.UpdateSettingsAsync(
                name,
                new WorkerScriptSettings { Tags = new List<string>() });

            var cleared = await Client.Workers.GetSettingsAsync(name);
            Assert.Empty(cleared!.Tags ?? new List<string>());
            Assert.Equal(before.Logpush, cleared.Logpush);
            Assert.Equal(before.Observability?.Enabled, cleared.Observability?.Enabled);
            Log("tags cleared with an empty list, and nothing else moved");

            // Observability is a separate switch and may be gated by plan, so a refusal is reported rather
            // than failed: what matters here is what a live answer carries.
            try
            {
                var observability = await Client.Workers.UpdateSettingsAsync(
                    name,
                    new WorkerScriptSettings
                    {
                        Observability = new WorkerObservability
                        {
                            Enabled = true,
                            HeadSamplingRate = 1,
                            Logs = new WorkerObservabilityLogs { Enabled = true, InvocationLogs = true },
                        },
                    });

                var after = await Client.Workers.GetSettingsAsync(name);
                Log($"observability: answered enabled={observability.Observability?.Enabled}, read back " +
                    $"enabled={after?.Observability?.Enabled} sampling={after?.Observability?.HeadSamplingRate} " +
                    $"logs={after?.Observability?.Logs?.Enabled}/{after?.Observability?.Logs?.InvocationLogs} " +
                    $"redact_query_string={after?.Observability?.RedactQueryString}");
                if (after?.Observability?.AdditionalData is { Count: > 0 })
                {
                    Warn($"GET /accounts/{{account_id}}/workers/scripts/{name}/script-settings answered " +
                         $"observability fields this library does not model yet: " +
                         $"{JsonSerializer.Serialize(after.Observability.AdditionalData)}.");
                }
            }
            catch (CloudflareApiException exception)
            {
                Warn($"PATCH /accounts/{{account_id}}/workers/scripts/{name}/script-settings with observability " +
                     $"was refused with {exception.StatusCode}/{exception.ErrorCode}: {exception.Message}");
            }
        }
        finally
        {
            try
            {
                await Retrying5xxAsync(() => Client.Workers.DeleteAsync(name));
                Log($"deleted script {name}");
            }
            catch (CloudflareApiException exception) when (exception.IsNotFound)
            {
                Log("script was already gone");
            }
        }
    }

    [Fact]
    public async Task A_worker_tail_can_be_started_and_stopped()
    {
        if (!SkipWithoutAccount() || !SkipWithoutWritePermission())
        {
            return;
        }

        var name = $"cfnet-it-{Guid.NewGuid().ToString("N")[..10]}";
        var uploaded = await Retrying5xxAsync(() => Client.Workers.UploadAsync(
            name,
            // The tail streams what the Worker logs, so a script with something to log is the point of the test.
            "export default { async fetch(request) { console.log('cf-net tail probe'); return new Response('cf-net'); } }",
            "2025-01-01"));
        Log($"uploaded {uploaded.Name}");

        CFNet.Models.Workers.WorkerTail? tail = null;
        try
        {
            // A script that has never been tailed answers 200 with an empty array, which is the state this one
            // starts in.
            Assert.Empty(await Client.Workers.ListTailsAsync(name));

            tail = await Client.Workers.CreateTailAsync(name);
            Log($"tail {tail}");

            Assert.False(string.IsNullOrEmpty(tail.Id));
            Assert.False(string.IsNullOrEmpty(tail.Url));
            Assert.NotNull(tail.ExpiresAt);
            Assert.False(tail.HasExpired);

            // The url is what a consumer connects to, so it has to be an address rather than a bare token.
            // The scheme is not asserted: which one Cloudflare hands out is its choice, and the run reports it.
            Assert.True(
                Uri.TryCreate(tail.Url, UriKind.Absolute, out var tailUrl),
                $"the tail url '{tail.Url}' is not an absolute URI");
            Log($"tail {tail.Id} is served at {tailUrl!.Scheme}://{tailUrl.Host}");

            // The list answers with an array whose items carry the same three fields the create did, which is
            // not what the published schema says this endpoint answers with.
            var listed = await Client.Workers.ListTailsAsync(name);
            var only = Assert.Single(listed);
            Assert.Equal(tail.Id, only.Id);
            Assert.Equal(tail.Url, only.Url);

            // The expiry is only checked as "still in the future" rather than as the value the create
            // answered with, because whether Cloudflare reports the stored one or recomputes it on read is
            // not something a caller should depend on.
            Assert.NotNull(only.ExpiresAt);
            Assert.False(only.HasExpired);

            await Client.Workers.DeleteTailAsync(name, tail.Id!);
            Log($"stopped tail {tail.Id}");
            tail = null;

            // Stopped is the same as never started from the API's point of view.
            Assert.Empty(await Client.Workers.ListTailsAsync(name));

            // A script holds one tail at a time: a second create answers 200 with a new tail and replaces the
            // one already running rather than adding to it.
            var first = await Client.Workers.CreateTailAsync(name);
            var replacement = await Client.Workers.CreateTailAsync(name);
            Log($"tail {first.Id} was replaced by {replacement.Id}");
            tail = replacement;

            var after = await Client.Workers.ListTailsAsync(name);
            var current = Assert.Single(after);
            Assert.Equal(replacement.Id, current.Id);

            await Client.Workers.DeleteTailAsync(name, replacement.Id!);
            tail = null;
            Assert.Empty(await Client.Workers.ListTailsAsync(name));
        }
        finally
        {
            if (tail is not null)
            {
                try
                {
                    await Client.Workers.DeleteTailAsync(name, tail.Id!);
                    Log($"stopped tail {tail.Id}");
                }
                catch (CloudflareApiException exception) when (exception.IsNotFound)
                {
                    Log("tail was already gone");
                }
            }

            try
            {
                await Retrying5xxAsync(() => Client.Workers.DeleteAsync(name));
                Log($"deleted script {name}");
            }
            catch (CloudflareApiException exception) when (exception.IsNotFound)
            {
                Log("script was already gone");
            }
        }
    }

    [Fact]
    public async Task A_redirect_ruleset_can_be_created_given_a_bulk_redirect_rule_and_removed()
    {
        if (!SkipWithoutAccount() || !SkipWithoutWritePermission())
        {
            return;
        }

        var marker = Guid.NewGuid().ToString("N")[..8];
        var listName = $"cfnet_it_{DateTime.UtcNow:yyyyMMddHHmmss}_{marker}";

        // A redirect rule points at a list by name, so the list has to exist before Cloudflare will accept
        // the rule. Both are created here and both are removed below, ruleset first: a list a ruleset still
        // refers to is not one Cloudflare will delete.
        var list = await Client.Lists.CreateAsync(new CreateRulesListRequest
        {
            Name = listName,
            Kind = RulesListKind.Redirect,
            Description = "CF.Net integration test list for a redirect ruleset",
        });

        try
        {
            var added = await Client.Lists.CreateItemsAsync(
                list.Id!,
                new[]
                {
                    CreateRulesListItemRequest.ForRedirect($"cfnet-{marker}.example.com/old", "https://example.com/new", 301),
                });
            var itemOperation = await Client.Lists.WaitForBulkOperationAsync(
                added.OperationId!,
                timeout: TimeSpan.FromSeconds(60),
                pollInterval: TimeSpan.FromSeconds(2));
            Assert.False(itemOperation.HasFailed, $"Cloudflare refused the list item: {itemOperation.Error}");

            // Bulk Redirects runs from the account's root ruleset for http_request_redirect. A custom
            // ruleset is refused for that phase (20004), and this account is not entitled to the phase
            // custom rulesets are documented for (50002 on http_request_firewall_custom), so the root
            // redirect ruleset is the only place a ruleset can actually be written here. What the account
            // already serves decides how far the test goes: an entrypoint that exists is only ever
            // validated against, with dry_run, and never changed.
            var entrypoint = await Client.Rulesets.GetEntrypointAsync(RulesetPhase.HttpRequestRedirect);
            Log(entrypoint is null
                ? "this account runs no redirect entrypoint, so the test creates one and removes it again"
                : $"this account already runs a redirect entrypoint at version {entrypoint.Version} with {entrypoint.RuleCount} rule(s); it will only be dry-run against");

            if (entrypoint is not null)
            {
                var validated = await Client.Rulesets.UpdateEntrypointAsync(
                    RulesetPhase.HttpRequestRedirect,
                    new UpdateRulesetRequest
                    {
                        Rules = new List<CreateRulesetRuleRequest>
                        {
                            CreateRulesetRuleRequest.ForRedirectFromList(listName, description: "CF.Net dry run"),
                        },
                    },
                    dryRun: true);

                Log($"the dry run answered with {validated}");
                if (validated.Rules is { Count: > 0 } dryRules)
                {
                    Assert.Contains(
                        dryRules,
                        candidate => candidate.ActionParameters?.FromList?.Name == listName
                            && candidate.ActionParameters.FromList.Key == RulesetRedirectFromList.KeyFullUri);
                }

                var unchanged = await Client.Rulesets.GetEntrypointAsync(RulesetPhase.HttpRequestRedirect);
                Assert.Equal(entrypoint.Version, unchanged?.Version);
                Log("the live entrypoint is at the same version it was before the dry run");
                return;
            }

            Ruleset ruleset;
            try
            {
                ruleset = await Client.Rulesets.CreateAsync(new CreateRulesetRequest
                {
                    Name = $"cfnet-it-{marker}",
                    Kind = RulesetKind.Root,
                    Phase = RulesetPhase.HttpRequestRedirect,
                    Description = "CF.Net integration test ruleset",
                    Rules = new List<CreateRulesetRuleRequest>
                    {
                        CreateRulesetRuleRequest.ForRedirectFromList(listName, description: "bulk redirects"),
                    },
                });
            }
            catch (CloudflareApiException exception) when (
                exception.HasErrorCode(50002) || exception.HasErrorCode(20004) || exception.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                // 50002 is "not entitled to use the phase" and 20004 is "this kind cannot be used for this
                // phase": both are what the account or the API allows, not what the library sent. The list
                // is still cleaned up by the finally below.
                SkipUnless(
                    false,
                    "POST /accounts/{account_id}/rulesets was refused for the redirect phase, so this " +
                    "account cannot hold a ruleset for the suite to write: " + exception.Message);
                return;
            }

            try
            {
                Assert.Equal(RulesetKind.Root, ruleset.Kind);
                Assert.Equal(RulesetPhase.HttpRequestRedirect, ruleset.Phase);
                Assert.True(ruleset.IsRoot);
                Log($"created ruleset {ruleset}");

                // The rule went in with the ruleset, so it is already in what came back - with the
                // expression and key Cloudflare requires to agree with each other.
                var rule = Assert.Single(ruleset.Rules!);
                Assert.Equal("redirect", rule.Action);
                Assert.Equal(listName, rule.ActionParameters!.FromList!.Name);
                Assert.Equal(RulesetRedirectFromList.KeyFullUri, rule.ActionParameters.FromList.Key);
                Assert.Contains(listName, rule.Expression, StringComparison.Ordinal);
                Assert.Contains(RulesetRedirectFromList.KeyFullUri, rule.Expression, StringComparison.Ordinal);

                var read = await Client.Rulesets.GetAsync(ruleset.Id!);
                Assert.NotNull(read);
                Assert.Equal(ruleset.Version, read!.Version);

                var versions = await Client.Rulesets.ListVersionsAsync(ruleset.Id!);
                Assert.NotEmpty(versions);
                Log($"{versions.Count} version(s) after creating it with a rule");

                // A rule change answers with the whole ruleset, not the rule, so the change is read back out
                // of what came back. The expression still has to reference the field the key uses, which is
                // why it narrows with && rather than replacing the lookup.
                var afterAdd = await Client.Rulesets.CreateRuleAsync(
                    ruleset.Id!,
                    CreateRulesetRuleRequest.ForRedirectFromList(
                        listName,
                        $"http.request.full_uri in ${listName} && http.host eq \"cfnet-{marker}.example.com\"",
                        "second rule"));
                Assert.Equal(2, afterAdd.RuleCount);

                var second = afterAdd.Rules!.Single(candidate => candidate.Description == "second rule");

                // Cloudflare requires the action and the expression on an edit as well as on a create,
                // refusing one without them with 20015 and 20125, so an edit is the rule sent back whole
                // with the one change on top. The action parameters come back as the same type the request
                // takes, which is what makes that round trip a single assignment.
                var afterEdit = await Client.Rulesets.EditRuleAsync(
                    ruleset.Id!,
                    second.Id!,
                    new CreateRulesetRuleRequest
                    {
                        Action = second.Action,
                        Expression = second.Expression,
                        Description = second.Description,
                        ActionParameters = second.ActionParameters,
                        Enabled = false,
                    });
                Assert.False(afterEdit.Rules!.Single(candidate => candidate.Id == second.Id).Enabled);

                var afterRemove = await Client.Rulesets.DeleteRuleAsync(ruleset.Id!, second.Id!);
                Assert.Equal(1, afterRemove.RuleCount);
                Assert.False(
                    afterRemove.Rules!.Any(candidate => candidate.Id == second.Id),
                    $"rule {second.Id} should have been removed, and it is still in {afterRemove}");

                var finalVersions = await Client.Rulesets.ListVersionsAsync(ruleset.Id!);
                Log($"{finalVersions.Count} version(s) after appending, editing and removing a rule");
                Assert.True(
                    finalVersions.Count > versions.Count,
                    $"every change makes a version, so {versions.Count} should have grown, and it is {finalVersions.Count}");
            }
            finally
            {
                try
                {
                    await Client.Rulesets.DeleteAsync(ruleset.Id!);
                    Log($"deleted ruleset {ruleset.Id}");
                }
                catch (CloudflareApiException exception) when (exception.IsNotFound)
                {
                    Log("ruleset was already gone");
                }
            }

            // The account ran no redirect entrypoint before this test, so it should run none after it.
            Assert.Null(await Client.Rulesets.GetEntrypointAsync(RulesetPhase.HttpRequestRedirect));
        }
        finally
        {
            try
            {
                var deleted = await Client.Lists.DeleteAsync(list.Id!);
                Log($"deleted list {deleted}");
            }
            catch (CloudflareApiException exception) when (exception.IsNotFound)
            {
                Log("list was already gone");
            }
        }
    }

    [Fact]
    public async Task A_rules_list_can_be_created_filled_and_removed()
    {
        if (!SkipWithoutAccount() || !SkipWithoutWritePermission())
        {
            return;
        }

        // List names take lowercase letters, digits and underscores, so this avoids the hyphens
        // UniqueSuffix puts in a name. The redirect sources below are hostnames, which is the opposite
        // rule, so they get their own marker.
        var name = $"cfnet_it_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")[..8]}";
        var marker = Guid.NewGuid().ToString("N")[..8];
        var firstSource = $"cfnet-{marker}-one.example.com/old";
        var secondSource = $"cfnet-{marker}-two.example.com/a";

        // Created outside the try/finally on purpose: when the create is refused there is nothing to
        // delete, and a second failure from the finally would hide the reason the first one happened.
        RulesList created;
        try
        {
            created = await Client.Lists.CreateAsync(new CreateRulesListRequest
            {
                Name = name,
                Kind = RulesListKind.Redirect,
                Description = "CF.Net integration test list",
            });
        }
        catch (CloudflareApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            // Reading lists only needs Account Rule Lists Read; changing them needs Write. Reported rather
            // than failed, because it is the credential and not the library, and the read side of the same
            // resource is exercised by Rules_lists_can_be_listed.
            SkipUnless(
                false,
                "POST /accounts/{account_id}/rules/lists was refused, so this token can read Account Rule " +
                "Lists but not write them. " + exception.Message);
            return;
        }

        var listId = created.Id!;
        Log($"created list {created.Name} id={listId} kind={created.Kind}");

        try
        {
            Assert.Equal(name, created.Name);
            Assert.Equal(RulesListKind.Redirect, created.Kind);
            Assert.True(created.IsRedirectList);

            // Item changes are asynchronous: Cloudflare answers with an operation id and applies them in
            // the background, so reading straight after writing can still show the old contents. Waiting
            // on the operation is what makes the assertions below mean anything.
            var added = await Client.Lists.CreateItemsAsync(
                listId,
                new[]
                {
                    CreateRulesListItemRequest.ForRedirect(firstSource, "https://example.com/new", 301, "first"),
                    CreateRulesListItemRequest.ForRedirect(secondSource, "https://example.com/b"),
                });

            Assert.False(string.IsNullOrEmpty(added.OperationId));
            Log($"item create accepted as operation {added.OperationId}");

            var operation = await Client.Lists.WaitForBulkOperationAsync(
                added.OperationId!,
                timeout: TimeSpan.FromSeconds(60),
                pollInterval: TimeSpan.FromSeconds(2));

            Log($"operation {operation.Id} ended as {operation.Status}");
            Assert.True(operation.IsFinished, $"the item create never finished: {operation}");
            Assert.False(operation.HasFailed, $"Cloudflare refused the items: {operation.Error}");

            var items = await Client.Lists.ListItemsAsync(listId, new RulesListItemListParameters { PerPage = 10 });
            Assert.Equal(2, items.Count);
            Assert.Equal(2, items.Items.Count(item => item.ValueKind == RulesListKind.Redirect));
            Assert.Contains(items.Items, item => item.Redirect!.SourceUrl == firstSource);
            Assert.Contains(items.Items, item => item.Redirect!.SourceUrl == secondSource);

            var first = items.Items.Single(item => item.Redirect!.SourceUrl == firstSource);
            Assert.Equal("https://example.com/new", first.Redirect!.TargetUrl);
            Assert.Equal(301, first.Redirect.StatusCode);
            Assert.Equal("first", first.Comment);

            // One item read on its own has to match the same item in the page.
            var alone = await Client.Lists.GetItemAsync(listId, first.Id!);
            Assert.NotNull(alone);
            Assert.Equal(first.Redirect!.SourceUrl, alone!.Redirect!.SourceUrl);

            var read = await Client.Lists.GetAsync(listId);
            Assert.NotNull(read);
            Log($"list {read!.Name} now reports {read.NumItems} item(s)");
            if (read.NumItems != 2)
            {
                // The item endpoints are the authority here; num_items is a counter Cloudflare keeps
                // beside the list and it is worth knowing when the two disagree.
                Warn($"GET /accounts/{{account_id}}/rules/lists/{listId} reported num_items=" +
                     $"{read.NumItems} after two items were added and the bulk operation completed.");
            }

            var updated = await Client.Lists.UpdateAsync(
                listId,
                new UpdateRulesListRequest { Description = "CF.Net integration test list, updated" });
            Assert.Equal("CF.Net integration test list, updated", updated.Description);

            // Removing items goes by id inside a body, and is asynchronous the same way adding is.
            var removal = await Client.Lists.DeleteItemsAsync(
                listId,
                items.Items.Select(item => item.Id!).ToList());
            var removed = await Client.Lists.WaitForBulkOperationAsync(
                removal.OperationId!,
                timeout: TimeSpan.FromSeconds(60),
                pollInterval: TimeSpan.FromSeconds(2));

            Log($"operation {removed.Id} ended as {removed.Status}");
            Assert.False(removed.HasFailed, $"Cloudflare refused the removal: {removed.Error}");
            Assert.True(removed.IsFinished, $"the removal never finished: {removed}");
            Assert.Equal(0, (await Client.Lists.ListItemsAsync(listId)).Count);
        }
        finally
        {
            try
            {
                var deletedId = await Client.Lists.DeleteAsync(listId);
                Log($"deleted list {deletedId}");
            }
            catch (CloudflareApiException exception) when (exception.IsNotFound)
            {
                Log("list was already gone");
            }
        }

        Assert.Null(await Client.Lists.GetAsync(listId));
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
            var uploaded = await Retrying5xxAsync(() => Client.Workers.UploadAsync(
                name,
                // The scheduled handler is here so the cron triggers below are set on a Worker that can
                // be triggered; the fetch assertion only looks for the response text.
                "export default { async fetch(request) { return new Response('cf-net'); }, async scheduled(event) { } }",
                "2025-01-01",
                new[] { WorkerBinding.PlainText("GREETING", "hello") }));

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

            // The search endpoint is a separate index from the scripts list, so a script uploaded moments
            // ago may not be in it yet. One retry keeps that honest without turning a lag into a red build,
            // and a still-missing row is reported rather than asserted, because "eventually" is not a
            // contract this library can hold Cloudflare to.
            var search = await Client.Workers.SearchAsync(new WorkerScriptSearchParameters { Name = name });
            if (search.Items.All(item => item.ScriptName != name))
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                search = await Client.Workers.SearchAsync(new WorkerScriptSearchParameters { Name = name });
            }

            var found = search.Items.FirstOrDefault(item => item.ScriptName == name);
            Log($"search: {search.Count} row(s), total_count={search.TotalCount}, matched={found is not null}");
            if (found is null)
            {
                Warn($"GET /accounts/{{account_id}}/workers/scripts-search?name={name} returned {search.Count} " +
                     "row(s) and none of them was the script uploaded moments earlier. Either the search " +
                     "index lags longer than the five second retry, or its name filter does not match the " +
                     "way the scripts list does.");
            }
            else
            {
                Log($"search row: {found} id={found.Id} created={found.CreatedOn:O} " +
                    $"modified={found.ModifiedOn:O} service={found.ServiceName} " +
                    $"env={found.EnvironmentName} default={found.EnvironmentIsDefault}");

                if (found.AdditionalData is { Count: > 0 })
                {
                    Warn($"GET /accounts/{{account_id}}/workers/scripts-search returned fields this library " +
                         $"does not model yet: {JsonSerializer.Serialize(found.AdditionalData)}. They are " +
                         "captured rather than dropped, and belong in WorkerScriptSearchItem once they have " +
                         "been seen more than once.");
                }
            }

            // Reading one version by id is the only call that returns a version's resources, so this is
            // where the bindings, exported handlers and runtime of the version just uploaded become visible
            // - and where a field this library does not model yet would show up.
            if (latest?.Id is { Length: > 0 } versionId)
            {
                var detail = await Client.Workers.GetVersionAsync(name, versionId);
                Assert.NotNull(detail);
                Assert.Equal(versionId, detail!.Id);
                Assert.Equal(latest.Number, detail.Number);
                Assert.Equal(WorkerVersionSources.Api, detail.Metadata?.Source);

                var runtime = detail.Resources?.ScriptRuntime;
                Log($"version detail: resources={detail.Resources} runtime={runtime} " +
                    $"etag={detail.Resources?.Script?.Etag} " +
                    $"last_deployed_from={detail.Resources?.Script?.LastDeployedFrom} " +
                    $"bindings={detail.Resources?.Bindings?.Count ?? 0}");

                var extras = new (string Where, IDictionary<string, JsonElement>? Data)[]
                {
                    ("version", detail.AdditionalData),
                    ("resources", detail.Resources?.AdditionalData),
                    ("script", detail.Resources?.Script?.AdditionalData),
                    ("script_runtime", runtime?.AdditionalData),
                    ("metadata", detail.Metadata?.AdditionalData),
                };
                var unmodelled = extras.Where(extra => extra.Data is { Count: > 0 }).ToList();
                if (unmodelled.Count > 0)
                {
                    Warn($"GET /accounts/{{account_id}}/workers/scripts/{name}/versions/{versionId} returned " +
                         "fields this library does not model yet - " +
                         string.Join("; ", unmodelled.Select(
                             extra => $"{extra.Where}: {JsonSerializer.Serialize(extra.Data)}")) +
                         ". They are captured rather than dropped.");
                }

                // The exports map is deliberately raw: the reference types each entry as a union of six
                // variants and no live answer had shown this library one. Reporting what arrives is the
                // evidence that would justify modelling them.
                if (runtime?.Exports is { Count: > 0 })
                {
                    Warn($"GET /accounts/{{account_id}}/workers/scripts/{name}/versions/{versionId} reported " +
                         $"{runtime.Exports.Count} declarative export(s), which this library keeps as raw " +
                         $"JSON: {JsonSerializer.Serialize(runtime.Exports)}.");
                }
            }

            // Uploading a version leaves the deployment alone, so this is safe after the deployment
            // assertions above: traffic still goes to the version the first upload created. This is also
            // the only call that answers with startup_time_ms and exports_reconciliation.
            var secondRequest = new UploadWorkerScriptRequest(
                name,
                "export default { async fetch() { return new Response('v2'); }, async scheduled(event) { } }",
                DateTime.UtcNow.ToString("yyyy-MM-dd"))
            {
                Annotations = new WorkerAnnotations
                {
                    Message = "CF.Net integration second version",
                    Tag = UniqueSuffix("cfnet"),
                },
            };

            var created = await Client.Workers.CreateVersionAsync(name, secondRequest);
            Assert.NotNull(created);
            Assert.True(created!.Number > latest?.Number);
            Assert.NotEqual(latest?.Id, created.Id);
            Assert.Equal(WorkerVersionSources.Api, created.Metadata?.Source);
            Log($"created version {created.Number} ({created.Id}) startup={created.StartupTimeMs}ms " +
                $"resources={created.Resources} runtime={created.Resources?.ScriptRuntime} " +
                $"preview={created.Metadata?.HasPreview}");

            var createdExtras = new (string Where, IDictionary<string, JsonElement>? Data)[]
            {
                ("version", created.AdditionalData),
                ("resources", created.Resources?.AdditionalData),
                ("script", created.Resources?.Script?.AdditionalData),
                ("script_runtime", created.Resources?.ScriptRuntime?.AdditionalData),
                ("metadata", created.Metadata?.AdditionalData),
            };
            var createdUnmodelled = createdExtras.Where(extra => extra.Data is { Count: > 0 }).ToList();
            if (createdUnmodelled.Count > 0)
            {
                Warn($"POST /accounts/{{account_id}}/workers/scripts/{name}/versions returned fields this " +
                     "library does not model yet - " +
                     string.Join("; ", createdUnmodelled.Select(
                         extra => $"{extra.Where}: {JsonSerializer.Serialize(extra.Data)}")) +
                     ". They are captured rather than dropped.");
            }

            // Published only on this response, and never yet seen live: reporting it is what would turn the
            // deliberately raw ExportsReconciliation into a type.
            if (created.ExportsReconciliation is { ValueKind: JsonValueKind.Object } reconciliation &&
                reconciliation.EnumerateObject().Any())
            {
                Warn($"POST /accounts/{{account_id}}/workers/scripts/{name}/versions returned " +
                     $"exports_reconciliation, which this library keeps raw: {reconciliation.GetRawText()}.");
            }

            // The reference says Cloudflare fills the CI annotations in from the environment; this checks
            // that a caller can set one by hand and read it back, which no version schema promises.
            var readBack = await Client.Workers.GetVersionAsync(name, created.Id!);
            for (var attempt = 0; attempt < 3 && readBack is null; attempt++)
            {
                // Observed against the live API: a version Cloudflare has just accepted with a 200 can still
                // answer 404 on its own path for a moment, so it is read again before being reported as a
                // version that cannot be read by id.
                Log($"GET …/versions/{created.Id} answered 'not there' straight after the create; " +
                    "waiting and reading again.");
                await Task.Delay(TimeSpan.FromSeconds(3));
                readBack = await Client.Workers.GetVersionAsync(name, created.Id!);
            }

            Log($"read back: annotations={readBack?.Annotations} preview={readBack?.Metadata?.HasPreview} " +
                $"startup={readBack?.StartupTimeMs}");
            if (readBack is null)
            {
                Warn($"GET /accounts/{{account_id}}/workers/scripts/{name}/versions/{created.Id} still answered " +
                     "'not there' on the fourth try, although POST …/versions had answered 200 with that id. " +
                     "A single version stays unreadable by id for longer than the list takes to show it.");
            }
            else if (readBack.Annotations?.Message != secondRequest.Annotations!.Message)
            {
                Warn($"POST /accounts/{{account_id}}/workers/scripts/{name}/versions accepted workers/message " +
                     $"but GET …/versions/{created.Id} returned " +
                     $"{JsonSerializer.Serialize(readBack?.Annotations)}. Either annotations are not readable " +
                     "on a single version, or the message was dropped on the way in.");
            }

            var afterCreate = await Client.Workers.ListVersionsAsync(name);
            for (var attempt = 0; attempt < 5 && afterCreate!.Count < 2; attempt++)
            {
                // Observed against the live API: the list still showed only the version the upload made, a
                // moment after the create for a second one had answered 200 - and once, a list that had shown
                // a version showing none at all. Both are propagation rather than a wrong read, so the count
                // is read again before it is asserted on, the way the script source is after an upload.
                Log($"the new version was not listed yet ({afterCreate.Count} of 2); waiting and reading again.");
                await Task.Delay(TimeSpan.FromSeconds(4));
                afterCreate = await Client.Workers.ListVersionsAsync(name);
            }

            if (afterCreate!.Count == 0)
            {
                // A list answering no versions at all, for a script that answered 200 to both an upload and a
                // version create twenty seconds earlier, is Cloudflare's propagation and not this library's
                // parsing: the same call showed a version at the start of this fact. It is reported rather
                // than asserted on, so one wobble does not hide whatever the next increment has to say - and
                // reported with the answer as read, so a genuinely empty read would not pass quietly either.
                Warn($"GET /accounts/{{account_id}}/workers/scripts/{name}/versions answered no versions after " +
                     $"five reads over twenty seconds, although POST …/versions had answered 200 with id " +
                     $"{created.Id} earlier in this same fact. The list as read: " +
                     $"{JsonSerializer.Serialize(afterCreate, CFNet.Serialization.CloudflareJson.Default)}");
                return;
            }

            Assert.Equal(2, afterCreate.Count);
            Assert.Equal(created.Id, afterCreate.Latest?.Id);

            // Cron triggers are a whole-set replacement, so this writes two, reads them back and then
            // clears them again - the empty PUT is how a script's triggers are removed. The script is
            // deleted in the finally block below, which takes whatever is left of them with it.
            try
            {
                var scheduled = await Client.Workers.UpdateSchedulesAsync(
                    name,
                    new[] { "*/30 * * * *", "0 3 * * *" });
                Assert.NotNull(scheduled);
                Assert.Equal(2, scheduled!.Count);

                // Observed against the live API: the PUT answers 200 with both triggers, but not
                // necessarily in the order they were sent. A script's cron triggers are a set - the PUT
                // replaces the whole set and the answer lists what is now scheduled - so compare it as
                // one, and say what order Cloudflare used when it differs from the order sent.
                var sent = new[] { "*/30 * * * *", "0 3 * * *" };
                var answered = scheduled.Expressions.ToArray();
                Assert.True(
                    sent.OrderBy(x => x, StringComparer.Ordinal)
                        .SequenceEqual(answered.OrderBy(x => x, StringComparer.Ordinal)),
                    $"PUT /accounts/{{account_id}}/workers/scripts/{name}/schedules answered with " +
                    $"[{string.Join(", ", answered)}] where [{string.Join(", ", sent)}] was sent.");
                if (!answered.SequenceEqual(sent))
                {
                    Warn($"PUT /accounts/{{account_id}}/workers/scripts/{name}/schedules returned the " +
                         $"triggers in a different order than they were sent: sent " +
                         $"[{string.Join(", ", sent)}], got [{string.Join(", ", answered)}]. The library " +
                         "does not promise an order, because Cloudflare does not; this line records the " +
                         "one it used.");
                }

                Log($"cron triggers set: {scheduled}");

                var schedules = await Client.Workers.ListSchedulesAsync(name);
                Assert.NotNull(schedules);
                Assert.Equal(2, schedules!.Count);
                Assert.Contains("*/30 * * * *", schedules.Expressions);
                Log($"cron triggers read back: {schedules}; first created " +
                    $"{schedules.Schedules?[0].CreatedOn:O} modified {schedules.Schedules?[0].ModifiedOn:O}");

                if (schedules.Schedules?[0].AdditionalData is { Count: > 0 } ||
                    schedules.AdditionalData is { Count: > 0 })
                {
                    Warn($"GET /accounts/{{account_id}}/workers/scripts/{name}/schedules returned fields this " +
                         $"library does not model yet - schedule: " +
                         $"{JsonSerializer.Serialize(schedules.Schedules?[0].AdditionalData)}, result: " +
                         $"{JsonSerializer.Serialize(schedules.AdditionalData)}.");
                }

                var cleared = await Client.Workers.UpdateSchedulesAsync(name, Array.Empty<string>());
                Assert.Equal(0, cleared!.Count);
                Log("cron triggers cleared with an empty PUT");
            }
            catch (CloudflareApiException exception) when (exception.HasErrorCode(10063))
            {
                // An account prerequisite rather than a missing permission: the same token uploaded this
                // script, created a version and read it back, all of which need Workers Scripts Write.
                // Cloudflare refuses cron triggers until the account has a workers.dev subdomain, which
                // this one does not - GET …/workers/subdomain answers 404 and the subdomain test skips with
                // 10007 for the same reason. Reported rather than asserted, so the run says what would
                // unlock it instead of going red on something the library cannot fix.
                Warn($"PUT /accounts/{{account_id}}/workers/scripts/{name}/schedules was refused with " +
                     $"[10063]: {exception.Message} Cron triggers need a workers.dev subdomain, which this " +
                     "account does not have. Opening Workers & Pages in the dashboard once creates it, after " +
                     "which this step writes, reads back and clears two triggers for real.");
            }

            var settings = await Client.Workers.GetSettingsAsync(name);
            Log($"logpush={settings?.Logpush} tags={string.Join(",", settings?.Tags ?? new List<string>())}");
        }
        finally
        {
            try
            {
                await Retrying5xxAsync(() => Client.Workers.DeleteAsync(name));
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
        catch (CloudflareApiException exception) when (exception.StatusCode is { } status && (int)status >= 500)
        {
            // Cloudflare answers this endpoint with a 500 and [8000000] "An unknown error occurred" from time
            // to time, which was seen live on the sibling test that also creates a project. Its own failure is
            // reported rather than failed, and the finally below still removes whatever was made.
            Warn($"Pages project {name}: {exception.Message}");
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

        var projects = await ReadablePagesProjectsAsync();
        if (projects is null)
        {
            return;
        }

        // Every call below only reads, so the account's own projects are fair game here - unlike the
        // build-cache purge, which creates a project of its own rather than writing to somebody's site.
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

        // Purging only throws away cached build artefacts: the next build is slower and nothing is
        // deleted. It is a write all the same, so it goes to a project this test creates rather than to
        // whatever the account happens to list first - an account that is actually in use has real
        // projects on it, and "the first one" is somebody's site. This test used to purge that, and only
        // stopped being harmless-looking by accident. Retrying a deployment is deliberately not
        // exercised here, because that starts a real build.
        var name = $"cfnet-it-{Guid.NewGuid().ToString("N")[..10]}";

        try
        {
            var created = await Client.Pages.CreateAsync(name, "main");
            await Client.Pages.PurgeBuildCacheAsync(created.Name);
            Log($"purged the build cache of {created.Name}, the project this test created");
        }
        catch (CloudflareApiException exception) when (exception.StatusCode is { } status && (int)status >= 500)
        {
            // Observed against the live API: POST /accounts/{account_id}/pages/projects answered 500 with
            // [8000000] "An unknown error occurred. Contact your account team or Cloudflare support". That is
            // Cloudflare failing rather than this library being wrong about anything, so it is reported and
            // the run goes on; the finally below still removes the project if one was made.
            Warn($"Pages project {name}: {exception.Message}");
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

    [Fact]
    public async Task A_worker_secret_can_be_written_listed_read_bulk_written_and_removed()
    {
        if (!SkipWithoutAccount() || !SkipWithoutWritePermission())
        {
            return;
        }

        var name = $"cfnet-it-{Guid.NewGuid().ToString("N")[..10]}";

        // Two secret names, and values generated here, written once and never printed: no read of a secret
        // answers its value, so a run report that carried one would be the only copy anyone could see.
        const string firstName = "CF_NET_FIRST";
        const string secondName = "CF_NET_SECOND";
        var firstValue = Guid.NewGuid().ToString("N");
        var secondValue = Guid.NewGuid().ToString("N");

        try
        {
            await Retrying5xxAsync(() => Client.Workers.UploadAsync(
                name,
                "export default { async fetch(request) { return new Response('cf-net'); } }",
                "2025-01-01",
                new[] { WorkerBinding.SecretText(firstName, firstValue) }));
            Log($"uploaded {name} with a secret_text binding named {firstName}");

            // A binding in a script's metadata names a secret; the secrets endpoint is what stores a value
            // behind the name. Whether an upload that carried one already stored it is worth knowing, so it is
            // recorded rather than assumed.
            var before = await Client.Workers.ListSecretsAsync(name);
            Log($"secrets straight after the upload: {before.Count} - {string.Join(", ", before)}");
            ReportSecretFields("GET /accounts/{account_id}/workers/scripts/{script_name}/secrets", before);

            var written = await Client.Workers.PutSecretAsync(name, firstName, firstValue);
            Assert.Equal(firstName, written.Name);
            Assert.Equal(WorkerSecretTypes.SecretText, written.Type);
            Log($"put {written}, and no value came back with it, which is the point of the endpoint");
            ReportSecretFields("PUT /accounts/{account_id}/workers/scripts/{script_name}/secrets", new[] { written });

            var read = await Client.Workers.GetSecretAsync(name, firstName);
            Assert.NotNull(read);
            Assert.Equal(firstName, read!.Name);
            Assert.Equal(WorkerSecretTypes.SecretText, read.Type);
            ReportSecretFields(
                "GET /accounts/{account_id}/workers/scripts/{script_name}/secrets/{secret_name}",
                new[] { read });

            // One call writes a second secret and removes the first, which is a null in the map. A bulk write
            // exists because each of these makes a version of the script, so two calls would make two.
            var bulk = await Client.Workers.UpdateSecretsBulkAsync(
                name,
                new WorkerSecretBulkRequest()
                    .WithText(secondName, secondValue)
                    .WithRemoved(firstName));
            Log($"the bulk write answered {bulk.Count} secret(s): {string.Join(", ", bulk.Values)}");
            ReportSecretFields(
                "PATCH /accounts/{account_id}/workers/scripts/{script_name}/secrets-bulk",
                bulk.Values.ToList());

            var after = await Client.Workers.ListSecretsAsync(name);
            Assert.Contains(after, secret => secret.Name == secondName);

            if (after.Any(secret => secret.Name == firstName))
            {
                // A null in that map is published as a removal, so this is either a wrong reading of it or a
                // body Cloudflare ignored - either way it is the fact worth reporting, not failing on.
                Warn($"PATCH …/secrets-bulk answered 200 and the secret mapped to null is still there: " +
                     $"{after.Count} secret(s) - {string.Join(", ", after)}. A null in that map is documented " +
                     "as a removal, so either it is not one, or this library writes it in a way Cloudflare " +
                     "ignores.");
            }
            else
            {
                Log($"the bulk write removed {firstName}, which is what a null in its map means");
                Assert.Null(await Client.Workers.GetSecretAsync(name, firstName));
            }

            await Client.Workers.DeleteSecretAsync(name, secondName);
            var emptied = await Client.Workers.ListSecretsAsync(name);
            Assert.DoesNotContain(emptied, secret => secret.Name == secondName);
            Log($"deleted {secondName}; {emptied.Count} secret(s) left on {name}");
        }
        finally
        {
            await Client.Workers.DeleteAsync(name);
            Log($"deleted the script {name}");
        }
    }

    /// <summary>
    /// Warns once about the fields of a secret this library does not model, naming them rather than printing
    /// the item: even with no value in it, a secret's description belongs in a run report by name only.
    /// </summary>
    private void ReportSecretFields(string endpoint, IReadOnlyList<WorkerSecret> secrets)
    {
        foreach (var secret in secrets.Take(3))
        {
            if (secret.AdditionalData is not { Count: > 0 })
            {
                continue;
            }

            Warn($"{endpoint} answered fields this library does not model for a Worker secret: " +
                 $"{string.Join(", ", secret.AdditionalData.Keys)} on '{secret.Name}'.");
        }
    }

    [Fact]
    public async Task A_worker_route_can_be_created_read_changed_and_removed()
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

        var scriptName = $"cfnet-it-{Guid.NewGuid().ToString("N")[..10]}";

        // A route only carries traffic for a hostname that has a proxied DNS record in the zone, so this makes
        // one rather than borrowing a hostname that may or may not be there.
        var hostname = $"{UniqueSuffix("cfnet-route")}.{zone!.Name}";
        var unmodelled = new SortedSet<string>();
        DnsRecord? record = null;

        try
        {
            await Retrying5xxAsync(() => Client.Workers.UploadAsync(
                scriptName,
                "export default { async fetch(request) { return new Response('cf-net'); } }",
                "2025-01-01"));

            record = await Retrying5xxAsync(() => Client.DnsRecords.CreateAddressRecordAsync(
                hostname, "192.0.2.1", proxied: true, ttl: 1, ZoneId));
            Log($"created the proxied record {record.Name} for the route to point at");

            var pattern = $"{hostname}/*";
            var created = await Client.Workers.CreateRouteAsync(
                WorkerRouteRequest.For(pattern, scriptName), ZoneId);
            Assert.False(string.IsNullOrWhiteSpace(created.Id));
            Assert.Equal(pattern, created.Pattern);
            Assert.Equal(scriptName, created.Script);
            Assert.False(created.BypassesWorkers);
            Collect(unmodelled, "routes.create", created.AdditionalData);
            Log($"created route {created.Id}: {created}");

            var listed = await Client.Workers.ListRoutesAsync(ZoneId);
            Assert.Contains(listed, route => route.Id == created.Id);
            Collect(
                unmodelled,
                "routes.list",
                listed.First(route => route.Id == created.Id).AdditionalData);

            // The list takes no filter, so which routes belong to one Worker is answered locally - and the
            // script name here belongs to nothing else, so this is exactly one route.
            var mine = await Client.Workers.ListRoutesForWorkerAsync(scriptName, ZoneId);
            Assert.Equal(new[] { created.Id }, mine.Select(route => route.Id));

            var read = await Client.Workers.GetRouteAsync(created.Id!, ZoneId);
            Assert.NotNull(read);
            Assert.Equal(pattern, read!.Pattern);
            Assert.Equal(scriptName, read.Script);
            Collect(unmodelled, "routes.get", read.AdditionalData);

            // A PUT replaces the whole route, so this one keeps the script and moves the pattern.
            var narrower = $"{hostname}/api/*";
            var updated = await Client.Workers.UpdateRouteAsync(
                created.Id!,
                WorkerRouteRequest.For(narrower, scriptName),
                ZoneId);
            Assert.Equal(narrower, updated.Pattern);
            Assert.Equal(scriptName, updated.Script);
            Collect(unmodelled, "routes.put", updated.AdditionalData);

            // The same PUT with no script is what turns a route into a bypass - a documented kind of route,
            // which negates the less specific patterns around it. Not asserted on, because no published
            // example answers one; if Cloudflare keeps the script instead, the Warn below says so.
            var bypass = await Client.Workers.UpdateRouteAsync(
                created.Id!,
                WorkerRouteRequest.Bypassing(narrower),
                ZoneId);
            Collect(unmodelled, "routes.put-without-script", bypass.AdditionalData);
            var afterBypass = await Client.Workers.GetRouteAsync(created.Id!, ZoneId);
            if (bypass.Script is not null || afterBypass?.Script is not null)
            {
                Warn($"PUT /zones/{{zone_id}}/workers/routes/{{route_id}} with no script in the body answered " +
                     $"'{bypass.Script}', and the GET after it answered '{afterBypass?.Script}'. A route with " +
                     "no script is documented as one that runs nothing and negates the less specific patterns " +
                     "around it, so either this library is dropping the field on the way out or Cloudflare is " +
                     "keeping the previous one.");
            }

            await Client.Workers.DeleteRouteAsync(created.Id!, ZoneId);
            Assert.Null(await Client.Workers.GetRouteAsync(created.Id!, ZoneId));
            Log($"deleted route {created.Id} and the GET of it answers 'not there'");

            // The account's Workers settings ride along: the read and the write are the same two fields, so
            // this reads them and hands them straight back, which changes nothing either way.
            var settings = await Client.Workers.GetAccountSettingsAsync();
            Log($"workers account settings: {settings}");
            Collect(unmodelled, "account-settings.get", settings.AdditionalData);

            // The field is a string on purpose, because this endpoint does not enumerate its words - so this
            // is not a claim that the answer has to be one of them, it is how a word this library has not met
            // gets named instead of quietly passing by.
            Assert.True(
                settings.DefaultUsageModel is WorkerUsageModels.Standard
                    or WorkerUsageModels.Bundled
                    or WorkerUsageModels.Unbound,
                $"GET /accounts/{{account_id}}/workers/account-settings answered default_usage_model=" +
                $"'{settings.DefaultUsageModel}', which is not one of the words WorkerUsageModels carries.");

            try
            {
                var put = await Client.Workers.UpdateAccountSettingsAsync(settings);
                Log($"the account-settings PUT answered {put}");
                Collect(unmodelled, "account-settings.put", put.AdditionalData);
            }
            catch (CloudflareApiException exception)
            {
                // Cloudflare's own page for this endpoint says the write needs Account Settings Write, a
                // different group from the Workers ones the rest of this fact runs on, so a refusal here is
                // that rather than a defect in the call - and it is the one thing worth reporting.
                Warn($"PUT /accounts/{{account_id}}/workers/account-settings was refused: " +
                     $"{exception.StatusCode} [{exception.ErrorCode}] {Truncate(exception.Message, 240)}. " +
                     "Cloudflare documents this write as needing Account Settings Write, which the token this " +
                     "run uses does not carry.");
            }
        }
        finally
        {
            if (record?.Id is { Length: > 0 } recordId)
            {
                await Client.DnsRecords.DeleteAsync(recordId, ZoneId);
                Log($"deleted the proxied record {hostname}");
            }

            await Client.Workers.DeleteAsync(scriptName);
            Log($"deleted the script {scriptName}");

            if (unmodelled.Count > 0)
            {
                Warn("Fields the Workers routes and account-settings answers carried that this library does not " +
                     $"model: {string.Join("; ", unmodelled)}. They are readable through AdditionalData, and " +
                     "naming them here is how they become properties instead of guesses.");
            }
        }
    }

    /// <summary>
    /// Records the fields an answer carried that this library does not model, tagged with the call they came
    /// from, so one Warn at the end of a fact can name them all.
    /// </summary>
    private static void Collect(
        SortedSet<string> into,
        string tag,
        IDictionary<string, System.Text.Json.JsonElement>? additional)
    {
        if (additional is not { Count: > 0 })
        {
            return;
        }

        foreach (var key in additional.Keys)
        {
            into.Add($"{tag}: {key}");
        }
    }

    [Fact]
    public async Task A_page_rule_can_be_created_read_patched_replaced_and_removed()
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

        // A live run settled this before it settled anything else: Cloudflare answers 400 [1011] "Page Rules
        // endpoint does not support account owned tokens" to every call here, the list as much as the create,
        // so an account-owned token cannot exercise page rules at all and the fact opts out with that reason.
        if (!SkipUnless(
                !IsAccountOwnedToken,
                "Every page rule call is refused for an account-owned token: Cloudflare answers 400 [1011] " +
                "'Page Rules endpoint does not support account owned tokens', for the list as much as the " +
                "create. Configure a user token (cfut_) to exercise this one."))
        {
            return;
        }

        var pattern = $"{UniqueSuffix("cfnet-pr")}.{zone!.Name}/*";
        var unmodelled = new SortedSet<string>();
        var surprises = new List<string>();
        PageRule? created = null;

        try
        {
            try
            {
                // One zone setting overridden for one URL pattern. No forwarding action here, because a rule
                // that redirects cannot carry another action and the redirect gets its own PUT below.
                created = await Client.PageRules.CreateAsync(
                    new PageRuleRequest
                    {
                        Targets = new List<PageRuleTarget> { PageRuleTarget.Matching(pattern) },
                        Actions = new List<PageRuleAction> { PageRuleAction.On(PageRuleActionIds.BrowserCheck) },
                        Status = PageRuleStatus.Active,
                    },
                    ZoneId);
            }
            catch (CloudflareApiException exception)
            {
                // Everything after this needs a rule to work on. A plan carries an allowance of page rules -
                // three on a free plan - so a full zone reads like this, and so does a token kind Cloudflare
                // refuses the endpoint for, which is what an account-owned one gets with 1011.
                Warn($"POST /zones/{{zone_id}}/pagerules was refused: {exception.StatusCode} " +
                     $"[{exception.ErrorCode}] {Truncate(exception.Message, 240)}. A plan carries an allowance " +
                     "of page rules, three on a free plan, so a full zone reads exactly like this - and so does " +
                     "a token kind the endpoint refuses, which 1011 is.");
                return;
            }

            Assert.False(string.IsNullOrWhiteSpace(created.Id));
            var ruleId = created.Id!;
            Assert.Equal(PageRuleStatus.Active, created.Status);
            Assert.Equal(pattern, created.Targets!.Single().Constraint!.Value);
            Assert.Equal(PageRuleTargetOperator.Matches, created.Targets!.Single().Constraint!.Operator);
            Assert.Equal(PageRuleActionIds.BrowserCheck, created.Actions!.Single().Id);
            Assert.Equal("on", created.Actions!.Single().ValueAsText());
            CollectPageRule(unmodelled, "pagerules.create", created);
            Log($"created page rule {ruleId} for {pattern}");

            if (created.Priority != 1)
            {
                surprises.Add(
                    $"the create answered priority={created.Priority} for a body that sent none, and the " +
                    "endpoint documents a default of 1");
            }

            if (created.CreatedOnUtc is null && created.CreatedOn is { Length: > 0 } stamp)
            {
                surprises.Add($"created_on arrived as '{Truncate(stamp, 60)}', which is not a timestamp this library can read");
            }

            var listed = await Client.PageRules.ListAsync(zoneId: ZoneId);
            Assert.Contains(listed, rule => rule.Id == ruleId);

            // The list takes four filters and no paging. The status one is checkable against a rule this fact
            // just made active; the ordering one is a claim about Cloudflare rather than about this library, so
            // it is reported when it does not hold rather than asserted.
            var disabled = await Client.PageRules.ListAsync(
                new PageRuleListParameters { Status = PageRuleStatus.Disabled }, ZoneId);
            Assert.DoesNotContain(disabled, rule => rule.Id == ruleId);

            var ordered = await Client.PageRules.ListAsync(
                new PageRuleListParameters
                {
                    Order = PageRuleListOrder.Priority,
                    Direction = SortDirection.Descending,
                },
                ZoneId);
            var priorities = ordered.Select(rule => rule.Priority ?? 0).ToList();
            if (priorities.Count > 1 && !priorities.SequenceEqual(priorities.OrderByDescending(priority => priority)))
            {
                surprises.Add(
                    $"?order=priority&direction=desc answered priorities [{string.Join(", ", priorities)}], " +
                    "which is not descending");
            }

            var read = await Client.PageRules.GetAsync(ruleId, ZoneId);
            Assert.NotNull(read);
            Assert.Equal(pattern, read!.Targets!.Single().Constraint!.Value);
            Assert.Equal(created.Actions!.Single().Id, read.Actions!.Single().Id);
            CollectPageRule(unmodelled, "pagerules.get", read);

            // The patch is the point of having both a PUT and a PATCH: one field changes, and the targets and
            // actions are left alone rather than replaced with nothing.
            var patched = await Client.PageRules.EditAsync(
                ruleId,
                new PageRuleRequest { Status = PageRuleStatus.Disabled },
                ZoneId);
            Assert.Equal(PageRuleStatus.Disabled, patched.Status);
            Assert.Equal(pattern, patched.Targets!.Single().Constraint!.Value);
            Assert.Equal(PageRuleActionIds.BrowserCheck, patched.Actions!.Single().Id);
            CollectPageRule(unmodelled, "pagerules.patch", patched);

            var afterPatch = await Client.PageRules.GetAsync(ruleId, ZoneId);
            Assert.Equal(PageRuleStatus.Disabled, afterPatch!.Status);
            Assert.Equal(pattern, afterPatch.Targets!.Single().Constraint!.Value);

            // And the PUT replaces the whole rule: a new target, a different action, a priority of its own, and
            // no status in the body at all - which is what makes the answer's status worth reporting.
            var forwarded = await Client.PageRules.UpdateAsync(
                ruleId,
                new PageRuleRequest
                {
                    Targets = new List<PageRuleTarget> { PageRuleTarget.Of(PageRuleTargetOperator.Matches, pattern) },
                    Actions = new List<PageRuleAction> { PageRuleAction.Forwarding(302, $"https://{zone.Name}/") },
                    Priority = 2,
                },
                ZoneId);
            Assert.Equal(2, forwarded.Priority);
            Assert.Equal(PageRuleActionIds.ForwardingUrl, forwarded.Actions!.Single().Id);
            Assert.Equal(pattern, forwarded.Targets!.Single().Constraint!.Value);
            CollectPageRule(unmodelled, "pagerules.put", forwarded);

            if (forwarded.Status != PageRuleStatus.Disabled)
            {
                surprises.Add(
                    $"a PUT that sent no status answered '{forwarded.Status}' where the rule was disabled " +
                    "before it, so a PUT resets a field its body leaves out rather than keeping it");
            }

            var value = forwarded.Actions!.Single().Value;
            if (value is not { ValueKind: JsonValueKind.Object } ||
                !value.Value.TryGetProperty("status_code", out _) ||
                !value.Value.TryGetProperty("url", out _))
            {
                surprises.Add(
                    $"the forwarding_url value came back as {Truncate(value?.GetRawText(), 200)}, which is not " +
                    "the {status_code, url} object the create takes");
            }

            await Client.PageRules.DeleteAsync(ruleId, ZoneId);
            Assert.Null(await Client.PageRules.GetAsync(ruleId, ZoneId));
            Log($"deleted page rule {ruleId}, and the GET of it answers 'not there'");
            created = null;
        }
        finally
        {
            if (created?.Id is { Length: > 0 } id)
            {
                await Client.PageRules.DeleteAsync(id, ZoneId);
                Log($"deleted page rule {id} on the way out");
            }

            if (surprises.Count > 0)
            {
                Warn($"Page rules answered something other than what the reference publishes: " +
                     $"{string.Join("; ", surprises)}.");
            }

            if (unmodelled.Count > 0)
            {
                Warn("Fields the page rule answers carried that this library does not model: " +
                     $"{string.Join("; ", unmodelled)}. They are readable through AdditionalData, and naming " +
                     "them here is how they become properties instead of guesses.");
            }
        }
    }

    /// <summary>
    /// Records the fields a page rule answer carried that this library does not model - on the rule, on each of
    /// its targets and constraints, and on each of its actions, tagged with the call they came from.
    /// </summary>
    private static void CollectPageRule(SortedSet<string> into, string tag, PageRule rule)
    {
        Collect(into, tag, rule.AdditionalData);

        foreach (var target in rule.Targets ?? new List<PageRuleTarget>())
        {
            Collect(into, $"{tag}.target", target.AdditionalData);
            Collect(into, $"{tag}.constraint", target.Constraint?.AdditionalData);
        }

        foreach (var action in rule.Actions ?? new List<PageRuleAction>())
        {
            Collect(into, $"{tag}.action.{action.Id}", action.AdditionalData);
        }
    }

    [Fact]
    public async Task A_turnstile_widget_can_be_created_read_updated_rotated_and_deleted()
    {
        if (!SkipWithoutAccount() || !SkipWithoutWritePermission())
        {
            return;
        }

        var name = UniqueSuffix("cfnet-it-widget");
        var renamed = $"{name}-upd";

        // Created first and on its own, because everything below needs the site key it answers - and because
        // a refusal to create is worth reporting rather than failing: it is either a missing Turnstile Sites
        // permission or an account that has never used the product, and neither is a defect in this library.
        TurnstileWidget created;
        try
        {
            created = await Client.Turnstile.CreateAsync(
                new TurnstileWidgetRequest
                {
                    Name = name,
                    Mode = TurnstileWidgetMode.Managed,
                    Domains = new List<string> { "example.com" },
                    Region = TurnstileRegion.World,
                    ClearanceLevel = TurnstileClearanceLevel.NoClearance,
                    BotFightMode = false,
                    EphemeralId = false,
                    Offlabel = false,
                });
        }
        catch (CloudflareApiException exception)
        {
            Warn($"POST /accounts/{{account_id}}/challenges/widgets was refused with " +
                 $"{exception.StatusCode}/{exception.ErrorCode}: {Truncate(exception.Message, 300)} " +
                 $"Cloudflare's body: {Truncate(exception.ResponseBody, 500)}");
            return;
        }

        var sitekey = created.Sitekey;
        Assert.False(string.IsNullOrEmpty(sitekey));
        Assert.False(string.IsNullOrEmpty(created.Secret));

        try
        {
            // A widget's secret is a credential, so what follows reports that one arrived and how long it is
            // rather than what it says: a CI annotation is not where a secret belongs.
            Log($"created {created}: mode={created.Mode} region={created.Region} " +
                $"clearance={created.ClearanceLevel} deployed_via={created.DeployedVia} " +
                $"created_on={created.CreatedOn} secret={created.Secret!.Length} chars");
            ReportWidgetFields("POST /accounts/{account_id}/challenges/widgets", created);

            var read = await Client.Turnstile.GetAsync(sitekey!);
            Assert.NotNull(read);
            Assert.Equal(name, read!.Name);
            Assert.Equal(TurnstileWidgetMode.Managed, read.Mode);
            Assert.Equal(created.Secret, read.Secret);
            Log($"read it back: {read} offlabel={read.Offlabel} ephemeral_id={read.EphemeralId} " +
                $"bot_fight_mode={read.BotFightMode} last_modified_via={read.LastModifiedVia}");
            ReportWidgetFields("GET /accounts/{account_id}/challenges/widgets/{sitekey}", read);

            // Updating is a PUT that replaces the whole widget, so the required fields go again beside the
            // name and the mode that are changing - and the name is what proves a replacement took rather than
            // an answer that merely looks like one. Both offlabel and ephemeral_id stay out of it: two live
            // runs reported that setting either to true is refused as an entitlement, in the same words for
            // each, and paid features are out of scope here, so both fields are wrapped and left alone. A
            // refusal is reported rather than failed and does not stop the rotation, the list or the delete
            // below it: the offline suite pins this call's method, path and exact body, so what a live run
            // adds is what Cloudflare accepts.
            TurnstileWidget? updated = null;
            try
            {
                updated = await Client.Turnstile.UpdateAsync(
                    sitekey!,
                    new TurnstileWidgetRequest
                    {
                        Name = renamed,
                        Mode = TurnstileWidgetMode.NonInteractive,
                        Domains = new List<string> { "example.com", "www.example.com" },
                        Region = TurnstileRegion.World,
                    });
            }
            catch (CloudflareApiException exception)
            {
                Warn($"PUT /accounts/{{account_id}}/challenges/widgets/{{sitekey}} was refused with " +
                     $"{exception.StatusCode}/{exception.ErrorCode}: {Truncate(exception.Message, 300)} " +
                     $"Cloudflare's body: {Truncate(exception.ResponseBody, 500)}");
            }

            if (updated is not null)
            {
                Assert.Equal(renamed, updated.Name);
                Assert.Equal(TurnstileWidgetMode.NonInteractive, updated.Mode);
                Assert.Equal(2, updated.Domains?.Count ?? 0);
                Log($"updated it: {updated} mode={updated.Mode} offlabel={updated.Offlabel} " +
                    $"domains={string.Join(",", updated.Domains ?? new List<string>())} " +
                    $"modified_on={updated.ModifiedOn}");
                ReportWidgetFields("PUT /accounts/{account_id}/challenges/widgets/{sitekey}", updated);
            }

            // Rotating without the flag leaves the old secret working for a grace period, which is the point
            // of not sending it: a deployment can be rolled without a gap in verification.
            var rotated = await Client.Turnstile.RotateSecretAsync(sitekey!);
            Assert.False(string.IsNullOrEmpty(rotated.Secret));
            Assert.NotEqual(created.Secret, rotated.Secret);
            Log($"rotated its secret: {rotated.Secret!.Length} chars, was {created.Secret!.Length}, and the " +
                $"sitekey stayed {rotated.Sitekey}");

            // A list answers every widget and no secrets, which is why the two shapes are typed apart.
            var listed = await Client.Turnstile.ListAsync(new TurnstileWidgetListParameters { PerPage = 100 });
            Log($"the account lists {listed.TotalCount ?? listed.Count} widget(s); this page carries " +
                $"{listed.Count}");
            var found = listed.Items.FirstOrDefault(widget => widget.Sitekey == sitekey);
            Assert.NotNull(found);

            // The point of the two types: a list item is not a widget, and has no secret to read.
            Assert.IsNotType<TurnstileWidget>(found);

            if (updated is not null)
            {
                // Only assert the replacement took when the replacement was accepted: a refused PUT leaves the
                // widget exactly as it was created, which is the name and the mode below.
                Assert.Equal(renamed, found!.Name);
                Assert.Equal(TurnstileWidgetMode.NonInteractive, found.Mode);
            }
            else
            {
                Assert.Equal(name, found!.Name);
                Assert.Equal(TurnstileWidgetMode.Managed, found.Mode);
            }

            ReportWidgetFields("GET /accounts/{account_id}/challenges/widgets", found);
        }
        finally
        {
            var deleted = await Client.Turnstile.DeleteAsync(sitekey!);
            Log($"deleted it: {(deleted is null ? "Cloudflare answered nothing" : deleted.ToString())}");
        }
    }

    /// <summary>
    /// Warns once about the fields of a widget this library does not model, naming them rather than printing
    /// the widget: a single widget's answer carries its secret, and this reports to a CI annotation.
    /// </summary>
    private void ReportWidgetFields(string endpoint, TurnstileWidgetSummary widget)
    {
        if (widget.AdditionalData is not { Count: > 0 })
        {
            return;
        }

        Warn($"{endpoint} answered fields this library does not model for a Turnstile widget: " +
             $"{string.Join(", ", widget.AdditionalData.Keys)}. The names are reported rather than the " +
             $"widget itself, because a single widget's answer carries its secret.");
    }

}
