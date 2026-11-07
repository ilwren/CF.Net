using CFNet.Exceptions;
using CFNet.Models.Common;
using CFNet.Models.Turnstile;
using CFNet.UnitTests.Testing;
using Xunit;

namespace CFNet.UnitTests.Services;

/// <summary>
/// Turnstile widgets: <c>/accounts/{account_id}/challenges/widgets</c>. Worth pinning closely because three
/// things about this endpoint are not what its name suggests - the path says <c>challenges</c>, a list answers
/// widgets <b>without</b> their secrets where a single widget's answer carries one, and updating one is a
/// <c>PUT</c> that replaces the whole widget rather than patching a field of it.
/// </summary>
public class TurnstileServiceTests
{
    /// <summary>A widget exactly as a list answers one: every field, and no secret among them.</summary>
    private const string ListedWidget =
        """{"sitekey":"0x4AAAAAAADnPIDROrmt1Wwj","name":"cf-net widget","mode":"managed","domains":["example.com","*.example.com"],"region":"world","clearance_level":"no_clearance","bot_fight_mode":false,"ephemeral_id":false,"offlabel":false,"created_on":"2025-03-04T12:34:56.789Z","modified_on":"2025-03-05T01:02:03Z","deployed_via":"api","last_modified_via":"unknown"}""";

    [Fact]
    public async Task ListAsync_gets_the_accounts_widgets_with_its_paging_and_ordering()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage($"[{ListedWidget}]", page: 2, totalPages: 3, count: 1, totalCount: 5);

        var page = await harness.Client.Turnstile.ListAsync(
            new TurnstileWidgetListParameters
            {
                Page = 2,
                PerPage = 50,
                Filter = "cf-net",
                Order = TurnstileWidgetOrder.Name,
                Direction = SortDirection.Descending,
            });

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.EndsWith("/accounts/acct-1234/challenges/widgets", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal(
            "page=2&per_page=50&filter=cf-net&order=name&direction=desc",
            harness.LastRequest.Query.TrimStart('?'));

        var widget = Assert.Single(page.Items);
        Assert.Equal("0x4AAAAAAADnPIDROrmt1Wwj", widget.Sitekey);
        Assert.Equal("cf-net widget", widget.Name);
        Assert.Equal(TurnstileWidgetMode.Managed, widget.Mode);
        Assert.Equal(new[] { "example.com", "*.example.com" }, widget.Domains);
        Assert.Equal(TurnstileRegion.World, widget.Region);
        Assert.Equal(TurnstileClearanceLevel.NoClearance, widget.ClearanceLevel);
        Assert.False(widget.BotFightMode);
        Assert.False(widget.EphemeralId);
        Assert.False(widget.Offlabel);
        Assert.Equal(TurnstileDeploymentSource.Api, widget.DeployedVia);

        // Cloudflare's own word for "we do not know" is a value of this enum, and is not the same thing as a
        // value this library has not heard of - which is what Unknown is for.
        Assert.Equal(TurnstileDeploymentSource.NotRecorded, widget.LastModifiedVia);

        // A list answers no secret, which is why it is typed apart from a single widget.
        Assert.IsNotType<TurnstileWidget>(widget);

        // The timestamps are ISO 8601 here, and read either way.
        Assert.Equal("2025-03-04T12:34:56.789Z", widget.CreatedOn);
        Assert.Equal(
            new DateTimeOffset(2025, 3, 4, 12, 34, 56, 789, TimeSpan.Zero),
            widget.CreatedOnUtc!.Value);
        Assert.Equal(new DateTimeOffset(2025, 3, 5, 1, 2, 3, TimeSpan.Zero), widget.ModifiedOnUtc!.Value);

        Assert.Equal("cf-net widget (0x4AAAAAAADnPIDROrmt1Wwj)", widget.ToString());
        Assert.True(widget.AdditionalData is not { Count: > 0 });

        // And the page reports its own paging, so a caller can keep walking it.
        Assert.Equal(5, page.TotalCount);
        Assert.Equal(2, page.Page);
        Assert.True(page.HasMore);
        Assert.Equal(3, page.NextPage);
    }

    [Fact]
    public async Task ListAsync_sends_no_query_at_all_when_the_caller_gave_no_parameters()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");

