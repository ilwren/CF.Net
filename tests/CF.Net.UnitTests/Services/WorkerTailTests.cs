using System.Net;
using System.Text.Json;
using CFNet.Exceptions;
using CFNet.Models.Workers;
using CFNet.Serialization;
using CFNet.UnitTests.Testing;
using Xunit;

namespace CFNet.UnitTests.Services;

/// <summary>
/// Worker tails. The shape asserted here is the current one - an id, an expiry and the url a consumer
/// connects to - answered the same way by both the create and the read, so a script has the one tail this
/// model describes rather than a list of them.
/// </summary>
public class WorkerTailTests
{
    /// <summary>A tail as Cloudflare answers one.</summary>
    private const string LiveTail =
        """{"id":"tail-1","expires_at":"2026-11-07T02:03:04Z","url":"wss://tail.example.workers.dev/acct-1234/tail-1"}""";

    [Fact]
    public async Task CreateTailAsync_posts_to_the_script_and_sends_no_body()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(LiveTail);

        var tail = await harness.Client.Workers.CreateTailAsync("my-worker");

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/workers/scripts/my-worker/tails",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        // The script in the path is the whole request, so there is nothing to send.
        Assert.True(string.IsNullOrEmpty(harness.LastRequest.Body));

        Assert.Equal("tail-1", tail.Id);
        Assert.Equal("wss://tail.example.workers.dev/acct-1234/tail-1", tail.Url);
        Assert.Equal(new DateTimeOffset(2026, 11, 7, 2, 3, 4, TimeSpan.Zero), tail.ExpiresAt);
        Assert.Equal("tail-1 (expires 2026-11-07 02:03:04Z)", tail.ToString());
    }

    [Fact]
    public async Task GetTailAsync_reads_the_tail_a_script_has()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(LiveTail);

        var tail = await harness.Client.Workers.GetTailAsync("my-worker");

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Contains("/workers/scripts/my-worker/tails", harness.LastRequest.Path, StringComparison.Ordinal);
        Assert.Equal("tail-1", tail!.Id);
    }

    [Fact]
    public async Task GetTailAsync_returns_null_when_the_script_is_not_being_tailed()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.NotFound, 10007, "Tail not found");

        // Not being tailed is the normal state of a script, and what an expired tail reads as too.
        Assert.Null(await harness.Client.Workers.GetTailAsync("my-worker"));
    }

    [Fact]
    public async Task DeleteTailAsync_addresses_the_tail_by_id()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("{\"success\":true,\"errors\":[],\"messages\":[],\"result\":{}}");

        await harness.Client.Workers.DeleteTailAsync("my-worker", "tail-1");

        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/workers/scripts/my-worker/tails/tail-1",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task A_tail_needs_both_the_script_and_the_tail_id()
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Workers.CreateTailAsync("  "));
        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Workers.DeleteTailAsync("my-worker", ""));

        // Both are refused before a request is built, so a missing id never reaches Cloudflare as a 404.
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public void A_tail_reports_whether_it_has_expired()
    {
        Assert.False(new WorkerTail { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5) }.HasExpired);
        Assert.True(new WorkerTail { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5) }.HasExpired);

        // No expiry reported is not an expired tail: it says nothing has been read yet.
        Assert.False(new WorkerTail().HasExpired);
        Assert.Equal(" (no expiry reported)", new WorkerTail().ToString());
    }

    [Fact]
    public void A_tail_keeps_the_fields_the_current_schema_does_not_list()
    {
        // Earlier versions of this API answered with the dispatch namespace, the script name and the tail's
        // consumers. They are not in the shape Cloudflare publishes now, and nothing is dropped for it.
        var tail = JsonSerializer.Deserialize<WorkerTail>(
            """{"id":"tail-1","expires_at":"2026-11-07T02:03:04Z","url":"wss://t/1","dispatch_namespace":"acct-1234","script_name":"my-worker","consumers":[]}""",
            CloudflareJson.Default)!;

        Assert.Equal("tail-1", tail.Id);
        Assert.NotNull(tail.AdditionalData);
        Assert.Contains("dispatch_namespace", tail.AdditionalData!.Keys, StringComparer.Ordinal);
        Assert.Contains("script_name", tail.AdditionalData.Keys, StringComparer.Ordinal);
        Assert.Contains("consumers", tail.AdditionalData.Keys, StringComparer.Ordinal);
    }
}
