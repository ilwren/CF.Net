using CFNet.TestKit;
using Xunit;

namespace CFNet.UnitTests.Testing;

/// <summary>
/// The shape checks exist so that a copy/paste mistake says "that is not an account id" instead of
/// turning every integration test into an authentication failure. They are deliberately offline: the
/// only authority on whether a credential works is Cloudflare.
/// </summary>
public class CredentialFormatTests
{
    private const string AccountId = "023e105f4ecef8ad9ca31a8372d0c353";
    private const string ZoneId = "8d0c8239f88f98a8cb82ec7bb29b8556";
    private const string NewUserToken = "cfut_" + "AbCdEf0123456789AbCdEf0123456789AbCdEf01" + "9x";
    private const string NewAccountToken = "cfat_" + "AbCdEf0123456789AbCdEf0123456789AbCdEf01" + "9x";
    private const string NewGlobalKey = "cfk_" + "AbCdEf0123456789AbCdEf0123456789AbCdEf01" + "9x";
    private const string LegacyToken = "v1AbCdEf0123456789AbCdEf0123456789AbCdEf";
    private const string LegacyGlobalKey = "c2547eb745079dac9320b638f5e225cf483cc";

    [Theory]
    [InlineData(AccountId)]
    [InlineData(ZoneId)]
    [InlineData("023E105F4ECEF8AD9CA31A8372D0C353")]
    public void Ids_are_32_hexadecimal_characters(string id) =>
        Assert.True(CredentialFormat.IsWellFormedId(id));

