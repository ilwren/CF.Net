using CFNet.Exceptions;
using CFNet.Models.Workers;
using CFNet.UnitTests.Testing;
using Xunit;

namespace CFNet.UnitTests.Services;

/// <summary>
/// Workers routes - the zone-scoped half of this service - and the account's Workers settings beside them.
/// Routes are worth pinning closely because three things about them are easy to get wrong: the path is under
/// <c>zones/{zone_id}</c> rather than <c>accounts/{account_id}</c>, the list takes no filter so "this Worker's
/// routes" is a local answer, and a route with no script is a real kind of route rather than an unfinished one.
/// </summary>
public class WorkerRouteTests
{
    /// <summary>One route that runs a Worker and one that runs nothing, exactly as a list answers them.</summary>
    private const string ListedRoutes =
        """[{"id":"route-1","pattern":"app.example.com/api/*","script":"my-worker"},{"id":"route-2","pattern":"app.example.com/images/cat.png"}]""";

    [Fact]
    public async Task ListRoutesAsync_reads_the_zones_routes_under_the_zone_path()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(ListedRoutes);

        var routes = await harness.Client.Workers.ListRoutesAsync();

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.EndsWith(
            "/zones/zone-5678/workers/routes",
            harness.LastRequest.Path,
            StringComparison.Ordinal);

        // No paging and no filters: this endpoint takes none.
        Assert.Equal(string.Empty, harness.LastRequest.Query.TrimStart('?'));

        Assert.Equal(2, routes.Count);
        Assert.Equal("route-1", routes[0].Id);
        Assert.Equal("app.example.com/api/*", routes[0].Pattern);
        Assert.Equal("my-worker", routes[0].Script);
        Assert.False(routes[0].BypassesWorkers);
        Assert.Equal("app.example.com/api/* -> my-worker", routes[0].ToString());
        Assert.True(routes[0].AdditionalData is not { Count: > 0 });

