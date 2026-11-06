using System.Net;
using CFNet.Exceptions;
using CFNet.Models.Rules;
using CFNet.UnitTests.Testing;
using Xunit;

namespace CFNet.UnitTests.Services;

/// <summary>
/// The ruleset engine. Two things about this API shape the tests: the same resource hangs off an account
/// path and a zone path, and every rule change answers with the whole ruleset rather than with the rule.
/// </summary>
public class RulesetServiceTests
{
    /// <summary>A custom redirect ruleset holding one Bulk Redirects rule, as Cloudflare returns it.</summary>
    private const string RulesetWithRule = """
        {"id":"rs-1","name":"cfnet_it_redirects","kind":"custom","phase":"http_request_redirect",
         "version":"3","last_updated":"2026-01-02T03:04:05Z",
         "rules":[{"id":"rule-1","action":"redirect","expression":"true","enabled":true,"version":"3",
                   "last_updated":"2026-01-02T03:04:05Z","logging":{"enabled":true},
                   "action_parameters":{"from_list":{"name":"my_redirects","key":"target_url"}}}]}
        """;

    /// <summary>Wraps a result payload in the envelope the transport expects.</summary>
    private static string Envelope(string resultJson) =>
        "{\"success\":true,\"errors\":[],\"messages\":[],\"result\":" + resultJson + "}";

    // ---------------------------------------------------------------- scope

    [Fact]
    public async Task ListAsync_reads_the_account_rulesets()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(Envelope("""[{"id":"rs-1","name":"default","kind":"root","phase":"http_request_firewall_custom","version":"7","last_updated":"2026-01-02T03:04:05Z"}]"""));

        var page = await harness.Client.Rulesets.ListAsync();

        Assert.Contains("/accounts/acct-1234/rulesets", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal(string.Empty, harness.LastRequest.Query);

        var ruleset = Assert.Single(page.Items);
        Assert.Equal("rs-1", ruleset.Id);
        Assert.True(ruleset.IsRoot);
        Assert.False(ruleset.IsManaged);
        // The version is a string in Cloudflare's own schema, even though it reads like a number.
        Assert.Equal("7", ruleset.Version);
        Assert.Equal(0, ruleset.RuleCount);
        Assert.Equal("default (root, http_request_firewall_custom, version 7, 0 rule(s))", ruleset.ToString());
    }

    [Fact]
    public async Task ListAsync_pages_by_cursor()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(Envelope("[]"));

        await harness.Client.Rulesets.ListAsync(new RulesetListParameters { Cursor = "cur-1", PerPage = 25 });

