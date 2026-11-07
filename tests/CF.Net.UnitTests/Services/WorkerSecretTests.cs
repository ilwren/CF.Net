using System.Text.Json;
using CFNet.Exceptions;
using CFNet.Models.Workers;
using CFNet.UnitTests.Testing;
using Xunit;

namespace CFNet.UnitTests.Services;

/// <summary>
/// A Worker's secrets: <c>…/workers/scripts/{script_name}/secrets</c>, and the bulk write beside it. Worth
/// pinning closely because three things here are easy to get wrong - no read of a secret ever answers its
/// value, the bulk path is <c>secrets-bulk</c> rather than a sub-path of <c>secrets</c>, and in a bulk write a
/// name mapped to null is a <em>deletion</em>, which this library's writer would otherwise drop on the floor.
/// </summary>
public class WorkerSecretTests
{
    /// <summary>A text secret and a key secret, exactly as a list answers them: names and kinds, no values.</summary>
    private const string ListedSecrets =
        """[{"name":"MY_SECRET","type":"secret_text"},{"name":"MY_KEY","type":"secret_key","format":"raw","usages":["encrypt","decrypt"],"algorithm":{"name":"AES-GCM","length":256}}]""";

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void The_secret_model_has_nowhere_to_put_a_value()
    {
        // Cloudflare stores secrets encrypted and answers only which ones exist and what kind each is, so the
        // model that reads them has no value property at all: a read cannot leak one, and nothing could arrive
        // in one without landing in AdditionalData, which the live run reports by name.
        Assert.DoesNotContain(
            typeof(WorkerSecret).GetProperties().Select(property => property.Name),
            name => name is "Text" or "Value" or "Secret" or "KeyBase64");

        Assert.NotNull(typeof(WorkerSecret).GetProperty("Name"));
        Assert.NotNull(typeof(WorkerSecret).GetProperty("Type"));
    }

    [Fact]
    public async Task ListSecretsAsync_reads_both_kinds_of_secret_and_neither_of_their_values()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(ListedSecrets);

