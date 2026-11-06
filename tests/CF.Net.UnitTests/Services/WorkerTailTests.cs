using System.Net;
using System.Text.Json;
using CFNet.Exceptions;
using CFNet.Models.Workers;
using CFNet.Serialization;
using CFNet.UnitTests.Testing;
using Xunit;

namespace CFNet.UnitTests.Services;

/// <summary>
/// Worker tails. The two halves of this resource answer differently: creating a tail answers with one tail
/// carrying the url a consumer connects to, while reading the script's tails answers with an array whose
/// items carry the dispatch namespace, the script name and the consumers instead. Both are asserted here as
/// the live API answers them, not as one shape assumed for both.
/// </summary>
public class WorkerTailTests
{
    /// <summary>A tail as the create answers one.</summary>
    private const string CreatedTail =
        """{"id":"tail-1","expires_at":"2026-11-07T02:03:04Z","url":"wss://tail.example.workers.dev/acct-1234/tail-1"}""";

    /// <summary>Tails as the list answers them - no url, but the script and its consumers.</summary>
    private const string ListedTails =
        """[{"id":"tail-1","dispatch_namespace":"acct-1234","script_name":"my-worker","consumers":[{"service":"collector","environment":"production"}]}]""";

    [Fact]
    public async Task CreateTailAsync_posts_to_the_script_and_sends_no_body()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(CreatedTail);

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
    public async Task ListTailsAsync_reads_the_array_the_script_answers_with()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(ListedTails);

        var tails = await harness.Client.Workers.ListTailsAsync("my-worker");

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/workers/scripts/my-worker/tails",
            harness.LastRequest.Path,
            StringComparison.Ordinal);

        var tail = Assert.Single(tails);
        Assert.Equal("tail-1", tail.Id);
        Assert.Equal("acct-1234", tail.DispatchNamespace);
        Assert.Equal("my-worker", tail.ScriptName);

        var consumer = Assert.Single(tail.Consumers!);
        Assert.Equal("collector", consumer.Service);
        Assert.Equal("production", consumer.Environment);
        Assert.Null(consumer.Namespace);
        Assert.Equal("collector", consumer.ToString());

        // The url and the expiry belong to the create's answer, and a listed tail does not carry them.
        Assert.Null(tail.Url);
        Assert.Null(tail.ExpiresAt);
        Assert.Equal("tail-1 (no expiry reported)", tail.ToString());
    }

    [Fact]
    public async Task ListTailsAsync_is_empty_when_the_script_is_not_being_tailed()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");

        // This is what a live call answers for a script that has never been tailed: 200 and an empty array.
        Assert.Empty(await harness.Client.Workers.ListTailsAsync("my-worker"));
    }

    [Fact]
    public async Task ListTailsAsync_is_empty_when_the_script_is_not_found()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.NotFound, 10007, "Worker not found");

        // A missing script has no tails either, and that is not worth an exception to a caller listing them.
        Assert.Empty(await harness.Client.Workers.ListTailsAsync("my-worker"));
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
            () => harness.Client.Workers.ListTailsAsync(""));
        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Workers.DeleteTailAsync("my-worker", ""));

        // All three are refused before a request is built, so a missing id never reaches Cloudflare as a 404.
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public void A_tail_reports_whether_it_has_expired()
    {
        Assert.False(new WorkerTail { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5) }.HasExpired);
        Assert.True(new WorkerTail { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5) }.HasExpired);

        // No expiry reported is not an expired tail: a listed tail carries none at all.
        Assert.False(new WorkerTail().HasExpired);
    }

    [Fact]
    public void A_tail_consumer_in_a_dispatch_namespace_says_so()
    {
        var consumer = new WorkerTailConsumer
        {
            Service = "collector",
            Namespace = "platform",
        };

        Assert.Equal("collector (platform)", consumer.ToString());
    }

    [Fact]
    public void A_tail_keeps_any_field_it_does_not_model()
    {
        var tail = JsonSerializer.Deserialize<WorkerTail>(
            """{"id":"tail-1","script_name":"my-worker","events_per_minute":42}""",
            CloudflareJson.Default)!;

        Assert.Equal("my-worker", tail.ScriptName);
        Assert.NotNull(tail.AdditionalData);
        Assert.Contains("events_per_minute", tail.AdditionalData!.Keys, StringComparer.Ordinal);
        Assert.Equal(42, tail.AdditionalData["events_per_minute"].GetInt32());
    }
}
