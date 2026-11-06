using System.Net;
using System.Text.Json;
using CFNet.Exceptions;
using CFNet.Models.Workers;
using CFNet.Serialization;
using CFNet.UnitTests.Testing;
using Xunit;

namespace CFNet.UnitTests.Services;

/// <summary>
/// Workers Observability destinations. The point of these tests is the parts that are easy to get wrong from
/// the outside: field names that are camelCase on the wire where the rest of Cloudflare is snake_case, an
/// update that requires a field even when it is not what is changing, and a delete that answers with the
/// thing it deleted.
/// </summary>
public class WorkersObservabilityServiceTests
{
    /// <summary>A destination as every endpoint on this resource answers one.</summary>
    private const string LiveDestination =
        """{"slug":"dest-1","name":"my-destination","enabled":true,"scripts":["my-worker"],"configuration":{"type":"logpush","url":"https://logs.example.com/ingest","headers":{"Authorization":"Bearer t"},"logpushDataset":"opentelemetry-logs","jobStatus":{"error_message":"","last_complete":"2026-11-06T08:00:00Z","last_error":""}}}""";

    [Fact]
    public async Task CreateDestinationAsync_sends_what_the_schema_requires_and_reads_the_slug_back()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(LiveDestination);

        var created = await harness.Client.WorkersObservability.CreateDestinationAsync(
            CreateObservabilityDestinationRequest.ForLogpush(
                "my-destination",
                "https://logs.example.com/ingest",
                ObservabilityLogpushDatasets.OpenTelemetryLogs,
                enabled: true));

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/workers/observability/destinations",
            harness.LastRequest.Path,
            StringComparison.Ordinal);

        // logpushDataset is camelCase on the wire, headers is present even though it carries nothing, and an
        // unset skipPreflightCheck is absent rather than false.
        Assert.Equal(
            """{"name":"my-destination","enabled":true,"configuration":{"type":"logpush","url":"https://logs.example.com/ingest","headers":{},"logpushDataset":"opentelemetry-logs"}}""",
            harness.LastRequest.Body);

        Assert.Equal("dest-1", created.Slug);
        Assert.Equal("my-destination", created.Name);
        Assert.Equal("my-destination (dest-1)", created.ToString());
    }

    [Fact]
    public async Task CreateDestinationAsync_can_skip_the_delivery_check()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(LiveDestination);

        var request = CreateObservabilityDestinationRequest.ForLogpush(
            "my-destination",
            "https://logs.example.com/ingest",
            ObservabilityLogpushDatasets.OpenTelemetryTraces);
        request.SkipPreflightCheck = true;

        await harness.Client.WorkersObservability.CreateDestinationAsync(request);

        Assert.Contains("\"skipPreflightCheck\":true", harness.LastRequest.Body, StringComparison.Ordinal);

        // Enabled is required but not defaulted: the factory leaves a destination off unless it is asked not
        // to, so nothing starts flowing somewhere a caller did not choose.
        Assert.Contains("\"enabled\":false", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateDestinationAsync_refuses_a_request_missing_what_the_schema_requires()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.WorkersObservability.CreateDestinationAsync(
                new CreateObservabilityDestinationRequest()));

        // Name, Enabled and Configuration, each named in its own problem.
        Assert.Equal(3, exception.Problems.Count);
        Assert.Contains("Name", exception.Problems[0], StringComparison.Ordinal);
        Assert.Contains("Enabled", exception.Problems[1], StringComparison.Ordinal);
        Assert.Contains("Configuration", exception.Problems[2], StringComparison.Ordinal);

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task CreateDestinationAsync_refuses_a_configuration_that_cannot_deliver_anything()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.WorkersObservability.CreateDestinationAsync(
                new CreateObservabilityDestinationRequest
                {
                    Name = "my-destination",
                    Enabled = false,
                    Configuration = new ObservabilityDestinationConfiguration(),
                }));

        // Type, Url, Headers and the dataset - which cannot be changed after the create, so it has to be
        // right here.
        Assert.Equal(4, exception.Problems.Count);
        Assert.Contains("Headers", exception.Problems[2], StringComparison.Ordinal);
        Assert.Contains("LogpushDataset", exception.Problems[3], StringComparison.Ordinal);

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task UpdateDestinationAsync_sends_the_enabled_flag_it_is_required_to_send()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(LiveDestination);

        await harness.Client.WorkersObservability.UpdateDestinationAsync(
            "dest-1",
            new UpdateObservabilityDestinationRequest
            {
                Enabled = true,
                Configuration = new ObservabilityDestinationConfiguration
                {
                    Type = ObservabilityDestinationTypes.Logpush,
                    Url = "https://logs.example.com/other",
                    Headers = new Dictionary<string, string?> { ["X-Cf-Net"] = "unit-test" },
                    ReplaceHeaders = true,
                },
            });

        Assert.Equal(HttpMethod.Patch, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/workers/observability/destinations/dest-1",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Equal(
            """{"enabled":true,"configuration":{"type":"logpush","url":"https://logs.example.com/other","headers":{"X-Cf-Net":"unit-test"},"replaceHeaders":true}}""",
            harness.LastRequest.Body);
    }

    [Fact]
    public async Task UpdateDestinationAsync_writes_an_enabled_flag_that_is_false()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(LiveDestination);

        // A required boolean that is false still has to reach the wire, which is why Enabled is a bool? the
        // caller sets rather than a bool that defaults to the value Cloudflare requires least.
        await harness.Client.WorkersObservability.UpdateDestinationAsync(
            "dest-1",
            new UpdateObservabilityDestinationRequest { Enabled = false });

        Assert.Equal("""{"enabled":false}""", harness.LastRequest.Body);
    }

    [Fact]
    public async Task UpdateDestinationAsync_refuses_an_update_without_the_enabled_flag()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.WorkersObservability.UpdateDestinationAsync(
                "dest-1",
                new UpdateObservabilityDestinationRequest
                {
                    Configuration = new ObservabilityDestinationConfiguration
                    {
                        Type = ObservabilityDestinationTypes.Logpush,
                        Url = "https://logs.example.com/other",
                        Headers = new Dictionary<string, string?>(),
                    },
                }));

        Assert.Contains("Enabled", Assert.Single(exception.Problems), StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task An_update_that_sends_a_configuration_has_to_send_a_complete_one()
    {
        using var harness = CloudflareHarness.Create();

        // A configuration on an update is held to the same shape as one on a create: type, url and headers,
        // all of them, even though the only field being changed here is the address.
        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.WorkersObservability.UpdateDestinationAsync(
                "dest-1",
                new UpdateObservabilityDestinationRequest
                {
                    Enabled = false,
                    Configuration = new ObservabilityDestinationConfiguration
                    {
                        Url = "https://logs.example.com/other",
                    },
                }));

        Assert.Equal(2, exception.Problems.Count);
        Assert.Contains("Type", exception.Problems[0], StringComparison.Ordinal);
        Assert.Contains("Headers", exception.Problems[1], StringComparison.Ordinal);

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task A_destination_with_no_headers_still_sends_an_empty_object()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(LiveDestination);

        // Observed live: configuration.headers is required and has to be an object, so a destination that
        // authenticates with nothing at all still sends {} rather than leaving the field out.
        await harness.Client.WorkersObservability.CreateDestinationAsync(
            CreateObservabilityDestinationRequest.ForLogpush(
                "my-destination",
                "https://logs.example.com/ingest",
                ObservabilityLogpushDatasets.OpenTelemetryMetrics));

        Assert.Contains(""headers":{}", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListDestinationsAsync_sends_the_camel_case_filters_this_endpoint_uses()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");

        var destinations = await harness.Client.WorkersObservability.ListDestinationsAsync(
            new ObservabilityDestinationListParameters
            {
                Order = ObservabilityDestinationOrders.Descending,
                OrderBy = ObservabilityDestinationOrderBy.Created,
                Page = 2,
                PerPage = 25,
            });

        Assert.Empty(destinations);
        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/workers/observability/destinations",
            harness.LastRequest.Path,
            StringComparison.Ordinal);

        // orderBy and perPage, not order_by and per_page: this endpoint spells its query differently from
        // most of the API, and the parameters class exists so that difference lives in one place.
        Assert.Equal("order=desc&orderBy=created&page=2&perPage=25", harness.LastRequest.Query.TrimStart('?'));
    }

    [Fact]
    public async Task ListDestinationsAsync_sends_no_query_at_all_without_filters()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult($"[{LiveDestination}]");

        var destinations = await harness.Client.WorkersObservability.ListDestinationsAsync();

        Assert.Equal(string.Empty, harness.LastRequest.Query.TrimStart('?'));
        Assert.Equal("dest-1", Assert.Single(destinations).Slug);
    }

    [Fact]
    public async Task ListDestinationsAsync_refuses_a_filter_value_the_endpoint_does_not_accept()
    {
        using var harness = CloudflareHarness.Create();

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.WorkersObservability.ListDestinationsAsync(
                new ObservabilityDestinationListParameters { Order = "sideways", OrderBy = "whenever", Page = 0 }));

        Assert.Equal(3, exception.Problems.Count);
        Assert.Contains("Order", exception.Problems[0], StringComparison.Ordinal);
        Assert.Contains("OrderBy", exception.Problems[1], StringComparison.Ordinal);
        Assert.Contains("Page", exception.Problems[2], StringComparison.Ordinal);

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task DeleteDestinationAsync_answers_with_the_destination_it_deleted()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(LiveDestination);

        var deleted = await harness.Client.WorkersObservability.DeleteDestinationAsync("dest-1");

        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/workers/observability/destinations/dest-1",
            harness.LastRequest.Path,
            StringComparison.Ordinal);

        // Not an empty success: what was removed comes back with the call that removed it.
        Assert.Equal("dest-1", deleted.Slug);
        Assert.Equal("my-destination", deleted.Name);
    }

    [Fact]
    public async Task A_destination_needs_a_slug_to_be_changed_or_deleted()
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.WorkersObservability.DeleteDestinationAsync("  "));
        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.WorkersObservability.UpdateDestinationAsync(
                "",
                new UpdateObservabilityDestinationRequest { Enabled = true }));

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public void A_destination_reads_its_job_status_and_the_workers_exporting_to_it()
    {
        var destination = JsonSerializer.Deserialize<ObservabilityDestination>(
            LiveDestination,
            CloudflareJson.Default)!;

        Assert.Equal("my-worker", Assert.Single(destination.Scripts!));

        var configuration = destination.Configuration!;
        Assert.Equal(ObservabilityDestinationTypes.Logpush, configuration.Type);
        Assert.Equal(ObservabilityLogpushDatasets.OpenTelemetryLogs, configuration.LogpushDataset);
        Assert.Equal("Bearer t", configuration.Headers!["Authorization"]);

        // The job status is snake_case inside an otherwise camelCase configuration.
        var jobStatus = configuration.JobStatus!;
        Assert.Equal("2026-11-06T08:00:00Z", jobStatus.LastComplete);
        Assert.Equal(string.Empty, jobStatus.ErrorMessage);
        Assert.Equal(string.Empty, jobStatus.LastError);
    }

    [Fact]
    public void A_destination_keeps_any_field_it_does_not_model()
    {
        var destination = JsonSerializer.Deserialize<ObservabilityDestination>(
            """{"slug":"dest-1","name":"n","enabled":false,"configuration":{"type":"logpush","url":"https://x/y","logpushDataset":"opentelemetry-metrics","compression":"gzip"}}""",
            CloudflareJson.Default)!;

        Assert.NotNull(destination.Configuration!.AdditionalData);
        Assert.Contains("compression", destination.Configuration.AdditionalData!.Keys, StringComparer.Ordinal);
    }
}
