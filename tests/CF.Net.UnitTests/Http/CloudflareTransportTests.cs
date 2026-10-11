using System.Net;
using System.Text.Json;
using CFNet.Configuration;
using CFNet.Exceptions;
using CFNet.Http;
using CFNet.Models.Common;
using CFNet.Models.Zones;
using CFNet.UnitTests.Testing;
using Xunit;

namespace CFNet.UnitTests.Http;

public class CloudflareTransportAuthenticationTests
{
    [Fact]
    public async Task An_api_token_is_sent_as_a_bearer_authorization_header()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");

        await harness.Client.Zones.ListAsync();

        Assert.Equal("Bearer " + CloudflareHarness.TestApiToken, harness.LastRequest.Header("Authorization"));
        Assert.Null(harness.LastRequest.Header("X-Auth-Key"));
        Assert.Null(harness.LastRequest.Header("X-Auth-Email"));
    }

    [Fact]
    public async Task A_global_api_key_is_sent_as_the_two_legacy_headers()
    {
        using var harness = CloudflareHarness.CreateWithGlobalKey();
        harness.RespondWithResult("[]");

        await harness.Client.Zones.ListAsync();

        Assert.Equal("global-key-1234", harness.LastRequest.Header("X-Auth-Key"));
        Assert.Equal("owner@example.com", harness.LastRequest.Header("X-Auth-Email"));

        // Sending both schemes at once would let a stale token override the key.
        Assert.Null(harness.LastRequest.Header("Authorization"));
    }

    [Fact]
    public async Task Whitespace_around_credentials_is_trimmed_before_sending()
    {
        using var harness = CloudflareHarness.Create(options => options.ApiToken = "  spaced-token  ");
        harness.RespondWithResult("[]");

        await harness.Client.Zones.ListAsync();

        Assert.Equal("Bearer spaced-token", harness.LastRequest.Header("Authorization"));
    }

    [Fact]
    public async Task The_user_agent_is_sent()
    {
        using var harness = CloudflareHarness.Create(options => options.UserAgent = "CF.Net.Tests/1.0");
        harness.RespondWithResult("[]");

        await harness.Client.Zones.ListAsync();

        Assert.Equal("CF.Net.Tests/1.0", harness.LastRequest.Header("User-Agent"));
    }

    [Fact]
    public async Task An_empty_user_agent_is_not_sent()
    {
        using var harness = CloudflareHarness.Create(options => options.UserAgent = string.Empty);
        harness.RespondWithResult("[]");

        await harness.Client.Zones.ListAsync();

        Assert.Null(harness.LastRequest.Header("User-Agent"));
    }

    [Fact]
    public async Task Per_request_headers_do_not_replace_authentication()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("{\"name\":\"assets\"}");

        await harness.Client.R2.CreateBucketAsync("assets", accountId: CloudflareHarness.TestAccountId);

        Assert.Equal("Bearer " + CloudflareHarness.TestApiToken, harness.LastRequest.Header("Authorization"));
    }

    [Fact]
    public async Task Credentials_are_validated_before_anything_is_sent()
    {
        using var harness = CloudflareHarness.Create(options => options.ApiToken = null);

        var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.Zones.ListAsync());

        Assert.Contains("credentials", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task The_ConfigureRequest_hook_sees_every_outgoing_message()
    {
        var seen = new List<string>();
        using var harness = CloudflareHarness.Create(options =>
            options.ConfigureRequest = message =>
            {
                seen.Add(message.Method.Method);
                message.Headers.TryAddWithoutValidation("X-Custom", "yes");
            });
        harness.RespondWithResult("[]");

        await harness.Client.Zones.ListAsync();

        Assert.Equal(new[] { "GET" }, seen);
        Assert.Equal("yes", harness.LastRequest.Header("X-Custom"));
    }

    [Fact]
    public async Task The_InspectResponse_hook_sees_every_incoming_message()
    {
        var seen = new List<HttpStatusCode>();
        using var harness = CloudflareHarness.Create(options =>
            options.InspectResponse = message => seen.Add(message.StatusCode));
        harness.RespondWithResult("[]");

        await harness.Client.Zones.ListAsync();

        Assert.Equal(new[] { HttpStatusCode.OK }, seen);
    }
}