    [Theory]
    [InlineData("my-account")]
    [InlineData("https://dash.cloudflare.com/023e105f4ecef8ad9ca31a8372d0c353")]
    [InlineData("023e105f4ecef8ad9ca31a8372d0c35")]
    [InlineData("023e105f4ecef8ad9ca31a8372d0c3531")]
    [InlineData("023e105f-4ecef-8ad9-ca31-a8372d0c3531")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_not_an_id(string? notAnId) =>
        Assert.False(CredentialFormat.IsWellFormedId(notAnId));

    [Fact]
    public void Normalise_removes_the_quotes_a_json_copy_brings()
    {
        Assert.Equal(AccountId, CredentialFormat.Normalise($"  \"{AccountId}\"  "));
        Assert.Equal(AccountId, CredentialFormat.Normalise($"'{AccountId}'"));
        Assert.Equal(AccountId, CredentialFormat.Normalise($" {AccountId} "));
        Assert.Null(CredentialFormat.Normalise("   "));
        Assert.Null(CredentialFormat.Normalise(null));
    }

    [Fact]
    public void A_quoted_id_is_reported_as_a_quoting_problem_not_a_shape_problem()
    {
        var problem = CredentialFormat.ExplainId($"\"{AccountId}\"", "CLOUDFLARE_ZONE_ID", "Look in the API section.");

        Assert.NotNull(problem);
        Assert.Contains("quotes", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplainId_names_the_variable_and_points_at_the_dashboard()
    {
        var problem = CredentialFormat.ExplainId("my-account", "CLOUDFLARE_ACCOUNT_ID", "Press Ctrl/Cmd+K and choose 'Copy account ID'.");

        Assert.NotNull(problem);
        Assert.Contains("CLOUDFLARE_ACCOUNT_ID", problem, StringComparison.Ordinal);
        Assert.Contains("32 hexadecimal characters", problem, StringComparison.Ordinal);
        Assert.Contains("Copy account ID", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(NewUserToken)]
    [InlineData(NewAccountToken)]
    [InlineData(LegacyToken)]
    public void Tokens_from_both_generations_are_accepted(string token) =>
        Assert.True(CredentialFormat.IsWellFormedApiToken(token));

    [Theory]
    [InlineData(NewGlobalKey)]
    [InlineData(LegacyGlobalKey)]
    [InlineData(AccountId)]
    [InlineData("https://dash.cloudflare.com/profile/api-tokens")]
    [InlineData("cfut_ with a label in front")]
    [InlineData("")]
    [InlineData(null)]
    public void Things_that_are_not_tokens_are_rejected(string? notAToken) =>
        Assert.False(CredentialFormat.IsWellFormedApiToken(notAToken));

    [Fact]
    public void A_global_key_pasted_into_the_token_variable_is_called_out_by_name()
    {
        var problem = CredentialFormat.ExplainApiToken(NewGlobalKey);

        Assert.NotNull(problem);
        Assert.Contains("CLOUDFLARE_GLOBAL_API_KEY", problem, StringComparison.Ordinal);
        Assert.Contains("CLOUDFLARE_API_EMAIL", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_quoted_token_is_reported_without_being_echoed()
    {
        var problem = CredentialFormat.ExplainApiToken($"\"{NewUserToken}\"");

        Assert.NotNull(problem);
        Assert.Contains("quotes", problem, StringComparison.Ordinal);
        Assert.DoesNotContain(NewUserToken, problem, StringComparison.Ordinal);
        Assert.DoesNotContain("cfut_", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Trailing_whitespace_from_a_copy_is_reported()
    {
        var problem = CredentialFormat.ExplainGlobalApiKey(NewGlobalKey + "  ");

        Assert.NotNull(problem);
        Assert.Contains("whitespace", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pasted_url_is_called_out()
    {
        var problem = CredentialFormat.ExplainApiToken("https://dash.cloudflare.com/profile/api-tokens");

        Assert.NotNull(problem);
        Assert.Contains("URL", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_token_pasted_into_the_global_key_variable_is_called_out()
    {
        var problem = CredentialFormat.ExplainGlobalApiKey(NewUserToken);

        Assert.NotNull(problem);
        Assert.Contains("CLOUDFLARE_API_TOKEN", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(NewGlobalKey)]
    [InlineData(LegacyGlobalKey)]
    public void Global_keys_from_both_generations_are_accepted(string key) =>
        Assert.True(CredentialFormat.IsWellFormedGlobalApiKey(key));

    [Fact]
    public void A_credential_is_never_echoed_into_a_message()
    {
        foreach (var problem in new[]
                 {
                     CredentialFormat.ExplainApiToken(NewGlobalKey),
                     CredentialFormat.ExplainApiToken("cfut_short"),
                     CredentialFormat.ExplainGlobalApiKey(NewUserToken),
                     CredentialFormat.ExplainGlobalApiKey("cfk_not-a-real-key"),
                 })
        {
            Assert.NotNull(problem);
            Assert.DoesNotContain(NewGlobalKey, problem, StringComparison.Ordinal);
            Assert.DoesNotContain(NewUserToken, problem, StringComparison.Ordinal);
            Assert.DoesNotContain("cfut_short", problem, StringComparison.Ordinal);
            Assert.DoesNotContain("cfk_not-a-real-key", problem, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Something_that_is_absent_is_not_malformed()
    {
        // "You did not set this" is a different message, and the skip reasons already say it.
        Assert.Null(CredentialFormat.ExplainId(null, "CLOUDFLARE_ZONE_ID", "hint"));
        Assert.Null(CredentialFormat.ExplainId("   ", "CLOUDFLARE_ZONE_ID", "hint"));
        Assert.Null(CredentialFormat.ExplainApiToken(null));
        Assert.Null(CredentialFormat.ExplainGlobalApiKey(null));
        Assert.Null(CredentialFormat.ExplainEmail(null));
    }

    [Fact]
    public void Something_that_looks_right_produces_no_message()
    {
        Assert.Null(CredentialFormat.ExplainId(AccountId, "CLOUDFLARE_ACCOUNT_ID", "hint"));
        Assert.Null(CredentialFormat.ExplainApiToken(NewUserToken));
        Assert.Null(CredentialFormat.ExplainGlobalApiKey(NewGlobalKey));
        Assert.Null(CredentialFormat.ExplainEmail("someone@example.com"));
    }

    [Fact]
    public void An_email_without_a_domain_is_described_but_not_quoted()
    {
        var problem = CredentialFormat.ExplainEmail("someone@localhost");

        Assert.NotNull(problem);
        Assert.Contains("X-Auth-Email", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("someone@localhost", problem, StringComparison.Ordinal);
    }
}