        var secrets = await harness.Client.Workers.ListSecretsAsync("my-script");

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/workers/scripts/my-script/secrets",
            harness.LastRequest.Path,
            StringComparison.Ordinal);

        // No paging and no filters: this endpoint takes none.
        Assert.Equal(string.Empty, harness.LastRequest.Query.TrimStart('?'));

        Assert.Equal(2, secrets.Count);

        var text = secrets[0];
        Assert.Equal("MY_SECRET", text.Name);
        Assert.Equal(WorkerSecretTypes.SecretText, text.Type);
        Assert.False(text.IsKey);
        Assert.Null(text.Format);
        Assert.Null(text.Usages);
        Assert.Null(text.Algorithm);
        Assert.Equal("MY_SECRET (secret_text)", text.ToString());
        Assert.True(text.AdditionalData is not { Count: > 0 });

        // A key secret is the same type with the key's own fields filled, and its algorithm stays the JSON
        // Cloudflare sent: an algorithm object differs per algorithm, the way WebCrypto's own do.
        var key = secrets[1];
        Assert.Equal("MY_KEY", key.Name);
        Assert.Equal(WorkerSecretTypes.SecretKey, key.Type);
        Assert.True(key.IsKey);
        Assert.Equal(WorkerSecretKeyFormat.Raw, key.Format);
        Assert.Equal(
            new[] { WorkerSecretKeyUsage.Encrypt, WorkerSecretKeyUsage.Decrypt },
            key.Usages);
        Assert.Equal(JsonValueKind.Object, key.Algorithm!.Value.ValueKind);
        Assert.Equal("AES-GCM", key.Algorithm!.Value.GetProperty("name").GetString());
        Assert.Equal(256, key.Algorithm!.Value.GetProperty("length").GetInt32());
        Assert.True(key.AdditionalData is not { Count: > 0 });
    }

    [Fact]
    public async Task GetSecretAsync_reads_one_secret_and_answers_null_for_one_that_is_not_there()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"name":"MY_SECRET","type":"secret_text"}""");
        harness.RespondWithError(System.Net.HttpStatusCode.NotFound, 10007, "Secret not found");

        var secret = await harness.Client.Workers.GetSecretAsync("my-script", "MY_SECRET");

        Assert.EndsWith(
            "/accounts/acct-1234/workers/scripts/my-script/secrets/MY_SECRET",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Equal("MY_SECRET", secret!.Name);
        Assert.Equal(WorkerSecretTypes.SecretText, secret.Type);

        // A secret the script does not have reads as null rather than as an error, like every other single
        // read in this library.
        Assert.Null(await harness.Client.Workers.GetSecretAsync("my-script", "NOT_THERE"));
        Assert.Equal(2, harness.RequestCount);
    }

    [Fact]
    public async Task The_url_encoded_flag_is_only_sent_when_a_caller_says_the_name_is_encoded()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"name":"MY_SECRET","type":"secret_text"}""");
        harness.RespondWithResult("""{"name":"MY SECRET","type":"secret_text"}""");

        await harness.Client.Workers.GetSecretAsync("my-script", "MY_SECRET");
        Assert.Equal(string.Empty, harness.LastRequest.Query.TrimStart('?'));

        // A name whose characters the encoding changed is the case Cloudflare's flag exists for, and the
        // segment is encoded either way - the flag tells Cloudflare to decode it again.
        await harness.Client.Workers.GetSecretAsync("my-script", "MY SECRET", urlEncoded: true);
        Assert.Equal("url_encoded=true", harness.LastRequest.Query.TrimStart('?'));
        Assert.Contains("/secrets/MY%20SECRET", harness.LastRequest.Uri!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PutSecretAsync_puts_the_secret_itself_as_the_body()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"name":"MY_SECRET","type":"secret_text"}""");
        harness.RespondWithResult("""{"name":"MY_KEY","type":"secret_key","format":"raw","usages":["encrypt"]}""");

        var text = await harness.Client.Workers.PutSecretAsync("my-script", "MY_SECRET", "hunter2");

        Assert.Equal(HttpMethod.Put, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/workers/scripts/my-script/secrets",
            harness.LastRequest.Path,
            StringComparison.Ordinal);

        // No wrapper: the body is the secret, and the value travels here and nowhere else.
        Assert.Equal(
            """{"name":"MY_SECRET","type":"secret_text","text":"hunter2"}""",
            harness.LastRequest.Body);
        Assert.Equal("MY_SECRET", text.Name);

        // A key secret carries its key material once, its format, its usages in WebCrypto's camelCase words,
        // and its algorithm as the JSON a caller has.
        await harness.Client.Workers.PutSecretAsync(
            "my-script",
            WorkerSecretRequest.OfKey(
                "MY_KEY",
                "a2V5",
                WorkerSecretKeyFormat.Raw,
                Json("""{"name":"AES-GCM","length":256}"""),
                new[] { WorkerSecretKeyUsage.Encrypt }));

        Assert.Equal(
            """{"name":"MY_KEY","type":"secret_key","key_base64":"a2V5","format":"raw","usages":["encrypt"],"algorithm":{"name":"AES-GCM","length":256}}""",
            harness.LastRequest.Body);
    }

    [Fact]
    public async Task PutSecretAsync_refuses_a_secret_Cloudflare_could_not_store()
    {
        using var harness = CloudflareHarness.Create();

        var refused = new[]
        {
            new WorkerSecretRequest(),
            new WorkerSecretRequest { Name = "MY_SECRET", Type = WorkerSecretTypes.SecretText },
            new WorkerSecretRequest { Name = "MY_SECRET", Type = WorkerSecretTypes.SecretText, Text = "v", Format = WorkerSecretKeyFormat.Raw },
            new WorkerSecretRequest { Name = "MY_KEY", Type = WorkerSecretTypes.SecretKey },
            new WorkerSecretRequest { Name = "MY_KEY", Type = WorkerSecretTypes.SecretKey, KeyBase64 = "a2V5" },
            new WorkerSecretRequest { Name = "MY_KEY", Type = WorkerSecretTypes.SecretKey, KeyBase64 = "a2V5", KeyJwk = Json("{}"), Format = WorkerSecretKeyFormat.Jwk, Usages = new List<WorkerSecretKeyUsage> { WorkerSecretKeyUsage.Sign } },
            new WorkerSecretRequest { Name = "MY_KEY", Type = WorkerSecretTypes.SecretKey, KeyBase64 = "a2V5", Format = WorkerSecretKeyFormat.Raw, Text = "v" },
            new WorkerSecretRequest { Name = "MY_KEY", Type = "secret_something", Text = "v" },
        };

        foreach (var secret in refused)
        {
            var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(
                () => harness.Client.Workers.PutSecretAsync("my-script", secret));
            Assert.NotEmpty(exception.Problems);
        }

        // Every refusal above was local, so nothing has been sent yet - and an empty string is a value rather
        // than a missing one, so the one call that does go out carries it.
        harness.RespondWithResult("""{"name":"MY_SECRET","type":"secret_text"}""");
        await harness.Client.Workers.PutSecretAsync("my-script", "MY_SECRET", string.Empty);
        Assert.Equal(1, harness.RequestCount);
        Assert.Equal(
            """{"name":"MY_SECRET","type":"secret_text","text":""}""",
            harness.LastRequest.Body);
    }

    [Fact]
    public async Task DeleteSecretAsync_deletes_by_name_and_sends_no_body()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("null");

        await harness.Client.Workers.DeleteSecretAsync("my-script", "MY_SECRET");

        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/workers/scripts/my-script/secrets/MY_SECRET",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Null(harness.LastRequest.Body);
        Assert.Equal(string.Empty, harness.LastRequest.Query.TrimStart('?'));
        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task UpdateSecretsBulkAsync_patches_the_map_and_writes_a_removal_as_a_null()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """{"MY_SECRET":{"name":"MY_SECRET","type":"secret_text"},"OTHER":{"name":"OTHER","type":"secret_text"}}""");

        var written = await harness.Client.Workers.UpdateSecretsBulkAsync(
            "my-script",
            new WorkerSecretBulkRequest()
                .WithText("MY_SECRET", "hunter2")
                .WithRemoved("OLD_SECRET")
                .WithVersionTag("deploy", Json("\"abc123\"")));

        Assert.Equal(HttpMethod.Patch, harness.LastRequest.Method);

        // secrets-bulk, not a sub-path of secrets.
        Assert.EndsWith(
            "/accounts/acct-1234/workers/scripts/my-script/secrets-bulk",
            harness.LastRequest.Path,
            StringComparison.Ordinal);

        // The null is the deletion, and it is written on purpose: this library's serializer omits nulls
        // everywhere else, which here would turn a removal into nothing at all.
        Assert.Equal(
            """{"secrets":{"MY_SECRET":{"name":"MY_SECRET","type":"secret_text","text":"hunter2"},"OLD_SECRET":null},"version_tags":{"deploy":"abc123"}}""",
            harness.LastRequest.Body);

        Assert.Equal(2, written.Count);
        Assert.Equal("MY_SECRET", written["MY_SECRET"].Name);
        Assert.Equal(WorkerSecretTypes.SecretText, written["OTHER"].Type);
    }

    [Fact]
    public async Task UpdateSecretsBulkAsync_refuses_a_write_that_would_change_nothing_or_lose_a_secret()
    {
        using var harness = CloudflareHarness.Create();

        var refused = new Func<Task>[]
        {
            () => harness.Client.Workers.UpdateSecretsBulkAsync("my-script", new WorkerSecretBulkRequest()),
            () => harness.Client.Workers.UpdateSecretsBulkAsync(
                "my-script",
                new WorkerSecretBulkRequest { Secrets = new Dictionary<string, WorkerSecretRequest?>() }),
            // A blank key stores a secret nothing can reach.
            () => harness.Client.Workers.UpdateSecretsBulkAsync(
                "my-script",
                new WorkerSecretBulkRequest().WithText("  ", "v")),
            // Cloudflare stores a secret under its key, so a secret whose own name disagrees is a mistake
            // rather than a rename.
            () => harness.Client.Workers.UpdateSecretsBulkAsync(
                "my-script",
                new WorkerSecretBulkRequest
                {
                    Secrets = new Dictionary<string, WorkerSecretRequest?>
                    {
                        ["ONE"] = WorkerSecretRequest.OfText("TWO", "v"),
                    },
                }),
            // And a secret that is not one Cloudflare can store is refused here, with its key named.
            () => harness.Client.Workers.UpdateSecretsBulkAsync(
                "my-script",
                new WorkerSecretBulkRequest
                {
                    Secrets = new Dictionary<string, WorkerSecretRequest?>
                    {
                        ["MY_KEY"] = new WorkerSecretRequest { Name = "MY_KEY", Type = WorkerSecretTypes.SecretKey },
                    },
                }),
        };

        foreach (var call in refused)
        {
            var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(call);
            Assert.NotEmpty(exception.Problems);
        }

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task Every_secret_method_reads_the_account_it_was_given_rather_than_the_configured_one()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");
        harness.RespondWithResult("""{"name":"MY_SECRET","type":"secret_text"}""");
        harness.RespondWithResult("null");

        await harness.Client.Workers.ListSecretsAsync("my-script", "acct-other");
        Assert.Contains(
            "/accounts/acct-other/workers/scripts/my-script/secrets",
            harness.LastRequest.Path,
            StringComparison.Ordinal);

        await harness.Client.Workers.PutSecretAsync(
            "my-script",
            WorkerSecretRequest.OfText("MY_SECRET", "v"),
            "acct-other");
        Assert.Contains("/accounts/acct-other/", harness.LastRequest.Path, StringComparison.Ordinal);

        await harness.Client.Workers.DeleteSecretAsync("my-script", "MY_SECRET", accountId: "acct-other");
        Assert.Contains(
            "/accounts/acct-other/workers/scripts/my-script/secrets/MY_SECRET",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
    }
}