public class CloudflareTransportEnvelopeTests
{
    [Fact]
    public async Task The_envelope_is_parsed_into_result_errors_messages_and_result_info()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("""
        {
          "success": true,
          "errors": [],
          "messages": ["All good"],
          "result": [ { "id": "z-1", "name": "a.test" }, { "id": "z-2", "name": "b.test" } ],
          "result_info": { "page": 1, "per_page": 2, "total_pages": 3, "count": 2, "total_count": 6 }
        }
        """, headers: new Dictionary<string, string> { ["CF-RAY"] = "8f1c2a3b4d5e6f70-LHR" });

        var response = await harness.Transport.SendAsync<List<Zone>?>(
            CloudflareRequest.Get("zones"), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(2, response.Result!.Count);
        Assert.Equal("a.test", response.Result[0].Name);
        Assert.Equal("All good", Assert.Single(response.Messages));
        Assert.Empty(response.Errors);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("8f1c2a3b4d5e6f70-LHR", response.RayId);
        Assert.NotNull(response.RawBody);
        Assert.Contains("\"total_count\": 6", response.RawBody, StringComparison.Ordinal);

        Assert.NotNull(response.ResultInfo);
        Assert.Equal(1, response.ResultInfo!.Page);
        Assert.Equal(3, response.ResultInfo.TotalPages);
        Assert.Equal(6, response.ResultInfo.TotalCount);

        Assert.Equal("https://api.cloudflare.com/client/v4/zones", response.RequestUri!.OriginalString);
        Assert.True(response.ResponseHeaders.ContainsKey("cf-ray"));
    }

    [Fact]
    public async Task Messages_arriving_as_objects_are_flattened_to_strings()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("""
        {
          "success": true,
          "errors": [],
          "messages": [ { "code": 10000, "message": "D1 changed" }, "plain one", 42, true ],
          "result": null
        }
        """);

        var response = await harness.Transport.SendAsync<JsonElement>(CloudflareRequest.Get("x"), CancellationToken.None);

