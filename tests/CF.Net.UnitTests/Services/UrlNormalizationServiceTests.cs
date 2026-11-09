using System.Text.Json;
using CFNet.Exceptions;
using CFNet.Models.Zones;
using CFNet.Serialization;
using CFNet.UnitTests.Testing;
using Xunit;

namespace CFNet.UnitTests.Services;

/// <summary>
/// URL normalization: one zone-level setting, three verbs, and a published answer in which every field is
/// optional - including the result itself. The facts here pin the path, the body a write sends, and the way a
/// half-filled answer is reported rather than guessed at.
/// </summary>
public class UrlNormalizationServiceTests
{
    /// <summary>The path every fact in this file expects.</summary>
    private const string SettingsPath = "/client/v4/zones/zone-5678/url_normalization";

    [Fact]
    public async Task GetAsync_reads_the_normalizer_a_zone_runs_and_which_requests_it_applies_to()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"scope":"both","type":"cloudflare"}""");

        var settings = await harness.Client.UrlNormalization.GetAsync();

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Equal(SettingsPath, harness.LastRequest.Path);
        Assert.Equal(string.Empty, harness.LastRequest.Query.TrimStart('?'));

        Assert.NotNull(settings);
        Assert.Equal(UrlNormalizationScope.Both, settings!.Scope);
        Assert.Equal(UrlNormalizationType.Cloudflare, settings.Type);
        Assert.Equal("URL normalization: cloudflare applied to both", settings.ToString());
        Assert.True(settings.AdditionalData is not { Count: > 0 });
    }

    [Fact]
    public async Task GetAsync_reports_no_setting_when_Cloudflare_answers_with_no_result()
    {
        using var harness = CloudflareHarness.Create();
        harness.Respond("""{"success":true,"errors":[],"messages":[],"result":null}""");

        var settings = await harness.Client.UrlNormalization.GetAsync();

        // The endpoint publishes the result as optional, so a null answer is an answer, not a failure.
        Assert.Null(settings);
        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task GetAsync_reads_a_half_filled_answer_as_a_half_filled_setting()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"type":"rfc3986"}""");

        var settings = await harness.Client.UrlNormalization.GetAsync();

        Assert.NotNull(settings);
        Assert.Null(settings!.Scope);
        Assert.Equal(UrlNormalizationType.Rfc3986, settings.Type);
        Assert.Equal("URL normalization: rfc3986 applied to no scope", settings.ToString());
    }

    [Fact]
    public async Task GetAsync_keeps_a_value_it_has_not_met_as_unknown_instead_of_failing()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"scope":"everything","type":"whatwg"}""");

        var settings = await harness.Client.UrlNormalization.GetAsync();

        Assert.NotNull(settings);
        Assert.Equal(UrlNormalizationScope.Unknown, settings!.Scope);
        Assert.Equal(UrlNormalizationType.Unknown, settings.Type);
        Assert.Equal("URL normalization: no normalizer applied to no scope", settings.ToString());
    }

    [Fact]
    public async Task UpdateAsync_sends_both_fields_because_Cloudflare_requires_both()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"scope":"incoming","type":"rfc3986"}""");

        var answered = await harness.Client.UrlNormalization.UpdateAsync(
            new UrlNormalizationSettings
            {
                Scope = UrlNormalizationScope.Incoming,
                Type = UrlNormalizationType.Rfc3986,
            });

        Assert.Equal(HttpMethod.Put, harness.LastRequest.Method);
        Assert.Equal(SettingsPath, harness.LastRequest.Path);
        Assert.Equal("application/json", harness.LastRequest.ContentType);
        Assert.Equal("""{"scope":"incoming","type":"rfc3986"}""", harness.LastRequest.Body);

        Assert.NotNull(answered);
        Assert.Equal(UrlNormalizationScope.Incoming, answered!.Scope);
        Assert.Equal(UrlNormalizationType.Rfc3986, answered.Type);
    }

    [Fact]
    public async Task UpdateAsync_uses_the_zone_it_is_given_instead_of_the_configured_one()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"scope":"none","type":"cloudflare"}""");

        await harness.Client.UrlNormalization.UpdateAsync(
            new UrlNormalizationSettings
            {
                Scope = UrlNormalizationScope.None,
                Type = UrlNormalizationType.Cloudflare,
            },
            "zone-other");

        Assert.Equal("/client/v4/zones/zone-other/url_normalization", harness.LastRequest.Path);
    }

    [Fact]
    public async Task UpdateAsync_refuses_to_send_a_setting_that_is_missing_half_of_itself()
    {
        using var harness = CloudflareHarness.Create();

        var missingScope = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.UrlNormalization.UpdateAsync(
                new UrlNormalizationSettings { Type = UrlNormalizationType.Cloudflare }));

        var missingType = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.UrlNormalization.UpdateAsync(
                new UrlNormalizationSettings { Scope = UrlNormalizationScope.Both }));

        // A PUT that sent one field would be sending the other as nothing, which is not a reset.
        Assert.Single(missingScope.Problems);
        Assert.Contains("scope", missingScope.Problems[0], StringComparison.Ordinal);
        Assert.Single(missingType.Problems);
        Assert.Contains("type", missingType.Problems[0], StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task UpdateAsync_refuses_values_it_cannot_turn_back_into_strings()
    {
        using var harness = CloudflareHarness.Create();

        var error = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.UrlNormalization.UpdateAsync(
                new UrlNormalizationSettings
                {
                    Scope = UrlNormalizationScope.Unknown,
                    Type = UrlNormalizationType.Unknown,
                }));

        Assert.Equal(2, error.Problems.Count);
        Assert.Contains("Set Scope to one of incoming, both or none.", error.Problems[0], StringComparison.Ordinal);
        Assert.Contains("Set Type to cloudflare or rfc3986.", error.Problems[1], StringComparison.Ordinal);
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task ResetAsync_deletes_the_setting_and_reads_back_what_Cloudflare_answers()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"scope":"incoming","type":"cloudflare"}""");

        var after = await harness.Client.UrlNormalization.ResetAsync();

        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.Equal(SettingsPath, harness.LastRequest.Path);
        Assert.Null(harness.LastRequest.Body);

        // Cloudflare answers a reset with the default it fell back to, not with an empty result.
        Assert.NotNull(after);
        Assert.Equal(UrlNormalizationScope.Incoming, after!.Scope);
        Assert.Equal(UrlNormalizationType.Cloudflare, after.Type);
    }

    [Fact]
    public void EnsureValid_accepts_every_published_combination()
    {
        foreach (var scope in new[] { UrlNormalizationScope.Incoming, UrlNormalizationScope.Both, UrlNormalizationScope.None })
        {
            foreach (var type in new[] { UrlNormalizationType.Cloudflare, UrlNormalizationType.Rfc3986 })
            {
                var settings = new UrlNormalizationSettings { Scope = scope, Type = type };

                settings.EnsureValid();

                // What goes on the wire is the string Cloudflare publishes, and reading it back is the same value.
                var wire = JsonSerializer.Serialize(settings, CloudflareJson.Default);
                var roundTripped = JsonSerializer.Deserialize<UrlNormalizationSettings>(wire, CloudflareJson.Default);
                Assert.Equal(scope, roundTripped!.Scope);
                Assert.Equal(type, roundTripped.Type);
            }
        }
    }

    [Fact]
    public void A_setting_read_from_the_wire_can_be_sent_back_unchanged()
    {
        var settings = JsonSerializer.Deserialize<UrlNormalizationSettings>(
            """{"scope":"both","type":"rfc3986"}""",
            CloudflareJson.Default);

        settings!.EnsureValid();

        Assert.Equal("""{"scope":"both","type":"rfc3986"}""", JsonSerializer.Serialize(settings, CloudflareJson.Default));
    }
}
