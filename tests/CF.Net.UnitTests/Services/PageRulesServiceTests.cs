using System.Text.Json;
using CFNet.Exceptions;
using CFNet.Models.Common;
using CFNet.Models.PageRules;
using CFNet.Serialization;
using CFNet.UnitTests.Testing;
using Xunit;

namespace CFNet.UnitTests.Services;

/// <summary>
/// Page rules: <c>/zones/{zone_id}/pagerules</c>, one word in the path. Worth pinning closely because three
/// things here are easy to get wrong - the list takes filters but no paging, a <c>PUT</c> replaces the whole
/// rule where a <c>PATCH</c> changes only what it is given, and an action's value has a different shape for
/// every action, which is why it is raw JSON with factories for the common ones.
/// </summary>
public class PageRulesServiceTests
{
    /// <summary>A rule with every kind of action value in it, as a list or a get answers one.</summary>
    private const string RichRule =
        """{"id":"rule-1","targets":[{"target":"url","constraint":{"operator":"matches","value":"example.com/images/*"}}],"actions":[{"id":"browser_check","value":"on"},{"id":"edge_cache_ttl","value":7200},{"id":"forwarding_url","value":{"status_code":301,"url":"https://example.org/*"}}],"priority":2,"status":"active","created_on":"2014-01-01T05:20:00.12345Z","modified_on":"2014-01-02T05:20:00.12345Z"}""";

    [Fact]
    public async Task ListAsync_reads_the_zones_rules_as_a_bare_array_with_no_paging()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult($"[{RichRule}]");

        var rules = await harness.Client.PageRules.ListAsync();

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.EndsWith("/zones/zone-5678/pagerules", harness.LastRequest.Path, StringComparison.Ordinal);

        // No page or per_page: the endpoint takes none, because a plan's allowance is small enough not to need
        // them, and the answer is a bare array rather than a paged one.
        Assert.Equal(string.Empty, harness.LastRequest.Query.TrimStart('?'));

        var rule = Assert.Single(rules);
        Assert.Equal("rule-1", rule.Id);
        Assert.Equal(2, rule.Priority);
        Assert.Equal(PageRuleStatus.Active, rule.Status);

        var target = Assert.Single(rule.Targets!);
        Assert.Equal(PageRuleTargetKinds.Url, target.Target);
        Assert.Equal(PageRuleTargetOperator.Matches, target.Constraint!.Operator);
        Assert.Equal("example.com/images/*", target.Constraint.Value);
        Assert.True(target.AdditionalData is not { Count: > 0 });

        // The three value shapes a page rule action takes: a word, a number, and an object - all of them kept
        // as the JSON Cloudflare sent rather than forced into one type.
        Assert.Equal(3, rule.Actions!.Count);
        Assert.Equal("browser_check", rule.Actions[0].Id);
        Assert.Equal("on", rule.Actions[0].ValueAsText());
        Assert.Null(rule.Actions[0].ValueAsNumber());
        Assert.Equal(7200, rule.Actions[1].ValueAsNumber());
        Assert.Null(rule.Actions[1].ValueAsText());
        Assert.Equal(JsonValueKind.Object, rule.Actions[2].Value!.Value.ValueKind);
        Assert.Equal(301, rule.Actions[2].Value!.Value.GetProperty("status_code").GetInt32());
        Assert.Equal("https://example.org/*", rule.Actions[2].Value!.Value.GetProperty("url").GetString());