        Assert.Equal(new[] { "D1 changed", "plain one", "42", "true" }, response.Messages);
    }

    [Fact]
    public async Task A_success_false_envelope_on_http_200_still_throws()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("""
        {
          "success": false,
          "errors": [ { "code": 1049, "message": "Email is invalid" } ],
          "messages": [],
          "result": null
        }
        """);

        var exception = await Assert.ThrowsAsync<CloudflareApiException>(
            () => harness.Transport.SendAsync<Zone?>(CloudflareRequest.Get("user"), CancellationToken.None));

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.Equal(1049, exception.ErrorCode);
        Assert.True(exception.HasErrorCode(1049));
        Assert.False(exception.HasErrorCode(1000));
        Assert.False(exception.IsNotFound);
        Assert.False(exception.IsRateLimited);
        Assert.Contains("Email is invalid", exception.Message, StringComparison.Ordinal);
        Assert.Contains("\"success\": false", exception.ResponseBody!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Structured_errors_are_surfaced_on_the_exception()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("""
        {
          "success": false,
          "errors": [
            { "code": 1061, "message": "DNS record already exists", "documentation_url": "https://developers.cloudflare.com/dns", "source": { "pointer": "content" } }
          ],
          "messages": [ "hint" ],
          "result": null
        }
        """, HttpStatusCode.Conflict, new Dictionary<string, string> { ["CF-RAY"] = "ray-1" });

        var exception = await Assert.ThrowsAsync<CloudflareApiException>(
            () => harness.Transport.SendAsync<Zone?>(CloudflareRequest.Get("zones"), CancellationToken.None));

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        var error = Assert.Single(exception.Errors);
        Assert.Equal(1061, error.Code);
        Assert.Equal("DNS record already exists", error.Message);
        Assert.Equal("https://developers.cloudflare.com/dns", error.DocumentationUrl);
        Assert.Equal("content", error.Source?.Pointer);
        Assert.Equal("hint", Assert.Single(exception.Messages));
        Assert.Equal("ray-1", exception.RayId);
        Assert.Contains("[1061] DNS record already exists", exception.Message, StringComparison.Ordinal);
        Assert.Contains("hint", exception.ToString(), StringComparison.Ordinal);
        Assert.False(exception.IsTransient);
    }

    [Fact]
    public async Task An_error_without_a_structured_body_still_produces_a_useful_message()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondRaw("<html><body>502 Bad Gateway</body></html>"u8.ToArray(), "text/html", HttpStatusCode.BadGateway);

        var exception = await Assert.ThrowsAsync<CloudflareApiException>(
            () => harness.Transport.SendAsync<Zone?>(CloudflareRequest.Get("zones"), CancellationToken.None));

        Assert.True(exception.IsTransient);
        Assert.Null(exception.ErrorCode);
        Assert.Empty(exception.Errors);
        Assert.Contains("did not contain a structured error description", exception.Message, StringComparison.Ordinal);
        Assert.Contains("502 Bad Gateway", exception.ResponseBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_body_that_is_valid_json_but_not_an_envelope_is_not_misread()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondRaw("[1,2,3]"u8.ToArray(), "application/json", HttpStatusCode.BadRequest);

        var exception = await Assert.ThrowsAsync<CloudflareApiException>(
            () => harness.Transport.SendAsync<Zone?>(CloudflareRequest.Get("zones"), CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.True(exception.IsValidationFailure);
        Assert.Empty(exception.Errors);
    }

    [Fact]
    public async Task ThrowOnApiError_false_returns_the_failure_instead_of_throwing()
    {
        using var harness = CloudflareHarness.Create(options => options.ThrowOnApiError = false);
        harness.RespondWithError(HttpStatusCode.NotFound, 1001, "Zone not found");

        var response = await harness.Transport.SendAsync<Zone?>(CloudflareRequest.Get("zones/z-1"), CancellationToken.None);

        Assert.False(response.Success);
        Assert.Null(response.Result);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1001, Assert.Single(response.Errors).Code);
    }

    [Fact]
    public async Task A_tolerated_status_code_is_returned_without_throwing_even_when_strict()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.NotFound, 1001, "Zone not found");

        var response = await harness.Transport.SendAsync<Zone?>(
            CloudflareRequest.Get("zones/z-1").Tolerate(HttpStatusCode.NotFound),
            CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Result);
    }

    [Fact]
    public async Task A_tolerated_status_code_does_not_swallow_a_different_failure()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithError(HttpStatusCode.Forbidden, 9109, "Not authorized");

        await Assert.ThrowsAsync<CloudflareApiException>(() => harness.Transport.SendAsync<Zone?>(
            CloudflareRequest.Get("zones/z-1").Tolerate(HttpStatusCode.NotFound),
            CancellationToken.None));
    }

    [Fact]
    public void EnsureResult_throws_when_a_successful_call_carried_no_payload()
    {
        var response = new CloudflareResponse<Zone?>
        {
            Success = true,
            Result = null,
            RequestUri = new Uri("https://api.cloudflare.com/client/v4/zones/z-1"),
        };

        var exception = Assert.Throws<CloudflareApiException>(response.EnsureResult);

        Assert.Contains("without a result payload", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureResult_returns_the_payload_when_present()
    {
        var zone = new Zone { Name = "a.test" };
        var response = new CloudflareResponse<Zone?> { Success = true, Result = zone };

        Assert.Same(zone, response.EnsureResult());
    }

    [Fact]
    public void Select_projects_the_result_and_keeps_the_metadata()
    {
        var response = new CloudflareResponse<Zone?>
        {
            Success = true,
            Result = new Zone { Id = "z-1", Name = "a.test" },
            StatusCode = HttpStatusCode.OK,
            RayId = "ray-9",
            ResultInfo = new CloudflareResultInfo { TotalCount = 1 },
        };

        var projected = response.Select(zone => zone?.Name);

        Assert.Equal("a.test", projected.Result);
        Assert.Equal(response.StatusCode, projected.StatusCode);
        Assert.Equal("ray-9", projected.RayId);
        Assert.Equal(1, projected.ResultInfo!.TotalCount);
        Assert.Throws<ArgumentNullException>(() => response.Select<string>(null!));
    }

    [Fact]
    public async Task SendRawAsync_returns_the_bytes_and_the_content_type()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondRaw("stored value"u8.ToArray(), "text/plain", HttpStatusCode.OK, new Dictionary<string, string> { ["CF-RAY"] = "ray-raw" });

        var raw = await harness.Transport.SendRawAsync(CloudflareRequest.Get("accounts/a/storage/kv/namespaces/n/values/k"), CancellationToken.None);

        Assert.Equal("stored value", raw.Body);
        Assert.Equal("text/plain", raw.ContentType);
        Assert.True(raw.IsSuccessStatusCode);
        Assert.Equal("ray-raw", raw.RayId);
        Assert.Equal("ray-raw", raw.Header("CF-RAY"));
        Assert.Equal("ray-raw", raw.Header("cf-ray"));
        Assert.Null(raw.Header("Missing"));
    }

    [Fact]
    public async Task SendRawAsync_throws_on_failure_unless_tolerated()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondRaw(Array.Empty<byte>(), "text/plain", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<CloudflareApiException>(
            () => harness.Transport.SendRawAsync(CloudflareRequest.Get("k"), CancellationToken.None));

        using var tolerant = CloudflareHarness.Create();
        tolerant.RespondRaw(Array.Empty<byte>(), "text/plain", HttpStatusCode.NotFound);

        var raw = await tolerant.Transport.SendRawAsync(
            CloudflareRequest.Get("k").Tolerate(HttpStatusCode.NotFound),
            CancellationToken.None);

        Assert.False(raw.IsSuccessStatusCode);
        Assert.Empty(raw.Content);
        Assert.Equal(string.Empty, raw.Body);
    }

    [Fact]
    public async Task The_query_string_is_built_from_the_parameters_object()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");

        await harness.Client.Zones.ListAsync(new ZoneListParameters
        {
            Page = 2,
            PerPage = 50,
            Name = "example.com",
            Status = ZoneStatusValues.Active,
            AccountId = "acct-1",
            Order = "name",
            Direction = SortDirection.Descending,
            Match = FilterMatchMode.All,
        });

        Assert.Equal("/client/v4/zones", harness.LastRequest.Path);
        Assert.Equal(
            "page=2&per_page=50&name=example.com&status=active&account.id=acct-1&order=name&direction=desc&match=all",
            harness.LastRequest.Query.TrimStart('?'));
    }

    [Fact]
    public async Task SendAsync_rejects_a_null_request()
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => harness.Transport.SendAsync<Zone?>(null!, CancellationToken.None));
    }

    [Fact]
    public void The_options_instance_is_shared_between_transport_and_client()
    {
        using var harness = CloudflareHarness.Create();

        Assert.Same(harness.Options, harness.Transport.Options);
        Assert.Same(harness.Options, harness.Client.Options);
    }
}

