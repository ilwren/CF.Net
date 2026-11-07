using System.Net;
using CFNet.Exceptions;
using CFNet.Models.Workers;
using CFNet.UnitTests.Testing;
using Xunit;

namespace CFNet.UnitTests.Services;

/// <summary>
/// A Worker's script-level settings. The interesting half is what a PATCH carries: only the fields that are
/// set, because this library never writes a JSON null, so an unset field is left alone rather than cleared
/// and clearing a list means sending an empty one.
/// </summary>
public class WorkerScriptSettingsTests
{
    [Fact]
    public async Task UpdateSettingsAsync_patches_script_settings_with_only_what_changed()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """{"logpush":true,"tags":["t1"],"tail_consumers":[],"observability":{"enabled":true}}""");

        var updated = await harness.Client.Workers.UpdateSettingsAsync(
            "hello",
            new WorkerScriptSettings { Logpush = true });

        Assert.Equal(HttpMethod.Patch, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/workers/scripts/hello/script-settings",
            harness.LastRequest.Path,
            StringComparison.Ordinal);

        // The three unset fields are absent rather than null, which is what makes this a patch and not a
        // replacement of the script's whole settings.
        Assert.Equal("""{"logpush":true}""", harness.LastRequest.Body);

        Assert.True(updated.Logpush);
        Assert.Equal("t1", Assert.Single(updated.Tags!));
        Assert.True(updated.Observability!.Enabled);
    }

    [Fact]
    public async Task UpdateSettingsAsync_sends_the_whole_observability_block()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"observability":{"enabled":true}}""");

        await harness.Client.Workers.UpdateSettingsAsync(
            "hello",
            new WorkerScriptSettings
            {
                Observability = new WorkerObservability
                {
                    Enabled = true,
                    HeadSamplingRate = 0.1,
                    RedactQueryString = true,
                    Issues = new WorkerObservabilityIssues { Enabled = false },
                    Logs = new WorkerObservabilityLogs
                    {
                        Enabled = true,
                        InvocationLogs = true,
                        Destinations = new List<string> { "my-destination" },
                        HeadSamplingRate = 1,
                        Persist = true,
                    },
                    Traces = new WorkerObservabilityTraces
                    {
                        Enabled = true,
                        Persist = false,
                        PropagationPolicy = WorkerTracePropagationPolicies.Accept,
                    },
                },
            });

        Assert.Equal(
            """{"observability":{"enabled":true,"head_sampling_rate":0.1,"redact_query_string":true,"issues":{"enabled":false},"logs":{"enabled":true,"invocation_logs":true,"destinations":["my-destination"],"head_sampling_rate":1,"persist":true},"traces":{"enabled":true,"persist":false,"propagation_policy":"accept"}}}""",
            harness.LastRequest.Body);
    }

    [Fact]
    public async Task Clearing_tags_and_tail_consumers_sends_empty_lists_because_a_null_is_never_written()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"tags":[],"tail_consumers":[]}""");

        await harness.Client.Workers.UpdateSettingsAsync(
            "hello",
            new WorkerScriptSettings
            {
                Tags = new List<string>(),
                TailConsumers = new List<WorkerTailConsumer>(),
            });

        // An empty list is how a caller clears these on purpose; there is no null to send.
        Assert.Equal("""{"tags":[],"tail_consumers":[]}""", harness.LastRequest.Body);
    }

    [Fact]
    public async Task UpdateSettingsAsync_refuses_a_request_with_nothing_to_change()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Workers.UpdateSettingsAsync("hello", new WorkerScriptSettings()));

        Assert.Contains("empty body", Assert.Single(exception.Problems), StringComparison.Ordinal);

        // Nothing is set, so nothing is sent: an empty PATCH would be a write that cannot change anything.
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task UpdateSettingsAsync_needs_a_script_to_change_the_settings_of()
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Workers.UpdateSettingsAsync(
                "  ",
                new WorkerScriptSettings { Logpush = false }));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => harness.Client.Workers.UpdateSettingsAsync("hello", null!));

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task GetSettingsAsync_reads_the_observability_settings_the_patch_takes()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """{"logpush":false,"tags":[],"tail_consumers":[],"observability":{"enabled":true,"head_sampling_rate":0.5,"redact_query_string":true,"issues":{"enabled":true},"logs":{"enabled":true,"invocation_logs":false,"destinations":["my-destination"],"head_sampling_rate":1,"persist":true},"traces":{"enabled":false,"propagation_policy":"authenticated"}}}""");

        var settings = await harness.Client.Workers.GetSettingsAsync("hello");

        var observability = settings!.Observability!;
        Assert.True(observability.Enabled);
        Assert.Equal(0.5, observability.HeadSamplingRate);
        Assert.True(observability.RedactQueryString);

        Assert.True(observability.Issues!.Enabled);

        Assert.True(observability.Logs!.Enabled);
        Assert.False(observability.Logs.InvocationLogs);
        Assert.True(observability.Logs.Persist);
        Assert.Equal(1, observability.Logs.HeadSamplingRate);
        Assert.Equal("my-destination", Assert.Single(observability.Logs.Destinations!));

        Assert.False(observability.Traces!.Enabled);
        Assert.Equal(
            WorkerTracePropagationPolicies.Authenticated,
            observability.Traces.PropagationPolicy);
    }

    [Fact]
    public async Task GetSettingsAsync_returns_null_when_the_script_is_not_there()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.NotFound, 10007, "Worker not found");

        Assert.Null(await harness.Client.Workers.GetSettingsAsync("hello"));
    }
}