        var page = await harness.Client.Turnstile.ListAsync();

        Assert.Equal(string.Empty, harness.LastRequest.Query.TrimStart('?'));
        Assert.Empty(page.Items);
        Assert.False(page.HasMore);
        Assert.Null(page.NextPage);
    }

    [Fact]
    public async Task ListAllAsync_walks_the_pages_until_one_says_it_is_the_last()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage($"[{ListedWidget},{ListedWidget}]", page: 1, totalPages: 2, count: 2, totalCount: 3);
        harness.RespondWithPage($"[{ListedWidget}]", page: 2, totalPages: 2, count: 1, totalCount: 3);

        var widgets = new List<TurnstileWidgetSummary>();
        await foreach (var widget in harness.Client.Turnstile.ListAllAsync(
                           new TurnstileWidgetListParameters { PerPage = 2 }))
        {
            widgets.Add(widget);
        }

        Assert.Equal(3, widgets.Count);
        Assert.Equal(2, harness.RequestCount);
        Assert.Equal("page=1&per_page=2", harness.Requests[0].Query.TrimStart('?'));
        Assert.Equal("page=2&per_page=2", harness.Requests[1].Query.TrimStart('?'));
    }

    [Fact]
    public async Task GetAsync_reads_a_single_widget_with_the_secret_a_list_leaves_out()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """{"sitekey":"0x4AAAAAAADnPIDROrmt1Wwj","name":"cf-net widget","mode":"invisible","domains":["example.com"],"region":"world","clearance_level":"no_clearance","bot_fight_mode":false,"ephemeral_id":true,"offlabel":false,"created_on":"2025-03-04T12:34:56.789Z","modified_on":"2025-03-05T01:02:03Z","secret":"0x4AAAAAAABcDeFgHiJkLmNoPqRsTuVwXyZ","deployed_via":"dashboard"}""");

        var widget = await harness.Client.Turnstile.GetAsync("0x4AAAAAAADnPIDROrmt1Wwj");

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/challenges/widgets/0x4AAAAAAADnPIDROrmt1Wwj",
            harness.LastRequest.Path,
            StringComparison.Ordinal);

        Assert.NotNull(widget);
        Assert.Equal(TurnstileWidgetMode.Invisible, widget!.Mode);
        Assert.Equal("0x4AAAAAAABcDeFgHiJkLmNoPqRsTuVwXyZ", widget.Secret);
        Assert.Equal(TurnstileDeploymentSource.Dashboard, widget.DeployedVia);
        Assert.Null(widget.LastModifiedVia);

        // The secret is a credential, so the one line this library prints about a widget leaves it out.
        Assert.DoesNotContain("0x4AAAAAAAB", widget.ToString(), StringComparison.Ordinal);
        Assert.True(widget.AdditionalData is not { Count: > 0 });
    }

    [Fact]
    public async Task GetAsync_answers_null_for_a_site_key_the_account_has_no_widget_under()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(System.Net.HttpStatusCode.NotFound, 7003, "Invalid sitekey");

        Assert.Null(await harness.Client.Turnstile.GetAsync("0x4AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));
        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task CreateAsync_posts_the_widget_and_reads_the_secret_Cloudflare_generated()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """{"sitekey":"0x4AAAAAAADnPIDROrmt1Wwj","name":"cf-net widget","mode":"managed","domains":["example.com","*.example.com"],"region":"world","secret":"0x4AAAAAAABcDeFgHiJkLmNoPqRsTuVwXyZ"}""");

        var widget = await harness.Client.Turnstile.CreateAsync(
            new TurnstileWidgetRequest
            {
                Name = "cf-net widget",
                Mode = TurnstileWidgetMode.Managed,
                Domains = new List<string> { "example.com", "*.example.com" },
                Region = TurnstileRegion.World,
                Offlabel = false,
            });

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.EndsWith("/accounts/acct-1234/challenges/widgets", harness.LastRequest.Path, StringComparison.Ordinal);

        // The body is the widget itself, with no wrapper, and only the fields the caller set: mode goes out as
        // the word Cloudflare publishes, offlabel as the one word it is on the wire, and the unset optionals
        // are not sent at all rather than sent as null.
        Assert.Equal(
            """{"name":"cf-net widget","mode":"managed","domains":["example.com","*.example.com"],"region":"world","offlabel":false}""",
            harness.LastRequest.Body);

        Assert.Equal("0x4AAAAAAADnPIDROrmt1Wwj", widget.Sitekey);
        Assert.Equal("0x4AAAAAAABcDeFgHiJkLmNoPqRsTuVwXyZ", widget.Secret);
    }

    [Fact]
    public async Task CreateAsync_refuses_a_widget_Cloudflare_could_not_make()
    {
        using var harness = CloudflareHarness.Create();

        var refused = new[]
        {
            new TurnstileWidgetRequest(),
            new TurnstileWidgetRequest { Name = "  ", Mode = TurnstileWidgetMode.Managed, Domains = new List<string> { "example.com" } },
            new TurnstileWidgetRequest { Name = "w", Domains = new List<string> { "example.com" } },
            new TurnstileWidgetRequest { Name = "w", Mode = TurnstileWidgetMode.Managed },
            new TurnstileWidgetRequest { Name = "w", Mode = TurnstileWidgetMode.Managed, Domains = new List<string>() },
            new TurnstileWidgetRequest { Name = "w", Mode = TurnstileWidgetMode.Managed, Domains = new List<string> { "example.com", " " } },
        };
        foreach (var request in refused)
        {
            var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
                () => harness.Client.Turnstile.CreateAsync(request));
            Assert.NotEmpty(exception.Problems);
        }

        // Nothing was refused by Cloudflare, because nothing was sent.
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task A_hostname_Cloudflare_may_refuse_is_still_sent_for_Cloudflare_to_refuse()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """{"sitekey":"0x4AAAAAAADnPIDROrmt1Wwj","name":"cf-net widget","mode":"managed","domains":["*.example.org"],"secret":"0x4AAAAAAABcDeFgHiJkLmNoPqRsTuVwXyZ"}""");

        // A wildcard subdomain is not a hostname Cloudflare accepts - a live PUT answered 400 with "invalid
        // value in `domains`, found invalid hostname" - but which hostnames it accepts is its call to make,
        // not this library's. The domain goes out exactly as it was given, and the refusal comes back as a
        // CloudflareApiException that quotes Cloudflare's own words.
        await harness.Client.Turnstile.CreateAsync(
            new TurnstileWidgetRequest
            {
                Name = "cf-net widget",
                Mode = TurnstileWidgetMode.Managed,
                Domains = new List<string> { "*.example.org" },
            });

        Assert.Contains("*.example.org", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateAsync_puts_the_whole_widget_because_a_put_replaces_it()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """{"sitekey":"0x4AAAAAAADnPIDROrmt1Wwj","name":"renamed","mode":"non-interactive","domains":["example.org"],"region":"china","secret":"0x4AAAAAAABcDeFgHiJkLmNoPqRsTuVwXyZ"}""");

        var widget = await harness.Client.Turnstile.UpdateAsync(
            "0x4AAAAAAADnPIDROrmt1Wwj",
            new TurnstileWidgetRequest
            {
                Name = "renamed",
                Mode = TurnstileWidgetMode.NonInteractive,
                Domains = new List<string> { "example.org" },
                Region = TurnstileRegion.China,
                EphemeralId = true,
                ClearanceLevel = TurnstileClearanceLevel.Interactive,
            });

        Assert.Equal(HttpMethod.Put, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/challenges/widgets/0x4AAAAAAADnPIDROrmt1Wwj",
            harness.LastRequest.Path,
            StringComparison.Ordinal);

        // Every field goes out in the order the request declares them, hyphens and underscores and all:
        // non-interactive is what Cloudflare calls the mode, clearance_level and ephemeral_id are what it
        // calls the rest, and nothing unset is sent.
        Assert.Equal(
            """{"name":"renamed","mode":"non-interactive","domains":["example.org"],"region":"china","clearance_level":"interactive","ephemeral_id":true}""",
            harness.LastRequest.Body);

        Assert.Equal(TurnstileWidgetMode.NonInteractive, widget.Mode);
        Assert.Equal(TurnstileRegion.China, widget.Region);
        Assert.Equal("0x4AAAAAAABcDeFgHiJkLmNoPqRsTuVwXyZ", widget.Secret);
    }

    [Fact]
    public async Task UpdateAsync_refuses_the_same_widget_CreateAsync_would()
    {
        using var harness = CloudflareHarness.Create();

        // A PUT with a field left out is not a field left alone, so the required ones are required here too.
        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Turnstile.UpdateAsync(
                "0x4AAAAAAADnPIDROrmt1Wwj",
                new TurnstileWidgetRequest { Name = "renamed" }));

        Assert.Equal(2, exception.Problems.Count);
        Assert.Contains("mode", exception.Problems[0], StringComparison.Ordinal);
        Assert.Contains("domains", exception.Problems[1], StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task RotateSecretAsync_posts_the_flag_and_answers_the_new_secret()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """{"sitekey":"0x4AAAAAAADnPIDROrmt1Wwj","name":"cf-net widget","mode":"managed","domains":["example.com"],"secret":"0x4AAAAAAACnewSecretNewSecretNewSecret1"}""");
        harness.RespondWithResult(
            """{"sitekey":"0x4AAAAAAADnPIDROrmt1Wwj","name":"cf-net widget","mode":"managed","domains":["example.com"],"secret":"0x4AAAAAAADanotherSecretAnotherSecret2"}""");

        var rotated = await harness.Client.Turnstile.RotateSecretAsync(
            "0x4AAAAAAADnPIDROrmt1Wwj",
            invalidateImmediately: true);

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/challenges/widgets/0x4AAAAAAADnPIDROrmt1Wwj/rotate_secret",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Equal("""{"invalidate_immediately":true}""", harness.LastRequest.Body);
        Assert.Equal("0x4AAAAAAACnewSecretNewSecretNewSecret1", rotated.Secret);

        // Leaving the flag alone sends no flag, which is what keeps Cloudflare's grace period in place.
        var again = await harness.Client.Turnstile.RotateSecretAsync("0x4AAAAAAADnPIDROrmt1Wwj");
        Assert.Equal("{}", harness.LastRequest.Body);
        Assert.Equal("0x4AAAAAAADanotherSecretAnotherSecret2", again.Secret);
        Assert.Equal(2, harness.RequestCount);
    }

    [Fact]
    public async Task DeleteAsync_deletes_by_site_key_and_tolerates_one_that_is_not_there()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """{"sitekey":"0x4AAAAAAADnPIDROrmt1Wwj","name":"cf-net widget","mode":"managed","domains":["example.com"]}""");
        harness.RespondWithError(System.Net.HttpStatusCode.NotFound, 7003, "Invalid sitekey");

        var deleted = await harness.Client.Turnstile.DeleteAsync("0x4AAAAAAADnPIDROrmt1Wwj");

        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/challenges/widgets/0x4AAAAAAADnPIDROrmt1Wwj",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Null(harness.LastRequest.Body);
        Assert.Equal("cf-net widget", deleted!.Name);

        // Deleting a widget that is not there is not an error: the call answers null rather than throwing.
        Assert.Null(await harness.Client.Turnstile.DeleteAsync("0x4AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));
        Assert.Equal(2, harness.RequestCount);
    }

    [Fact]
    public async Task A_site_key_is_encoded_as_the_path_segment_it_is()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("null");

        await harness.Client.Turnstile.GetAsync("0x4A AA/AA");

        // A real site key is alphanumeric and needs none of this, but the segment is encoded like every other
        // id in this library rather than pasted into a path.
        Assert.Contains("/challenges/widgets/0x4A%20AA%2FAA", harness.LastRequest.Uri!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_method_reads_the_account_it_was_given_rather_than_the_configured_one()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");
        harness.RespondWithResult(
            """{"sitekey":"s","name":"n","mode":"managed","domains":["example.com"],"secret":"x"}""");

        await harness.Client.Turnstile.ListAsync(accountId: "acct-other");
        Assert.Contains("/accounts/acct-other/challenges/widgets", harness.LastRequest.Path, StringComparison.Ordinal);

        var widget = await harness.Client.Turnstile.GetAsync("s", "acct-other");
        Assert.Contains("/accounts/acct-other/challenges/widgets/s", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal("x", widget!.Secret);
    }
}
