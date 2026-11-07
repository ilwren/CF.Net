using CFNet.Exceptions;
using CFNet.Models.Common;
using CFNet.Models.Firewall;
using CFNet.Serialization;
using CFNet.UnitTests.Testing;
using Xunit;

namespace CFNet.UnitTests.Services;

/// <summary>
/// The firewall's older surface: a zone's saved filters, and the IP Access rules of an account or of a zone.
/// Worth pinning closely because four things here are easy to get wrong - the filter create and bulk update take
/// a bare array rather than an object, the bulk delete is a <c>DELETE</c> with a body, the access rule list
/// narrows by a dotted <c>configuration.target</c>, and the access rules are served from two scopes where the
/// zone one is only used when a caller asks for it by name.
/// </summary>
public class FirewallServiceTests
{
    /// <summary>A filter as a create or a get answers one.</summary>
    private const string SavedFilter =
        """{"id":"filter-1","description":"office traffic","expression":"(ip.src eq 192.0.2.1)","paused":false,"ref":"cfnet"}""";

    /// <summary>An access rule as a create answers one, with the modes Cloudflare says it may be switched to.</summary>
    private const string SavedRule =
        """{"id":"rule-1","mode":"block","allowed_modes":["block","challenge","managed_challenge"],"configuration":{"target":"ip","value":"192.0.2.1"},"notes":"cf-net test","created_on":"2026-11-07T10:00:00Z","modified_on":"2026-11-07T11:00:00Z","scope":{"id":"user-1","email":"someone@example.com","type":"user"}}""";

    [Fact]
    public async Task CreateFiltersAsync_posts_a_bare_array_and_reads_back_the_ids_Cloudflare_assigned()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult($"[{SavedFilter}]");

        var saved = await harness.Client.Firewall.CreateFiltersAsync(new[]
        {
            FirewallFilterRequest
                .For("(ip.src eq 192.0.2.1)", "office traffic")
                .SetPaused()
                .Tagged("cfnet"),
        });

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.EndsWith("/zones/zone-5678/filters", harness.LastRequest.Path, StringComparison.Ordinal);

        // The body is the array itself - there is no wrapper and no single-filter create, so one filter is an
        // array of one - and it carries no id, because Cloudflare assigns that.
        Assert.Equal(
            """[{"description":"office traffic","expression":"(ip.src eq 192.0.2.1)","paused":true,"ref":"cfnet"}]""",
            harness.LastRequest.Body);