        // The endpoint takes nothing but the cursor and the page size: there is no kind or phase filter.
        Assert.Equal("?cursor=cur-1&per_page=25", harness.LastRequest.Query);
    }

    [Fact]
    public async Task A_zone_id_is_what_switches_the_scope()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(Envelope("[]"));

        await harness.Client.Rulesets.ListAsync(zoneId: "zone-9999");

        Assert.Contains("/zones/zone-9999/rulesets", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.DoesNotContain("/accounts/", harness.LastRequest.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_configured_default_zone_is_never_used_to_switch_scope()
    {
        // The harness configures both an account id and a zone id. A ruleset call that named neither has to
        // stay on the account path: the same method serves both scopes, so quietly picking the zone because
        // of how the client was configured would change what a caller gets without them asking.
        using var harness = CloudflareHarness.Create();
        harness.Respond(Envelope("[]"));

        await harness.Client.Rulesets.ListAsync();

        Assert.Contains("/accounts/", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.DoesNotContain("/zones/", harness.LastRequest.Path, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- the ruleset

    [Fact]
    public async Task GetAsync_reads_a_ruleset_and_its_rules()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(Envelope(RulesetWithRule));

        var ruleset = await harness.Client.Rulesets.GetAsync("rs-1");

        Assert.Contains("/accounts/acct-1234/rulesets/rs-1", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal("cfnet_it_redirects", ruleset!.Name);
        Assert.Equal(RulesetKind.Custom, ruleset.Kind);
        Assert.Equal(RulesetPhase.HttpRequestRedirect, ruleset.Phase);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), ruleset.LastUpdated);
        Assert.Equal(1, ruleset.RuleCount);

        var rule = Assert.Single(ruleset.Rules!);
        Assert.Equal("rule-1", rule.Id);
        Assert.True(rule.IsRedirect);
        Assert.False(rule.IsExecute);
        Assert.Equal("true", rule.Expression);
        Assert.True(rule.Enabled);
        Assert.Equal("3", rule.Version);
        Assert.True(rule.Logging!.Enabled);
        Assert.Equal("redirect: true", rule.ToString());

        // The Bulk Redirects parameters: a list name and the field of its items to redirect to.
        var parameters = rule.ActionParameters!;
        Assert.True(parameters.IsFromList);
        Assert.Equal("my_redirects", parameters.FromList!.Name);
        Assert.Equal("target_url", parameters.FromList.Key);
        Assert.Null(parameters.FromValue);
        Assert.Equal("from list my_redirects (target_url)", parameters.ToString());
    }

    [Fact]
    public async Task GetAsync_returns_null_when_there_is_no_such_ruleset()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.NotFound, 7003, "Ruleset not found");

        Assert.Null(await harness.Client.Rulesets.GetAsync("nope"));
    }

    [Fact]
    public async Task CreateAsync_sends_name_kind_and_phase()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(RulesetWithRule);

        var created = await harness.Client.Rulesets.CreateAsync(new CreateRulesetRequest
        {
            Name = "cfnet_it_redirects",
            Kind = RulesetKind.Custom,
            Phase = RulesetPhase.HttpRequestRedirect,
        });

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.EndsWith("/accounts/acct-1234/rulesets", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal(
            """{"name":"cfnet_it_redirects","kind":"custom","phase":"http_request_redirect"}""",
            harness.LastRequest.Body);
        Assert.Equal("rs-1", created.Id);
    }

    [Fact]
    public async Task CreateAsync_can_carry_the_first_rules_so_they_land_in_one_version()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(RulesetWithRule);

        await harness.Client.Rulesets.CreateAsync(new CreateRulesetRequest
        {
            Name = "cfnet_it_redirects",
            Kind = RulesetKind.Custom,
            Phase = RulesetPhase.HttpRequestRedirect,
            Rules = new List<CreateRulesetRuleRequest>
            {
                CreateRulesetRuleRequest.ForRedirectFromList("my_redirects", description: "bulk redirects"),
            },
        });

        Assert.Equal(
            """{"name":"cfnet_it_redirects","kind":"custom","phase":"http_request_redirect","rules":[{"action":"redirect","expression":"true","description":"bulk redirects","action_parameters":{"from_list":{"name":"my_redirects","key":"target_url"}}}]}""",
            harness.LastRequest.Body);
    }

    [Fact]
    public async Task CreateAsync_refuses_a_ruleset_that_could_not_run()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Rulesets.CreateAsync(new CreateRulesetRequest()));

        // All three are needed before Cloudflare could make anything of the request, so all three are named
        // at once rather than one per attempt.
        Assert.Equal(3, exception.Problems.Count);
        Assert.Contains("name", exception.Problems[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("kind", exception.Problems[1], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("phase", exception.Problems[2], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task DryRun_is_sent_as_a_query_parameter()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(RulesetWithRule);

        await harness.Client.Rulesets.CreateAsync(
            new CreateRulesetRequest
            {
                Name = "n",
                Kind = RulesetKind.Custom,
                Phase = RulesetPhase.HttpRequestRedirect,
            },
            dryRun: true);

        // Validating without applying is Cloudflare's own dry_run, and it rides along on the query.
        Assert.Equal("?dry_run=true", harness.LastRequest.Query);
    }

    [Fact]
    public async Task UpdateAsync_replaces_the_whole_ruleset()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(RulesetWithRule);

        await harness.Client.Rulesets.UpdateAsync(
            "rs-1",
            new UpdateRulesetRequest { Name = "renamed", Description = "still redirects" });

        Assert.Equal(HttpMethod.Put, harness.LastRequest.Method);
        Assert.Equal("""{"name":"renamed","description":"still redirects"}""", harness.LastRequest.Body);
    }

    [Fact]
    public async Task UpdateAsync_refuses_to_send_nothing_because_that_would_empty_the_ruleset()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Rulesets.UpdateAsync("rs-1", new UpdateRulesetRequest()));

        Assert.Contains("replaces", exception.Problems[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task UpdateAsync_can_clear_the_rules_on_purpose()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(RulesetWithRule);

        // An empty list is not the same as no list: this one says "no rules", and is the only way the
        // refusal above can be told apart from a deliberate clear.
        await harness.Client.Rulesets.UpdateAsync(
            "rs-1",
            new UpdateRulesetRequest { Rules = new List<CreateRulesetRuleRequest>() });

        Assert.Equal("""{"rules":[]}""", harness.LastRequest.Body);
    }

    [Fact]
    public async Task DeleteAsync_deletes_and_can_ask_first()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(Envelope("{}"));

        await harness.Client.Rulesets.DeleteAsync("rs-1", dryRun: true);

        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.Contains("/accounts/acct-1234/rulesets/rs-1", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal("?dry_run=true", harness.LastRequest.Query);
    }

    // ---------------------------------------------------------------- entrypoints

    [Fact]
    public async Task GetEntrypointAsync_reads_the_root_ruleset_for_a_phase()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(RulesetWithRule);

        var entrypoint = await harness.Client.Rulesets.GetEntrypointAsync(RulesetPhase.HttpRequestRedirect);

        Assert.Contains(
            "/accounts/acct-1234/rulesets/phases/http_request_redirect/entrypoint",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Equal("rs-1", entrypoint!.Id);
    }

    [Fact]
    public async Task GetEntrypointAsync_returns_null_for_a_phase_nothing_configured()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.NotFound, 7003, "Ruleset not found");

        // No entrypoint yet is the normal state for a phase nobody has configured, not a failure.
        Assert.Null(await harness.Client.Rulesets.GetEntrypointAsync(RulesetPhase.HttpRatelimit));
    }

    [Fact]
    public async Task UpdateEntrypointAsync_puts_the_rules_a_phase_should_run()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(RulesetWithRule);

        await harness.Client.Rulesets.UpdateEntrypointAsync(
            RulesetPhase.HttpRequestRedirect,
            new UpdateRulesetRequest
            {
                Rules = new List<CreateRulesetRuleRequest>
                {
                    CreateRulesetRuleRequest.ForRedirectFromList("my_redirects"),
                },
            });

        Assert.Equal(HttpMethod.Put, harness.LastRequest.Method);
        Assert.Contains(
            "/rulesets/phases/http_request_redirect/entrypoint",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Contains("\"from_list\":{\"name\":\"my_redirects\",\"key\":\"target_url\"}", harness.LastRequest.Body!, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- rules

    [Fact]
    public async Task CreateRuleAsync_appends_a_rule_and_answers_with_the_whole_ruleset()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(RulesetWithRule);

        var ruleset = await harness.Client.Rulesets.CreateRuleAsync(
            "rs-1",
            CreateRulesetRuleRequest.ForRedirectFromList("my_redirects", "true", "bulk redirects"));

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.EndsWith("/accounts/acct-1234/rulesets/rs-1/rules", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal(
            """{"action":"redirect","expression":"true","description":"bulk redirects","action_parameters":{"from_list":{"name":"my_redirects","key":"target_url"}}}""",
            harness.LastRequest.Body);

        // Cloudflare answers a rule change with the ruleset, so the new rule is read back out of it.
        Assert.Equal(1, ruleset.RuleCount);
        Assert.Equal("rule-1", ruleset.Rules![0].Id);
    }

    [Fact]
    public async Task CreateRuleAsync_refuses_a_rule_that_could_not_match()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Rulesets.CreateRuleAsync("rs-1", new CreateRulesetRuleRequest()));

        Assert.Equal(2, exception.Problems.Count);
        Assert.Contains("action", exception.Problems[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expression", exception.Problems[1], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task EditRuleAsync_patches_only_what_was_set()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(RulesetWithRule);

        await harness.Client.Rulesets.EditRuleAsync(
            "rs-1",
            "rule-1",
            new CreateRulesetRuleRequest { Enabled = false, Description = "off for now" });

        Assert.Equal(HttpMethod.Patch, harness.LastRequest.Method);
        Assert.Contains("/rulesets/rs-1/rules/rule-1", harness.LastRequest.Path, StringComparison.Ordinal);
        // A patch, unlike the ruleset's own update: what is left null is left alone.
        Assert.Equal("""{"description":"off for now","enabled":false}""", harness.LastRequest.Body);
    }

    [Fact]
    public async Task EditRuleAsync_refuses_an_edit_that_changes_nothing()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Rulesets.EditRuleAsync("rs-1", "rule-1", new CreateRulesetRuleRequest()));

        Assert.Contains("at least one field", exception.Problems[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task DeleteRuleAsync_removes_one_rule_and_answers_with_the_ruleset()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """{"id":"rs-1","name":"cfnet_it_redirects","kind":"custom","phase":"http_request_redirect","version":"4","rules":[]}""");

        var ruleset = await harness.Client.Rulesets.DeleteRuleAsync("rs-1", "rule-1");

        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.Contains("/rulesets/rs-1/rules/rule-1", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal("4", ruleset.Version);
        Assert.Equal(0, ruleset.RuleCount);
    }

    // ---------------------------------------------------------------- versions

    [Fact]
    public async Task Versions_read_back_as_rulesets_because_that_is_what_they_are()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond(Envelope("""
            [{"id":"rs-1","name":"v1","kind":"custom","phase":"http_request_redirect","version":"1","rules":[]},
             {"id":"rs-1","name":"v2","kind":"custom","phase":"http_request_redirect","version":"2","rules":[]}]
            """));

        var versions = await harness.Client.Rulesets.ListVersionsAsync("rs-1");

        Assert.Contains("/accounts/acct-1234/rulesets/rs-1/versions", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal(2, versions.Count);
        Assert.Equal("1", versions[0].Version);
        Assert.Equal("2", versions[1].Version);
    }

    [Fact]
    public async Task One_version_can_be_read_and_pruned()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(RulesetWithRule);
        var version = await harness.Client.Rulesets.GetVersionAsync("rs-1", "2");
        Assert.Contains("/rulesets/rs-1/versions/2", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.NotNull(version);

        using var pruned = CloudflareHarness.Create();
        pruned.RespondWithError(HttpStatusCode.NotFound, 7003, "Version not found");
        Assert.Null(await pruned.Client.Rulesets.GetVersionAsync("rs-1", "99"));

        using var deleting = CloudflareHarness.Create();
        deleting.Respond(Envelope("{}"));
        await deleting.Client.Rulesets.DeleteVersionAsync("rs-1", "2");
        Assert.Equal(HttpMethod.Delete, deleting.LastRequest.Method);
        Assert.Contains("/rulesets/rs-1/versions/2", deleting.LastRequest.Path, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- the shapes a rule can carry

    [Fact]
    public async Task A_dynamic_redirect_computes_its_target_instead_of_reading_a_list()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""
            {"id":"rs-2","name":"dynamic","kind":"root","phase":"http_request_dynamic_redirect","version":"1",
             "rules":[{"id":"rule-9","action":"redirect","expression":"true",
                       "action_parameters":{"from_value":{"target_url":{"expression":"concat(\"https://new.example.com\", http.request.uri.path)"},"status_code":302,"preserve_query_string":true}}}]}
            """);

        var rule = (await harness.Client.Rulesets.GetAsync("rs-2"))!.Rules![0];

        Assert.Null(rule.ActionParameters!.FromList);
        Assert.False(rule.ActionParameters.IsFromList);
        var value = rule.ActionParameters.FromValue!;
        Assert.Equal(302, value.StatusCode);
        Assert.True(value.PreserveQueryString);
        Assert.Null(value.TargetUrl!.Value);
        Assert.Contains("concat", value.TargetUrl.Expression, StringComparison.Ordinal);
        Assert.Contains("-302->", value.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Parameters_of_an_action_this_library_does_not_type_are_still_readable()
    {
        // An execute rule names another ruleset in its own parameters. Those are not declared, so they land
        // in AdditionalData rather than being dropped - a caller can read them as they came, and a later
        // release can type them from what a live run reports.
        var rule = System.Text.Json.JsonSerializer.Deserialize<RulesetRule>(
            """{"id":"rule-2","action":"execute","expression":"true","action_parameters":{"id":"rs-managed","version":"latest","overrides":{"action":"block"}}}""",
            CFNet.Serialization.CloudflareJson.Default)!;

        Assert.True(rule.IsExecute);
        Assert.False(rule.IsRedirect);
        Assert.NotNull(rule.ActionParameters);
        Assert.Contains("id", rule.ActionParameters!.AdditionalData!.Keys, StringComparer.Ordinal);
        Assert.Contains("overrides", rule.ActionParameters.AdditionalData.Keys, StringComparer.Ordinal);
        Assert.Equal(3, rule.ActionParameters.AdditionalData.Count);
        // The label names what it captured, so an untyped action's parameters are still readable.
        Assert.Contains("overrides", rule.ActionParameters.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_disabled_rule_says_so_in_its_label()
    {
        Assert.Equal(
            "block (disabled): ip.src eq 198.51.100.7",
            new RulesetRule
            {
                Action = "block",
                Expression = "ip.src eq 198.51.100.7",
                Enabled = false,
            }.ToString());
        Assert.Equal("(no parameters)", new RulesetActionParameters().ToString());
        Assert.Equal("(unnamed) (custom, http_ratelimit, version 1, 0 rule(s))", new Ruleset
        {
            Kind = RulesetKind.Custom,
            Phase = RulesetPhase.HttpRatelimit,
            Version = "1",
        }.ToString());
    }

    [Fact]
    public void The_phase_constants_are_the_wire_values()
    {
        // A phase that does not exist is refused rather than ignored, so these are the strings Cloudflare
        // expects and not labels this library invented.
        Assert.Equal("http_request_redirect", RulesetPhase.HttpRequestRedirect);
        Assert.Equal("http_request_dynamic_redirect", RulesetPhase.HttpRequestDynamicRedirect);
        Assert.Equal("http_request_firewall_custom", RulesetPhase.HttpRequestFirewallCustom);
        Assert.Equal("http_ratelimit", RulesetPhase.HttpRatelimit);
        Assert.Equal("magic_transit_ids_managed", RulesetPhase.MagicTransitIdsManaged);
    }
}