        // Cloudflare publishes date-time for both stamps, and the model keeps the words it sent as well as the
        // parsed instant, so neither has to be recovered from the other.
        Assert.Equal("2014-01-01T05:20:00.12345Z", rule.CreatedOn);
        Assert.Equal(2014, rule.CreatedOnUtc!.Value.Year);
        Assert.Equal(TimeSpan.Zero, rule.CreatedOnUtc!.Value.Offset);
        Assert.Equal(2014, rule.ModifiedOnUtc!.Value.Year);
        Assert.True(rule.AdditionalData is not { Count: > 0 });
    }

    [Fact]
    public async Task A_rule_reads_a_timestamp_it_can_parse_and_leaves_one_it_cannot()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """[{"id":"a","created_on":"2026-01-02T03:04:05Z"},{"id":"b","created_on":"2024-05-08 02:23:55.897"},{"id":"c","created_on":"not a timestamp"},{"id":"d"}]""");

        var rules = await harness.Client.PageRules.ListAsync();

        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), rules[0].CreatedOnUtc);

        // The shape without an offset, which Cloudflare writes on some endpoints: read as UTC rather than
        // dropped, and the words it sent are still there for a caller who wants them.
        Assert.Equal(new DateTimeOffset(2024, 5, 8, 2, 23, 55, 897, TimeSpan.Zero), rules[1].CreatedOnUtc);
        Assert.Equal("2024-05-08 02:23:55.897", rules[1].CreatedOn);

        Assert.Null(rules[2].CreatedOnUtc);
        Assert.Equal("not a timestamp", rules[2].CreatedOn);
        Assert.Null(rules[3].CreatedOnUtc);
    }

    [Fact]
    public async Task A_rule_describes_itself_in_one_line()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """[{"id":"rule-1","targets":[{"target":"url","constraint":{"operator":"matches","value":"example.com/*"}}],"actions":[{"id":"cache_level","value":"everything"}],"priority":1,"status":"disabled"}]""");

        var rule = (await harness.Client.PageRules.ListAsync()).Single();

        Assert.Equal(
            "url matches example.com/* => cache_level=\"everything\" (priority 1, Disabled)",
            rule.ToString());

        // An action that takes no value has none to print, and a target with no constraint says so rather than
        // looking like an empty pattern.
        Assert.Equal("disable_apps", PageRuleAction.NoValue(PageRuleActionIds.DisableApps).ToString());
        Assert.Equal("url (no constraint)", new PageRuleTarget { Target = PageRuleTargetKinds.Url }.ToString());
    }

    [Fact]
    public async Task ListAsync_applies_the_filters_it_was_given()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");
        harness.RespondWithResult("[]");

        await harness.Client.PageRules.ListAsync(new PageRuleListParameters
        {
            Status = PageRuleStatus.Disabled,
            Order = PageRuleListOrder.Priority,
            Direction = SortDirection.Descending,
            Match = FilterMatchMode.All,
        });
        Assert.Equal(
            "status=disabled&order=priority&direction=desc&match=all",
            harness.LastRequest.Query.TrimStart('?'));

        // And nothing at all when no filter is set, rather than four empty ones.
        await harness.Client.PageRules.ListAsync(new PageRuleListParameters());
        Assert.Equal(string.Empty, harness.LastRequest.Query.TrimStart('?'));
    }

    [Fact]
    public async Task ListAsync_reads_the_zone_it_was_given_and_refuses_to_guess_one()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");

        await harness.Client.PageRules.ListAsync(zoneId: "zone-other");
        Assert.Contains("/zones/zone-other/pagerules", harness.LastRequest.Path, StringComparison.Ordinal);

        using var noZone = CloudflareHarness.Create(options => options.ZoneId = null);
        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => noZone.Client.PageRules.ListAsync());
        Assert.NotEmpty(exception.Problems);
        Assert.Equal(0, noZone.RequestCount);
    }

    [Fact]
    public async Task GetAsync_reads_one_rule_and_answers_null_for_one_that_is_not_there()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(RichRule);
        harness.RespondWithError(System.Net.HttpStatusCode.NotFound, 10007, "Page rule not found");

        var rule = await harness.Client.PageRules.GetAsync("rule-1");
        Assert.EndsWith(
            "/zones/zone-5678/pagerules/rule-1",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Equal("rule-1", rule!.Id);

        // A rule the zone does not have reads as null rather than as an error, like every single read here.
        Assert.Null(await harness.Client.PageRules.GetAsync("not-there"));
        Assert.Equal(2, harness.RequestCount);
    }

    [Fact]
    public async Task CreateAsync_posts_the_rule_itself()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(RichRule);

        var created = await harness.Client.PageRules.CreateAsync(new PageRuleRequest
        {
            Targets = new List<PageRuleTarget> { PageRuleTarget.Matching("example.com/images/*") },
            Actions = new List<PageRuleAction> { PageRuleAction.On("browser_check") },
            Status = PageRuleStatus.Active,
        });

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.EndsWith("/zones/zone-5678/pagerules", harness.LastRequest.Path, StringComparison.Ordinal);

        // No wrapper, and no priority member when the caller left it to Cloudflare's default of 1.
        Assert.Equal(
            """{"targets":[{"target":"url","constraint":{"operator":"matches","value":"example.com/images/*"}}],"actions":[{"id":"browser_check","value":"on"}],"status":"active"}""",
            harness.LastRequest.Body);
        Assert.Equal("rule-1", created.Id);
    }

    [Fact]
    public async Task CreateAsync_can_forward_a_url_pattern_to_another_url()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(RichRule);

        await harness.Client.PageRules.CreateAsync(
            PageRuleRequest.Forwarding("example.com/blog/*", 301, "https://example.org/*"));

        // The forwarding action is the one page rules have that rulesets do not, and its value is an object:
        // the status code and where to send the request.
        Assert.Equal(
            """{"targets":[{"target":"url","constraint":{"operator":"matches","value":"example.com/blog/*"}}],"actions":[{"id":"forwarding_url","value":{"status_code":301,"url":"https://example.org/*"}}]}""",
            harness.LastRequest.Body);
    }

    [Fact]
    public void The_action_factories_build_the_value_shapes_their_ids_want()
    {
        Assert.Equal(
            """{"id":"disable_apps"}""",
            JsonSerializer.Serialize(PageRuleAction.NoValue(PageRuleActionIds.DisableApps), CloudflareJson.Default));
        Assert.Equal(
            """{"id":"cache_deception_armor","value":"on"}""",
            JsonSerializer.Serialize(PageRuleAction.On(PageRuleActionIds.CacheDeceptionArmor), CloudflareJson.Default));
        Assert.Equal(
            """{"id":"cache_deception_armor","value":"off"}""",
            JsonSerializer.Serialize(PageRuleAction.Off(PageRuleActionIds.CacheDeceptionArmor), CloudflareJson.Default));
        Assert.Equal(
            """{"id":"respect_strong_etag","value":"on"}""",
            JsonSerializer.Serialize(
                PageRuleAction.Flag(PageRuleActionIds.RespectStrongEtag, on: true),
                CloudflareJson.Default));
        Assert.Equal(
            """{"id":"edge_cache_ttl","value":7200}""",
            JsonSerializer.Serialize(
                PageRuleAction.Number(PageRuleActionIds.EdgeCacheTtl, 7200),
                CloudflareJson.Default));
        Assert.Equal(
            """{"id":"resolve_override","value":"origin.example.com"}""",
            JsonSerializer.Serialize(
                PageRuleAction.Text(PageRuleActionIds.ResolveOverride, "origin.example.com"),
                CloudflareJson.Default));

        // And read back whatever shape arrived, because Cloudflare is not consistent about which one an action
        // uses: a number of seconds has been seen as a word, and a flag as a boolean.
        Assert.Equal(7200, PageRuleAction.Text(PageRuleActionIds.EdgeCacheTtl, "7200").ValueAsNumber());
        Assert.Equal("1", PageRuleAction.Number(PageRuleActionIds.BrowserCheck, 1).ValueAsText());

        // A flag Cloudflare wrote as a real boolean reads as the word this library writes, so the two
        // spellings of one action meet instead of one of them being a null.
        var boolean = new PageRuleAction
        {
            Id = PageRuleActionIds.BrowserCheck,
            Value = JsonSerializer.SerializeToElement(true),
        };
        Assert.Equal("on", boolean.ValueAsText());

        Assert.Null(PageRuleAction.NoValue(PageRuleActionIds.DisableApps).ValueAsText());
        Assert.Null(PageRuleAction.NoValue(PageRuleActionIds.DisableApps).ValueAsNumber());
    }

    [Fact]
    public async Task CreateAsync_refuses_a_rule_that_would_match_everything_or_do_nothing()
    {
        using var harness = CloudflareHarness.Create();

        var refused = new Func<Task>[]
        {
            () => harness.Client.PageRules.CreateAsync(new PageRuleRequest()),
            // A target with nothing to match would apply the rule to every request in the zone.
            () => harness.Client.PageRules.CreateAsync(new PageRuleRequest
            {
                Targets = new List<PageRuleTarget> { new() { Target = PageRuleTargetKinds.Url } },
                Actions = new List<PageRuleAction> { PageRuleAction.On("browser_check") },
            }),
            () => harness.Client.PageRules.CreateAsync(new PageRuleRequest
            {
                Targets = new List<PageRuleTarget> { PageRuleTarget.Matching("   ") },
                Actions = new List<PageRuleAction> { PageRuleAction.On("browser_check") },
            }),
            // A rule that changes nothing is not a rule.
            () => harness.Client.PageRules.CreateAsync(new PageRuleRequest
            {
                Targets = new List<PageRuleTarget> { PageRuleTarget.Matching("example.com/*") },
            }),
            () => harness.Client.PageRules.CreateAsync(new PageRuleRequest
            {
                Targets = new List<PageRuleTarget> { PageRuleTarget.Matching("example.com/*") },
                Actions = new List<PageRuleAction> { new() },
            }),
            // Cloudflare publishes that a rule's actions redirect or override settings, but not both.
            () => harness.Client.PageRules.CreateAsync(new PageRuleRequest
            {
                Targets = new List<PageRuleTarget> { PageRuleTarget.Matching("example.com/*") },
                Actions = new List<PageRuleAction>
                {
                    PageRuleAction.Forwarding(301, "https://example.org/*"),
                    PageRuleAction.On(PageRuleActionIds.BrowserCheck),
                },
            }),
            () => harness.Client.PageRules.CreateAsync(new PageRuleRequest
            {
                Targets = new List<PageRuleTarget> { PageRuleTarget.Matching("example.com/*") },
                Actions = new List<PageRuleAction> { PageRuleAction.On("browser_check") },
                Priority = 0,
            }),
            () => harness.Client.PageRules.CreateAsync(new PageRuleRequest
            {
                Targets = new List<PageRuleTarget> { PageRuleTarget.Matching("example.com/*") },
                Actions = new List<PageRuleAction> { PageRuleAction.On("browser_check") },
                Status = PageRuleStatus.Unknown,
            }),
        };

        foreach (var call in refused)
        {
            var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(call);
            Assert.NotEmpty(exception.Problems);
        }

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task UpdateAsync_replaces_the_rule_and_EditAsync_changes_only_what_it_is_given()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(RichRule);
        harness.RespondWithResult(RichRule);

        await harness.Client.PageRules.UpdateAsync(
            "rule-1",
            new PageRuleRequest
            {
                Targets = new List<PageRuleTarget> { PageRuleTarget.Of(PageRuleTargetOperator.Contains, "/images/") },
                Actions = new List<PageRuleAction> { PageRuleAction.Number(PageRuleActionIds.EdgeCacheTtl, 3600) },
                Priority = 3,
            });

        Assert.Equal(HttpMethod.Put, harness.LastRequest.Method);
        Assert.EndsWith(
            "/zones/zone-5678/pagerules/rule-1",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Equal(
            """{"targets":[{"target":"url","constraint":{"operator":"contains","value":"/images/"}}],"actions":[{"id":"edge_cache_ttl","value":3600}],"priority":3}""",
            harness.LastRequest.Body);

        // The patch carries one field, and the body has one member: this is how a rule is disabled without
        // resending the targets and actions a PUT would have replaced them with.
        await harness.Client.PageRules.EditAsync(
            "rule-1",
            new PageRuleRequest { Status = PageRuleStatus.Disabled });

        Assert.Equal(HttpMethod.Patch, harness.LastRequest.Method);
        Assert.Equal("""{"status":"disabled"}""", harness.LastRequest.Body);

        // And a patch that would change nothing is refused rather than sent.
        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.PageRules.EditAsync("rule-1", new PageRuleRequest()));
        Assert.NotEmpty(exception.Problems);
        Assert.Equal(2, harness.RequestCount);
    }

    [Fact]
    public async Task DeleteAsync_deletes_by_id_and_sends_no_body()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"id":"rule-1"}""");

        await harness.Client.PageRules.DeleteAsync("rule-1");

        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.EndsWith(
            "/zones/zone-5678/pagerules/rule-1",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Null(harness.LastRequest.Body);
        Assert.Equal(1, harness.RequestCount);

        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.PageRules.DeleteAsync(" "));
        Assert.Equal(1, harness.RequestCount);
    }
}