        var filter = Assert.Single(saved);
        Assert.Equal("filter-1", filter.Id);
        Assert.Equal("(ip.src eq 192.0.2.1)", filter.Expression);
        Assert.Equal("office traffic", filter.Description);
        Assert.False(filter.Paused);
        Assert.Equal("cfnet", filter.Ref);
        Assert.True(filter.AdditionalData is not { Count: > 0 });
        Assert.Equal("filter filter-1 (office traffic): (ip.src eq 192.0.2.1)", filter.ToString());
    }

    [Fact]
    public async Task CreateFiltersAsync_refuses_a_call_that_would_save_nothing_before_sending_it()
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Firewall.CreateFiltersAsync(Array.Empty<FirewallFilterRequest>()));
        Assert.Equal(0, harness.RequestCount);

        // A filter with no expression matches nothing, and one whose description or tag is longer than the
        // length Cloudflare publishes is refused there, so both are refused here with every reason at once.
        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Firewall.CreateFiltersAsync(new[]
            {
                new FirewallFilterRequest
                {
                    Description = new string('d', 501),
                    Ref = new string('r', 51),
                },
            }));

        Assert.Contains(exception.Problems, problem => problem.Contains("needs an expression", StringComparison.Ordinal));
        Assert.Contains(exception.Problems, problem => problem.Contains("500", StringComparison.Ordinal));
        Assert.Contains(exception.Problems, problem => problem.Contains("50", StringComparison.Ordinal));
        Assert.Equal(3, exception.Problems.Count);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task ListFiltersAsync_sends_every_field_filter_and_the_paging()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage($"[{SavedFilter}]", 2, 4, 20, 73);

        var page = await harness.Client.Firewall.ListFiltersAsync(new FirewallFilterListParameters
        {
            Id = "filter-1",
            Description = "office traffic",
            Expression = "ip.src",
            Paused = false,
            Ref = "cfnet",
            Page = 2,
            PerPage = 20,
        });

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.EndsWith("/zones/zone-5678/filters", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal(
            "page=2&per_page=20&id=filter-1&description=office%20traffic&expression=ip.src&paused=false&ref=cfnet",
            harness.LastRequest.Query.TrimStart('?'));

        var filter = Assert.Single(page.Items);
        Assert.Equal("filter-1", filter.Id);
        Assert.Equal(2, page.Info!.Page);
        Assert.Equal(4, page.Info.TotalPages);
        Assert.Equal(73, page.Info.TotalCount);
        Assert.True(page.HasMore);
        Assert.Equal(3, page.NextPage);
    }

    [Fact]
    public async Task ListAllFiltersAsync_walks_the_pages_until_Cloudflare_says_there_are_no_more()
    {
        using var harness = CloudflareHarness.Create();
        var second = SavedFilter.Replace("\"id\":\"filter-1\"", "\"id\":\"filter-2\"", StringComparison.Ordinal);
        harness.RespondWithPage($"[{SavedFilter},{second}]", 1, 2, 2, 3);
        harness.RespondWithPage(
            $"[{second.Replace("\"id\":\"filter-2\"", "\"id\":\"filter-3\"", StringComparison.Ordinal)}]",
            2,
            2,
            1,
            3);

        var ids = new List<string?>();
        await foreach (var filter in harness.Client.Firewall.ListAllFiltersAsync())
        {
            ids.Add(filter.Id);
        }

        Assert.Equal(new string?[] { "filter-1", "filter-2", "filter-3" }, ids);
        Assert.Equal(2, harness.RequestCount);
        Assert.Equal("page=2", harness.LastRequest.Query.TrimStart('?').Split('&')[^1]);
    }

    [Fact]
    public async Task GetFilterAsync_reads_one_filter_and_answers_null_for_one_that_is_not_there()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(SavedFilter);
        harness.RespondWithError(System.Net.HttpStatusCode.NotFound, 10007, "Filter not found");

        var filter = await harness.Client.Firewall.GetFilterAsync("filter-1");

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.EndsWith(
            "/zones/zone-5678/filters/filter-1",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Equal("filter-1", filter!.Id);

        Assert.Null(await harness.Client.Firewall.GetFilterAsync("filter-404"));
    }

    [Fact]
    public async Task UpdateFilterAsync_puts_the_whole_filter_and_leaves_the_id_to_the_path()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(SavedFilter);

        await harness.Client.Firewall.UpdateFilterAsync(
            "filter-1",
            new FirewallFilterRequest { Expression = "(ip.src eq 192.0.2.7)", Paused = false });

        Assert.Equal(HttpMethod.Put, harness.LastRequest.Method);
        Assert.EndsWith(
            "/zones/zone-5678/filters/filter-1",
            harness.LastRequest.Path,
            StringComparison.Ordinal);

        // A PUT replaces the filter, so a field left out is a field removed - and the id belongs to the path,
        // which is why the body has two members and not three.
        Assert.Equal(
            """{"expression":"(ip.src eq 192.0.2.7)","paused":false}""",
            harness.LastRequest.Body);
    }

    [Fact]
    public async Task UpdateFilterAsync_refuses_a_body_that_names_a_different_filter_than_its_path()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Firewall.UpdateFilterAsync(
                "filter-1",
                FirewallFilterRequest.WithId("filter-2", "(ip.src eq 192.0.2.7)")));

        // Two identifiers would be two instructions, and Cloudflare has to be told which one was meant.
        Assert.Contains("filter-1", exception.Problems[0], StringComparison.Ordinal);
        Assert.Contains("filter-2", exception.Problems[0], StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);

        // The same identifier in both places is not a disagreement, so it is sent.
        harness.RespondWithResult(SavedFilter);
        await harness.Client.Firewall.UpdateFilterAsync(
            "filter-1",
            FirewallFilterRequest.WithId("filter-1", "(ip.src eq 192.0.2.7)"));
        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task UpdateFiltersAsync_puts_an_array_whose_members_carry_their_own_ids()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult($"[{SavedFilter}]");

        await harness.Client.Firewall.UpdateFiltersAsync(new[]
        {
            FirewallFilterRequest.WithId("filter-1", "(ip.src eq 192.0.2.7)"),
            FirewallFilterRequest.WithId("filter-2", "(ip.src eq 192.0.2.8)"),
        });

        Assert.Equal(HttpMethod.Put, harness.LastRequest.Method);
        Assert.EndsWith("/zones/zone-5678/filters", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal(
            """[{"id":"filter-1","expression":"(ip.src eq 192.0.2.7)"},""" +
            """{"id":"filter-2","expression":"(ip.src eq 192.0.2.8)"}]""",
            harness.LastRequest.Body);

        // A bulk update says which filter each member is in the body, so a member with no id is refused before
        // the call is spent - Cloudflare would have nothing to apply it to.
        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Firewall.UpdateFiltersAsync(new[]
            {
                FirewallFilterRequest.For("(ip.src eq 192.0.2.9)"),
            }));

        Assert.Contains(exception.Problems, problem => problem.Contains("needs an id", StringComparison.Ordinal));
        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task DeleteFilterAsync_answers_with_the_id_and_tolerates_a_filter_that_is_not_there()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"id":"filter-1"}""");
        harness.RespondWithError(System.Net.HttpStatusCode.NotFound, 10007, "Filter not found");

        var deleted = await harness.Client.Firewall.DeleteFilterAsync("filter-1");

        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.EndsWith(
            "/zones/zone-5678/filters/filter-1",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Equal("filter-1", deleted!.Id);
        Assert.Equal("deleted filter filter-1", deleted.ToString());

        // Removing something that is already gone is what a cleanup block does twice, so it answers null
        // rather than throwing at the second attempt.
        Assert.Null(await harness.Client.Firewall.DeleteFilterAsync("filter-404"));
    }

    [Fact]
    public async Task DeleteFiltersAsync_sends_the_ids_in_a_body_because_a_bulk_delete_has_nowhere_else_to_put_them()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""[{"id":"filter-1"},{"id":"filter-2"}]""");

        var deleted = await harness.Client.Firewall.DeleteFiltersAsync(new[] { "filter-1", "filter-2" });

        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.EndsWith("/zones/zone-5678/filters", harness.LastRequest.Path, StringComparison.Ordinal);

        // A DELETE with a body, and the wire name is singular for a list: {"id":["…"]}, exactly as Cloudflare's
        // own schema publishes it.
        Assert.Equal("""{"id":["filter-1","filter-2"]}""", harness.LastRequest.Body);

        Assert.Equal(2, deleted.Count);
        Assert.Equal("filter-1", deleted[0].Id);
        Assert.Equal("filter-2", deleted[1].Id);

        // A bulk delete of nothing would spend a call and remove nothing, so it is refused here.
        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Firewall.DeleteFiltersAsync(new[] { "filter-3", " " }));
        Assert.Contains(exception.Problems, problem => problem.Contains("blank", StringComparison.Ordinal));
        Assert.Equal(1, harness.RequestCount);

        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Firewall.DeleteFiltersAsync(Array.Empty<string>()));
        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task CreateAccessRuleAsync_posts_the_configuration_and_the_mode_to_the_account_path()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(SavedRule);

        var rule = await harness.Client.Firewall.CreateAccessRuleAsync(
            AccessRuleRequest.For(
                AccessRuleConfiguration.ForIp("192.0.2.1"),
                AccessRuleMode.Block,
                "cf-net test"));

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/firewall/access_rules/rules",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Equal(
            """{"configuration":{"target":"ip","value":"192.0.2.1"},"mode":"block","notes":"cf-net test"}""",
            harness.LastRequest.Body);

        Assert.Equal("rule-1", rule.Id);
        Assert.Equal(AccessRuleMode.Block, rule.Mode);
        Assert.Equal(AccessRuleTarget.Ip, rule.Configuration!.Target);
        Assert.Equal("192.0.2.1", rule.Configuration.Value);
        Assert.Equal("cf-net test", rule.Notes);
        Assert.Equal(new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero), rule.CreatedOnUtc);
        Assert.Equal(new DateTimeOffset(2026, 11, 7, 11, 0, 0, TimeSpan.Zero), rule.ModifiedOnUtc);
        Assert.True(rule.AdditionalData is not { Count: > 0 });

        // Which modes a rule may be switched to is Cloudflare's answer rather than the whole vocabulary, so a
        // caller can ask before sending a change it would be refused for.
        Assert.Equal(3, rule.AllowedModes!.Count);
        Assert.True(rule.Allows(AccessRuleMode.ManagedChallenge));
        Assert.False(rule.Allows(AccessRuleMode.Whitelist));

        Assert.Equal(AccessRuleScopeType.User, rule.Scope!.Type);
        Assert.Equal("someone@example.com", rule.Scope.Email);
        Assert.Equal("user someone@example.com", rule.Scope.ToString());
        Assert.Equal("access rule rule-1: block ip 192.0.2.1 - cf-net test", rule.ToString());
    }

    [Fact]
    public async Task Access_rules_use_the_zone_path_only_when_a_zone_is_passed_explicitly()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage($"[{SavedRule}]", 1, 1, 1, 1);
        harness.RespondWithPage($"[{SavedRule}]", 1, 1, 1, 1);

        // The harness has a default zone configured, and this call still goes to the account: a method that
        // changed scope depending on how the client was set up would be worse than one that asks.
        await harness.Client.Firewall.ListAccessRulesAsync();
        Assert.EndsWith(
            "/accounts/acct-1234/firewall/access_rules/rules",
            harness.LastRequest.Path,
            StringComparison.Ordinal);

        await harness.Client.Firewall.ListAccessRulesAsync(zoneId: "zone-5678");
        Assert.EndsWith(
            "/zones/zone-5678/firewall/access_rules/rules",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListAccessRulesAsync_sends_its_filters_including_the_dotted_configuration()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage($"[{SavedRule}]", 2, 3, 10, 25);

        var page = await harness.Client.Firewall.ListAccessRulesAsync(new AccessRuleListParameters
        {
            Configuration = AccessRuleListConfiguration.Of(AccessRuleFilterTarget.Ip, "192.0.2.1"),
            Mode = AccessRuleMode.Block,
            Notes = "cf-net",
            Order = AccessRuleOrder.ConfigurationValue,
            Direction = SortDirection.Descending,
            Match = FilterMatchMode.All,
            Page = 2,
            PerPage = 10,
        });

        // A nested filter goes out as dotted names, the way the zone list sends account.id, and the order words
        // are dotted too because that is what Cloudflare publishes for them.
        Assert.Equal(
            "page=2&per_page=10&configuration.target=ip&configuration.value=192.0.2.1&mode=block" +
            "&notes=cf-net&order=configuration.value&direction=desc&match=all",
            harness.LastRequest.Query.TrimStart('?'));

        var rule = Assert.Single(page.Items);
        Assert.Equal("rule-1", rule.Id);
        Assert.Equal(3, page.Info!.TotalPages);
        Assert.Equal(25, page.Info.TotalCount);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task GetAccessRuleAsync_reads_one_rule_and_answers_null_for_one_that_is_not_there()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(SavedRule);
        harness.RespondWithError(System.Net.HttpStatusCode.NotFound, 10007, "Rule not found");

        var rule = await harness.Client.Firewall.GetAccessRuleAsync("rule-1", zoneId: "zone-5678");

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.EndsWith(
            "/zones/zone-5678/firewall/access_rules/rules/rule-1",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Equal("rule-1", rule!.Id);

        Assert.Null(await harness.Client.Firewall.GetAccessRuleAsync("rule-404"));
    }

    [Fact]
    public async Task EditAccessRuleAsync_patches_the_whole_rule_because_Cloudflare_requires_both_fields()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(SavedRule);

        await harness.Client.Firewall.EditAccessRuleAsync(
            "rule-1",
            AccessRuleRequest.For(AccessRuleConfiguration.ForCountry("GB"), AccessRuleMode.Challenge));

        Assert.Equal(HttpMethod.Patch, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/firewall/access_rules/rules/rule-1",
            harness.LastRequest.Path,
            StringComparison.Ordinal);

        // Spelled PATCH and behaving like a PUT: the published schema requires the configuration and the mode
        // on this call, so there is no changing one of them on its own.
        Assert.Equal(
            """{"configuration":{"target":"country","value":"GB"},"mode":"challenge"}""",
            harness.LastRequest.Body);

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Firewall.EditAccessRuleAsync("rule-1", new AccessRuleRequest()));
        Assert.Contains(exception.Problems, problem => problem.Contains("needs a configuration", StringComparison.Ordinal));
        Assert.Contains(exception.Problems, problem => problem.Contains("needs a mode", StringComparison.Ordinal));
        Assert.Equal(2, exception.Problems.Count);
        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task DeleteAccessRuleAsync_answers_with_the_id_and_tolerates_a_rule_that_is_not_there()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"id":"rule-1"}""");
        harness.RespondWithError(System.Net.HttpStatusCode.NotFound, 10007, "Rule not found");

        var deleted = await harness.Client.Firewall.DeleteAccessRuleAsync("rule-1");

        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/firewall/access_rules/rules/rule-1",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Equal("rule-1", deleted!.Id);
        Assert.Equal("deleted access rule rule-1", deleted.ToString());

        Assert.Null(await harness.Client.Firewall.DeleteAccessRuleAsync("rule-404"));
    }

    [Fact]
    public void The_configuration_factories_name_the_five_targets_and_refuse_what_Cloudflare_will_not_take()
    {
        Assert.Equal(AccessRuleTarget.Ip, AccessRuleConfiguration.ForIp("192.0.2.1").Target);
        Assert.Equal(AccessRuleTarget.Ipv6, AccessRuleConfiguration.ForIpv6("2001:db8::1").Target);
        Assert.Equal(AccessRuleTarget.IpRange, AccessRuleConfiguration.ForRange("192.0.2.0/24").Target);
        Assert.Equal(AccessRuleTarget.Asn, AccessRuleConfiguration.ForAsn("AS12345").Target);
        Assert.Equal(AccessRuleTarget.Country, AccessRuleConfiguration.ForCountry("GB").Target);

        Assert.Equal("ip 192.0.2.1", AccessRuleConfiguration.ForIp("192.0.2.1").ToString());
        Assert.Equal("country GB", AccessRuleConfiguration.ForCountry("GB").ToString());
        Assert.Equal(
            "access rule request: block ip6 2001:db8::1",
            AccessRuleRequest.Blocking(AccessRuleConfiguration.ForIpv6("2001:db8::1")).ToString());
        Assert.Equal(
            "access rule request: whitelist ip 192.0.2.1",
            AccessRuleRequest.Skipping(AccessRuleConfiguration.ForIp("192.0.2.1")).ToString());

        // The prefix lengths are published, so a range outside them is refused here rather than answered with
        // a refusal from Cloudflare: /16 and /24 for IPv4, /32, /48 and /64 for IPv6.
        Assert.All(
            new[] { "192.0.2.0/16", "192.0.2.0/24", "2001:db8::/32", "2001:db8::/48", "2001:db8::/64" },
            range => AccessRuleConfiguration.ForRange(range).EnsureValid());

        var wrongPrefix = Assert.Throws<CloudflareConfigurationException>(
            () => AccessRuleConfiguration.ForRange("192.0.2.0/23").EnsureValid());
        Assert.Contains("/16, /24", wrongPrefix.Problems[0], StringComparison.Ordinal);

        var wrongIpv6Prefix = Assert.Throws<CloudflareConfigurationException>(
            () => AccessRuleConfiguration.ForRange("2001:db8::/56").EnsureValid());
        Assert.Contains("/32, /48, /64", wrongIpv6Prefix.Problems[0], StringComparison.Ordinal);

        var noPrefix = Assert.Throws<CloudflareConfigurationException>(
            () => AccessRuleConfiguration.ForRange("192.0.2.1").EnsureValid());
        Assert.Contains("no prefix length", noPrefix.Problems[0], StringComparison.Ordinal);

        // A country target takes an ISO-3166-1 alpha-2 code, and a three-letter one is the alpha-3 code that
        // belongs to a different standard.
        var country = Assert.Throws<CloudflareConfigurationException>(
            () => AccessRuleConfiguration.ForCountry("GBR").EnsureValid());
        Assert.Contains("two-letter", country.Problems[0], StringComparison.Ordinal);

        var empty = Assert.Throws<CloudflareConfigurationException>(
            () => new AccessRuleConfiguration().EnsureValid());
        Assert.Equal(2, empty.Problems.Count);
    }

    [Fact]
    public async Task A_mode_or_target_Cloudflare_invents_reads_as_unknown_and_is_refused_on_the_way_out()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """{"id":"rule-9","mode":"teleport","allowed_modes":["teleport"],"configuration":{"target":"continent","value":"EU"}}""");

        var rule = await harness.Client.Firewall.GetAccessRuleAsync("rule-9");

        // Reading is forgiving, so a word this version has not met becomes Unknown rather than taking the whole
        // rule down with it - and ToString says so instead of printing a member name that means nothing.
        Assert.Equal(AccessRuleMode.Unknown, rule!.Mode);
        Assert.Equal(AccessRuleTarget.Unknown, rule.Configuration!.Target);
        Assert.Contains(AccessRuleMode.Unknown, rule.AllowedModes!);
        Assert.Equal("access rule rule-9: an unpublished mode an unpublished target EU", rule.ToString());

        // Writing is not: sending a word Cloudflare has never published would silently ask for the wrong thing.
        Assert.Throws<InvalidOperationException>(
            () => CloudflareEnumConverter<AccessRuleMode>.ToWire(AccessRuleMode.Unknown));
        Assert.Throws<InvalidOperationException>(
            () => CloudflareEnumConverter<AccessRuleTarget>.ToWire(AccessRuleTarget.Unknown));

        // And the list's own filter vocabulary is shorter than the rule's: Cloudflare types it separately and
        // leaves ip6 out, so a caller cannot narrow a list to one IPv6 address and this library does not let
        // one try.
        Assert.Equal(
            new[] { "Unknown", "Ip", "IpRange", "Asn", "Country" },
            Enum.GetNames<AccessRuleFilterTarget>());
        Assert.Equal(6, Enum.GetNames<AccessRuleTarget>().Length);
    }

    [Fact]
    public void The_whitelist_word_is_what_the_api_still_says_for_what_the_dashboard_calls_skip()
    {
        // Cloudflare renamed this in the product and not in the API, so the wire word is the older one and the
        // member is named for what it does now - with the older word pinned here so a rename cannot slip
        // through as a behaviour change.
        Assert.Equal("whitelist", CloudflareEnumConverter<AccessRuleMode>.ToWire(AccessRuleMode.Whitelist));
        Assert.Equal("js_challenge", CloudflareEnumConverter<AccessRuleMode>.ToWire(AccessRuleMode.JavaScriptChallenge));
        Assert.Equal("managed_challenge", CloudflareEnumConverter<AccessRuleMode>.ToWire(AccessRuleMode.ManagedChallenge));
        Assert.Equal("ip_range", CloudflareEnumConverter<AccessRuleTarget>.ToWire(AccessRuleTarget.IpRange));
        Assert.Equal("ip6", CloudflareEnumConverter<AccessRuleTarget>.ToWire(AccessRuleTarget.Ipv6));
        Assert.Equal("configuration.target", CloudflareEnumConverter<AccessRuleOrder>.ToWire(AccessRuleOrder.ConfigurationTarget));
        Assert.Equal("organization", CloudflareEnumConverter<AccessRuleScopeType>.ToWire(AccessRuleScopeType.Organization));
    }

    [Fact]
    public void The_filter_request_factories_say_what_they_would_save()
    {
        Assert.Equal(
            "filter request: (ip.src eq 192.0.2.1)",
            FirewallFilterRequest.For("(ip.src eq 192.0.2.1)").ToString());
        Assert.Equal(
            "filter request filter-1 (office traffic): (ip.src eq 192.0.2.1)",
            FirewallFilterRequest.WithId("filter-1", "(ip.src eq 192.0.2.1)", "office traffic").ToString());
        Assert.Equal(
            "bulk delete of 2 filter(s)",
            FirewallFilterBulkDeleteRequest.Of(new[] { "filter-1", "filter-2" }).ToString());

        // Copying a filter that was read is how a bulk update is built: change one field, keep the rest, and
        // the id comes along because the body is what says which filter changes.
        var read = new FirewallFilter
        {
            Id = "filter-1",
            Description = "office traffic",
            Expression = "(ip.src eq 192.0.2.1)",
            Paused = false,
            Ref = "cfnet",
        };

        var copy = FirewallFilterRequest.FromFilter(read);
        Assert.Equal("filter-1", copy.Id);
        Assert.Equal("office traffic", copy.Description);
        Assert.Equal("(ip.src eq 192.0.2.1)", copy.Expression);
        Assert.False(copy.Paused);
        Assert.Equal("cfnet", copy.Ref);
        copy.EnsureValidForBulkUpdate();
    }
}