        // The second route carries no script at all, which is a bypass rather than something missing.
        Assert.Equal("route-2", routes[1].Id);
        Assert.Null(routes[1].Script);
        Assert.True(routes[1].BypassesWorkers);
        Assert.Equal("app.example.com/images/cat.png -> no script", routes[1].ToString());
    }

    [Fact]
    public async Task ListRoutesAsync_reads_the_zone_it_was_given_and_refuses_to_guess_one()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");

        var routes = await harness.Client.Workers.ListRoutesAsync("zone-other");
        Assert.Contains(
            "/zones/zone-other/workers/routes",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Empty(routes);

        // With no default configured and none given, there is no zone to ask - and routes belong to one.
        using var noZone = CloudflareHarness.Create(options => options.ZoneId = null);
        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => noZone.Client.Workers.ListRoutesAsync());
        Assert.NotEmpty(exception.Problems);
        Assert.Equal(0, noZone.RequestCount);
    }

    [Fact]
    public async Task ListRoutesForWorkerAsync_picks_one_workers_routes_out_of_the_zones()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """[{"id":"route-1","pattern":"app.example.com/api/*","script":"my-worker"},{"id":"route-2","pattern":"app.example.com/images/cat.png"},{"id":"route-3","pattern":"other.example.com/*","script":"another-worker"},{"id":"route-4","pattern":"app.example.com/old/*","script":"my-worker"}]""");

        var mine = await harness.Client.Workers.ListRoutesForWorkerAsync("my-worker");

        // One call, not one per route: the endpoint takes no filter, so the picking is done here.
        Assert.Equal(1, harness.RequestCount);
        Assert.Equal(new[] { "route-1", "route-4" }, mine.Select(route => route.Id));

        // A bypass route belongs to no Worker, whatever its pattern looks like.
        Assert.DoesNotContain(mine, route => route.BypassesWorkers);
    }

    [Fact]
    public async Task GetRouteAsync_reads_one_route_and_answers_null_for_one_that_is_not_there()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"id":"route-1","pattern":"app.example.com/api/*","script":"my-worker"}""");
        harness.RespondWithError(System.Net.HttpStatusCode.NotFound, 10007, "Route not found");

        var route = await harness.Client.Workers.GetRouteAsync("route-1");
        Assert.EndsWith(
            "/zones/zone-5678/workers/routes/route-1",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Equal("app.example.com/api/*", route!.Pattern);

        // A route the zone does not have reads as null rather than as an error, like every single read here.
        Assert.Null(await harness.Client.Workers.GetRouteAsync("not-there"));
        Assert.Equal(2, harness.RequestCount);
    }

    [Fact]
    public async Task CreateRouteAsync_posts_the_pattern_and_the_script_it_runs()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"id":"route-9","pattern":"app.example.com/api/*","script":"my-worker"}""");

        var created = await harness.Client.Workers.CreateRouteAsync(
            WorkerRouteRequest.For("app.example.com/api/*", "my-worker"));

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.EndsWith(
            "/zones/zone-5678/workers/routes",
            harness.LastRequest.Path,
            StringComparison.Ordinal);

        // No wrapper: the body is the route.
        Assert.Equal(
            """{"pattern":"app.example.com/api/*","script":"my-worker"}""",
            harness.LastRequest.Body);
        Assert.Equal("route-9", created.Id);
        Assert.Equal("my-worker", created.Script);
    }

    [Fact]
    public async Task CreateRouteAsync_can_create_a_route_that_runs_nothing()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"id":"route-9","pattern":"app.example.com/images/cat.png"}""");

        var bypass = await harness.Client.Workers.CreateRouteAsync(
            WorkerRouteRequest.Bypassing("app.example.com/images/cat.png"));

        // The absent script is the point of the call: it negates the less specific patterns around it, and
        // this library's writer leaves the key out rather than sending a null for it.
        Assert.Equal(
            """{"pattern":"app.example.com/images/cat.png"}""",
            harness.LastRequest.Body);
        Assert.True(bypass.BypassesWorkers);
    }

    [Fact]
    public async Task UpdateRouteAsync_puts_the_whole_route_so_a_null_script_takes_the_worker_off()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"id":"route-1","pattern":"app.example.com/api/v2/*","script":"my-worker"}""");
        harness.RespondWithResult("""{"id":"route-1","pattern":"app.example.com/api/v2/*"}""");

        var updated = await harness.Client.Workers.UpdateRouteAsync(
            "route-1",
            WorkerRouteRequest.For("app.example.com/api/v2/*", "my-worker"));

        Assert.Equal(HttpMethod.Put, harness.LastRequest.Method);
        Assert.EndsWith(
            "/zones/zone-5678/workers/routes/route-1",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Equal(
            """{"pattern":"app.example.com/api/v2/*","script":"my-worker"}""",
            harness.LastRequest.Body);
        Assert.Equal("my-worker", updated.Script);

        // The same PUT with no script is a replacement, not a patch: the Worker comes off the route.
        var turned = await harness.Client.Workers.UpdateRouteAsync(
            "route-1",
            WorkerRouteRequest.For("app.example.com/api/v2/*"));
        Assert.Equal("""{"pattern":"app.example.com/api/v2/*"}""", harness.LastRequest.Body);
        Assert.True(turned.BypassesWorkers);
    }

    [Fact]
    public async Task CreateRouteAsync_refuses_the_patterns_the_published_rules_rule_out()
    {
        using var harness = CloudflareHarness.Create();

        var refused = new[]
        {
            new WorkerRouteRequest(),
            WorkerRouteRequest.For("   "),
            WorkerRouteRequest.For("https://app.example.com/?anything"),
            WorkerRouteRequest.For("app.example.com/api#fragment"),
            WorkerRouteRequest.For("app.example.com/my api/*"),
            WorkerRouteRequest.For("app.example.com/api/*", "   "),
        };

        foreach (var route in refused)
        {
            var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
                () => harness.Client.Workers.CreateRouteAsync(route));
            Assert.NotEmpty(exception.Problems);
        }

        // Everything above was refused locally, so nothing went out - including for the update.
        Assert.Equal(0, harness.RequestCount);
        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Workers.UpdateRouteAsync("route-1", WorkerRouteRequest.For("a?b")));
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task DeleteRouteAsync_deletes_by_id_and_sends_no_body()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"id":"route-1"}""");

        await harness.Client.Workers.DeleteRouteAsync("route-1");

        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.EndsWith(
            "/zones/zone-5678/workers/routes/route-1",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Null(harness.LastRequest.Body);
        Assert.Equal(1, harness.RequestCount);

        // Cloudflare answers the id it removed and nothing else worth modelling, so nothing is returned - and
        // an id that is not one is refused locally rather than sent as a path segment.
        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Workers.DeleteRouteAsync(" "));
    }

    [Fact]
    public async Task GetAccountSettingsAsync_reads_the_accounts_workers_defaults()
    {
        using var harness = CloudflareHarness.Create();
        // "standard" is a word this endpoint's own documentation does not list, and one a live free-plan
        // account answers, so the model keeps the string rather than an enum of the documented two.
        harness.RespondWithResult("""{"default_usage_model":"standard","green_compute":false}""");

        var settings = await harness.Client.Workers.GetAccountSettingsAsync();

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/workers/account-settings",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Equal(WorkerUsageModels.Standard, settings.DefaultUsageModel);
        Assert.False(settings.GreenCompute);
        Assert.Equal(
            "default_usage_model=standard, green_compute=False",
            settings.ToString());
        Assert.True(settings.AdditionalData is not { Count: > 0 });
    }

    [Fact]
    public async Task UpdateAccountSettingsAsync_puts_the_settings_it_was_given()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"default_usage_model":"bundled","green_compute":true}""");
        harness.RespondWithResult("{}");

        var updated = await harness.Client.Workers.UpdateAccountSettingsAsync(
            WorkersAccountSettings.Of(WorkerUsageModels.Bundled, greenCompute: true));

        Assert.Equal(HttpMethod.Put, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/workers/account-settings",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Equal(
            """{"default_usage_model":"bundled","green_compute":true}""",
            harness.LastRequest.Body);
        Assert.Equal(WorkerUsageModels.Bundled, updated.DefaultUsageModel);
        Assert.True(updated.GreenCompute);

        // A PUT is a replacement, so settings with nothing set send nothing and leave both to Cloudflare.
        var emptied = await harness.Client.Workers.UpdateAccountSettingsAsync(new WorkersAccountSettings());
        Assert.Equal("{}", harness.LastRequest.Body);
        Assert.Equal("default_usage_model=(unset), green_compute=(unset)", emptied.ToString());
    }

    [Fact]
    public async Task The_account_settings_read_can_be_handed_straight_back_to_the_write()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"default_usage_model":"standard","green_compute":false}""");
        harness.RespondWithResult("""{"default_usage_model":"standard","green_compute":true}""");

        var read = await harness.Client.Workers.GetAccountSettingsAsync();
        Assert.Equal(WorkerUsageModels.Standard, read.DefaultUsageModel);

        // The two shapes are one type on purpose: this is how a setting is changed without guessing at the
        // other one, and it is why the body of the write is exactly the answer of the read.
        read.GreenCompute = true;
        var written = await harness.Client.Workers.UpdateAccountSettingsAsync(read);

        Assert.Equal(
            """{"default_usage_model":"standard","green_compute":true}""",
            harness.LastRequest.Body);
        Assert.True(written.GreenCompute);
        Assert.Equal(read.DefaultUsageModel, written.DefaultUsageModel);
    }

    [Fact]
    public async Task Every_route_and_settings_method_reads_the_account_or_zone_it_was_given()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");
        harness.RespondWithResult("""{"id":"route-1","pattern":"p","script":"s"}""");
        harness.RespondWithResult("null");
        harness.RespondWithResult("{}");

        await harness.Client.Workers.ListRoutesForWorkerAsync("my-worker", "zone-other");
        Assert.Contains("/zones/zone-other/", harness.LastRequest.Path, StringComparison.Ordinal);

        await harness.Client.Workers.CreateRouteAsync(WorkerRouteRequest.For("p", "s"), "zone-other");
        Assert.Contains("/zones/zone-other/workers/routes", harness.LastRequest.Path, StringComparison.Ordinal);

        await harness.Client.Workers.DeleteRouteAsync("route-1", "zone-other");
        Assert.Contains(
            "/zones/zone-other/workers/routes/route-1",
            harness.LastRequest.Path,
            StringComparison.Ordinal);

        await harness.Client.Workers.GetAccountSettingsAsync("acct-other");
        Assert.Contains("/accounts/acct-other/", harness.LastRequest.Path, StringComparison.Ordinal);
    }
}