public class CloudflareTransportRetryTests
{
    private static HttpResponseMessage Status(HttpStatusCode code, string body = "{\"success\":true,\"result\":[]}", IDictionary<string, string>? headers = null)
    {
        var message = new HttpResponseMessage(code)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };

        if (headers is not null)
        {
            foreach (var pair in headers)
            {
                message.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
            }
        }

        return message;
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Transient_failures_are_retried_until_they_succeed(HttpStatusCode status)
    {
        var attempts = 0;
        using var harness = CloudflareHarness.CreateWithResponder((_, index) =>
        {
            attempts++;
            return index == 0 ? Status(status) : Status(HttpStatusCode.OK);
        });

        var response = await harness.Transport.SendAsync<List<Zone>?>(CloudflareRequest.Get("zones"), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(2, attempts);
        Assert.Equal(2, harness.RequestCount);
        Assert.Single(harness.Delays);
    }

    [Fact]
    public async Task Retries_stop_after_MaxRetries_attempts_and_report_the_last_failure()
    {
        using var harness = CloudflareHarness.CreateWithResponder(
            (_, _) => Status(HttpStatusCode.ServiceUnavailable, "{\"success\":false,\"errors\":[{\"code\":1000,\"message\":\"down\"}],\"result\":null}"),
            options => options.MaxRetries = 2);

        var exception = await Assert.ThrowsAsync<CloudflareApiException>(
            () => harness.Transport.SendAsync<Zone?>(CloudflareRequest.Get("zones"), CancellationToken.None));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Equal(3, harness.RequestCount);
        Assert.Equal(2, harness.Delays.Count);
    }

    [Fact]
    public async Task MaxRetries_zero_means_a_single_attempt()
    {
        using var harness = CloudflareHarness.CreateWithResponder(
            (_, _) => Status(HttpStatusCode.InternalServerError),
            options => options.MaxRetries = 0);

        await Assert.ThrowsAsync<CloudflareApiException>(
            () => harness.Transport.SendAsync<Zone?>(CloudflareRequest.Get("zones"), CancellationToken.None));

        Assert.Equal(1, harness.RequestCount);
        Assert.Empty(harness.Delays);
    }

    [Fact]
    public async Task Back_off_doubles_and_is_capped()
    {
        using var harness = CloudflareHarness.CreateWithResponder(
            (_, index) => index < 3 ? Status(HttpStatusCode.TooManyRequests) : Status(HttpStatusCode.OK),
            options =>
            {
                options.MaxRetries = 5;
                options.RetryBaseDelay = TimeSpan.FromMilliseconds(100);
                options.MaxRetryDelay = TimeSpan.FromMilliseconds(250);
                options.UseRetryJitter = false;
            });

        await harness.Transport.SendAsync<List<Zone>?>(CloudflareRequest.Get("zones"), CancellationToken.None);

        Assert.Equal(
            new[]
            {
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(200),
                TimeSpan.FromMilliseconds(250),
            },
            harness.Delays);
    }

    [Fact]
    public async Task Jitter_only_ever_increases_the_delay()
    {
        using var harness = CloudflareHarness.CreateWithResponder(
            (_, index) => index < 4 ? Status(HttpStatusCode.TooManyRequests) : Status(HttpStatusCode.OK),
            options =>
            {
                options.MaxRetries = 5;
                options.RetryBaseDelay = TimeSpan.FromMilliseconds(100);
                options.UseRetryJitter = true;
                options.MaxRetryDelay = TimeSpan.FromSeconds(10);
            });

        await harness.Transport.SendAsync<List<Zone>?>(CloudflareRequest.Get("zones"), CancellationToken.None);

        var expectedMinimums = new[] { 100d, 200d, 400d, 800d };
        Assert.Equal(4, harness.Delays.Count);
        for (var i = 0; i < harness.Delays.Count; i++)
        {
            Assert.InRange(harness.Delays[i].TotalMilliseconds, expectedMinimums[i], expectedMinimums[i] * 1.25);
        }
    }

    [Fact]
    public async Task RetryAfter_in_seconds_wins_over_the_exponential_back_off()
    {
        using var harness = CloudflareHarness.CreateWithResponder(
            (_, index) => index == 0
                ? Status(HttpStatusCode.TooManyRequests, headers: new Dictionary<string, string> { ["Retry-After"] = "3" })
                : Status(HttpStatusCode.OK),
            options =>
            {
                options.MaxRetries = 1;
                options.RetryBaseDelay = TimeSpan.FromMilliseconds(10);
                options.MaxRetryDelay = TimeSpan.FromSeconds(30);
            });

        await harness.Transport.SendAsync<List<Zone>?>(CloudflareRequest.Get("zones"), CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(3), Assert.Single(harness.Delays));
    }

    [Fact]
    public async Task RetryAfter_is_clamped_to_MaxRetryDelay()
    {
        using var harness = CloudflareHarness.CreateWithResponder(
            (_, index) => index == 0
                ? Status(HttpStatusCode.TooManyRequests, headers: new Dictionary<string, string> { ["Retry-After"] = "600" })
                : Status(HttpStatusCode.OK),
            options =>
            {
                options.MaxRetries = 1;
                options.MaxRetryDelay = TimeSpan.FromSeconds(5);
            });

        await harness.Transport.SendAsync<List<Zone>?>(CloudflareRequest.Get("zones"), CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(5), Assert.Single(harness.Delays));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task Permanent_failures_are_not_retried(HttpStatusCode status)
    {
        using var harness = CloudflareHarness.CreateWithResponder((_, _) => Status(status));

        await Assert.ThrowsAsync<CloudflareApiException>(
            () => harness.Transport.SendAsync<Zone?>(CloudflareRequest.Get("zones"), CancellationToken.None));

        Assert.Equal(1, harness.RequestCount);
        Assert.Empty(harness.Delays);
    }

    [Fact]
    public async Task A_transport_level_failure_is_retried_and_then_wrapped()
    {
        using var harness = CloudflareHarness.CreateWithResponder(
            (_, _) => throw new HttpRequestException("connection reset"),
            options => options.MaxRetries = 2);

        var exception = await Assert.ThrowsAsync<CloudflareTransportException>(
            () => harness.Transport.SendAsync<Zone?>(CloudflareRequest.Get("zones"), CancellationToken.None));

        Assert.Equal(3, harness.RequestCount);
        Assert.Equal(3, exception.Attempts);
        Assert.False(exception.IsTimeout);
        Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Contains("zones", exception.RequestUri!.ToString(), StringComparison.Ordinal);
        Assert.Contains("3 attempt(s)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_succeeding_attempt_after_a_transport_failure_is_used()
    {
        using var harness = CloudflareHarness.CreateWithResponder((_, index) =>
        {
            if (index == 0)
            {
                throw new HttpRequestException("temporary");
            }

            return Status(HttpStatusCode.OK);
        });

        var response = await harness.Transport.SendAsync<List<Zone>?>(CloudflareRequest.Get("zones"), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(2, harness.RequestCount);
    }

    [Fact]
    public async Task A_client_timeout_is_reported_as_a_timeout_not_as_a_generic_failure()
    {
        using var harness = CloudflareHarness.CreateWithResponder(
            (_, _) => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"),
            options => options.MaxRetries = 1);

        var exception = await Assert.ThrowsAsync<CloudflareTransportException>(
            () => harness.Transport.SendAsync<Zone?>(CloudflareRequest.Get("zones"), CancellationToken.None));

        Assert.True(exception.IsTimeout);
        Assert.Equal(2, exception.Attempts);
        Assert.IsType<TaskCanceledException>(exception.InnerException);
    }

    [Fact]
    public async Task Cancelling_the_token_stops_immediately_without_retrying()
    {
        using var harness = CloudflareHarness.CreateWithResponder((_, _) => Status(HttpStatusCode.OK));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.Transport.SendAsync<Zone?>(CloudflareRequest.Get("zones"), cancellation.Token));

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task Cancelling_during_a_retry_wait_surfaces_as_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        using var harness = CloudflareHarness.CreateWithResponder(
            (_, index) => index == 0 ? Status(HttpStatusCode.ServiceUnavailable) : Status(HttpStatusCode.OK),
            options =>
            {
                options.MaxRetries = 3;
                options.RetryBaseDelay = TimeSpan.FromSeconds(1);
            });

        // The delay seam is where the cancellation has to be observed.
        harness.Transport.DelayOverride = (_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.Transport.SendAsync<Zone?>(CloudflareRequest.Get("zones"), cancellation.Token));

        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task The_request_body_is_re_materialised_for_every_attempt()
    {
        using var harness = CloudflareHarness.CreateWithResponder(
            (_, index) => index == 0 ? Status(HttpStatusCode.ServiceUnavailable) : Status(HttpStatusCode.OK),
            options => options.MaxRetries = 1);

        await harness.Transport.SendAsync<Zone?>(
            CloudflareRequest.Post("zones", new CreateZoneRequest("example.com", "acct-1")),
            CancellationToken.None);

        Assert.Equal(2, harness.RequestCount);
        Assert.Equal(harness.Requests[0].Body, harness.Requests[1].Body);
        Assert.Contains("\"name\":\"example.com\"", harness.Requests[1].Body!, StringComparison.Ordinal);
        Assert.Contains("\"account\":{\"id\":\"acct-1\"}", harness.Requests[1].Body!, StringComparison.Ordinal);
    }
}
