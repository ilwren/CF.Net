using CFNet.Exceptions;
using CFNet.Models.Ssl;
using CFNet.UnitTests.Testing;
using Xunit;

namespace CFNet.UnitTests.Services;

/// <summary>
/// Origin post-quantum encryption: a zone-level setting that Cloudflare publishes under the zone's
/// <c>cache</c> segment even though it is about the TLS handshake with the origin, and that Cloudflare documents
/// as a no-op since automatic key exchange replaced the control it used to hold. The facts here pin the path,
/// the one-field body, and the rule that a successful write is reported as what came back rather than as what
/// the zone now does.
/// </summary>
public class OriginPostQuantumEncryptionServiceTests
{
    /// <summary>The path every fact in this file expects - note the <c>cache</c> segment.</summary>
    private const string SettingsPath = "/client/v4/zones/zone-5678/cache/origin_post_quantum_encryption";

    [Fact]
    public async Task GetAsync_reads_the_value_a_zone_holds_and_whether_it_may_change_it()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """{"id":"origin_pqe","editable":true,"value":"preferred","modified_on":"2025-09-30T12:34:56Z"}""");

        var setting = await harness.Client.OriginPostQuantumEncryption.GetAsync();

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Equal(SettingsPath, harness.LastRequest.Path);
        Assert.Equal(string.Empty, harness.LastRequest.Query.TrimStart('?'));

        Assert.Equal("origin_pqe", setting.Id);
        Assert.True(setting.Editable);
        Assert.Equal(OriginPostQuantumEncryptionMode.Preferred, setting.Value);
        Assert.Equal(new DateTimeOffset(2025, 9, 30, 12, 34, 56, TimeSpan.Zero), setting.ModifiedOn);
        Assert.Equal(
            "origin post-quantum encryption (origin_pqe): preferred, editable",
            setting.ToString());
        Assert.True(setting.AdditionalData is not { Count: > 0 });
    }

    [Fact]
    public async Task GetAsync_tolerates_a_setting_that_was_never_changed()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"id":"origin_pqe","editable":false,"value":"off","modified_on":null}""");

        var setting = await harness.Client.OriginPostQuantumEncryption.GetAsync();

        Assert.False(setting.Editable);
        Assert.Equal(OriginPostQuantumEncryptionMode.Off, setting.Value);
        Assert.Null(setting.ModifiedOn);
        Assert.Equal(
            "origin post-quantum encryption (origin_pqe): off, not editable",
            setting.ToString());
    }

    [Fact]
    public async Task GetAsync_reads_a_value_it_has_not_met_as_unknown_instead_of_failing()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"id":"origin_pqe","editable":true,"value":"quantum_only"}""");

        var setting = await harness.Client.OriginPostQuantumEncryption.GetAsync();

        Assert.Equal(OriginPostQuantumEncryptionMode.Unknown, setting.Value);
        Assert.Null(setting.ModifiedOn);
        Assert.Contains("an unpublished value", setting.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAsync_reports_a_missing_result_as_an_api_exception_naming_the_call()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("""{"success":true,"errors":[],"messages":[],"result":null}""");

        var error = await Assert.ThrowsAsync<CloudflareApiException>(
            () => harness.Client.OriginPostQuantumEncryption.GetAsync());

        // Unlike URL normalization, every field of this answer is required, so a null result is a broken answer.
        Assert.Contains("origin_post_quantum_encryption", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAsync_uses_the_zone_it_is_given_instead_of_the_configured_one()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"id":"origin_pqe","editable":true,"value":"supported"}""");

        await harness.Client.OriginPostQuantumEncryption.GetAsync("zone-other");

        Assert.Equal(
            "/client/v4/zones/zone-other/cache/origin_post_quantum_encryption",
            harness.LastRequest.Path);
    }

    [Fact]
    public async Task UpdateAsync_sends_the_one_field_the_endpoint_takes()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"id":"origin_pqe","editable":true,"value":"supported"}""");

        var request = new OriginPostQuantumEncryptionRequest { Value = OriginPostQuantumEncryptionMode.Supported };
        Assert.Equal("set origin post-quantum encryption to supported", request.ToString());

        var answered = await harness.Client.OriginPostQuantumEncryption.UpdateAsync(request);

        Assert.Equal(HttpMethod.Put, harness.LastRequest.Method);
        Assert.Equal(SettingsPath, harness.LastRequest.Path);
        Assert.Equal("application/json", harness.LastRequest.ContentType);
        Assert.Equal("""{"value":"supported"}""", harness.LastRequest.Body);
        Assert.Equal(OriginPostQuantumEncryptionMode.Supported, answered.Value);
    }

    [Fact]
    public async Task UpdateAsync_reports_what_came_back_even_when_it_disagrees_with_what_was_asked_for()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"id":"origin_pqe","editable":false,"value":"off"}""");

        var answered = await harness.Client.OriginPostQuantumEncryption.UpdateAsync(
            new OriginPostQuantumEncryptionRequest { Value = OriginPostQuantumEncryptionMode.Preferred });

        // Cloudflare documents this endpoint as a no-op, so the answer is the only evidence a caller has - and
        // here it says the zone is still off, and that this zone cannot change it.
        Assert.Equal(OriginPostQuantumEncryptionMode.Off, answered.Value);
        Assert.False(answered.Editable);
        Assert.Equal(
            "origin post-quantum encryption (origin_pqe): off, not editable",
            answered.ToString());
    }

    [Fact]
    public async Task UpdateAsync_refuses_a_request_that_carries_no_value()
    {
        using var harness = CloudflareHarness.Create();

        var error = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.OriginPostQuantumEncryption.UpdateAsync(new OriginPostQuantumEncryptionRequest()));

        Assert.Single(error.Problems);
        Assert.Contains("value", error.Problems[0], StringComparison.Ordinal);
        Assert.Equal("set origin post-quantum encryption to no value", new OriginPostQuantumEncryptionRequest().ToString());
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task UpdateAsync_refuses_a_value_it_cannot_turn_back_into_a_string()
    {
        using var harness = CloudflareHarness.Create();

        var error = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.OriginPostQuantumEncryption.UpdateAsync(
                new OriginPostQuantumEncryptionRequest { Value = OriginPostQuantumEncryptionMode.Unknown }));

        Assert.Single(error.Problems);
        Assert.Contains("Set Value to off, supported or preferred.", error.Problems[0], StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }
}
