using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Serialization;

namespace CFNet.Models.Workers;

/// <summary>
/// One of a Worker's secrets, as Cloudflare describes it back: a name and a kind, and <b>never a value</b>.
/// </summary>
/// <remarks>
/// <para>
/// A secret is what a <c>secret_text</c> binding reaches: the binding in a script's metadata carries the name,
/// and the value behind that name is written through <c>PUT …/workers/scripts/{script_name}/secrets</c> and
/// cannot be read back by anything - not this endpoint, not the dashboard. What a read answers is which
/// secrets exist and what kind each one is, which is enough to reconcile a deployment and not enough to leak
/// one.
/// </para>
/// <para>
/// Cloudflare models the answer as a union of two shapes, discriminated by <c>type</c>: <c>secret_text</c> is
/// just a name, and <c>secret_key</c> - a WebCrypto key rather than a string - adds <see cref="Format"/>,
/// <see cref="Usages"/> and <see cref="Algorithm"/>. One type carries both, with the key's fields null for a
/// text secret, which is how <see cref="WorkerBinding"/> covers every binding kind too.
/// </para>
/// </remarks>
public sealed class WorkerSecret
{
    /// <summary>The name the script uses to reach the secret (<c>env.MY_SECRET</c>).</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Secret kind literal. See <see cref="WorkerSecretTypes"/>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// The key's format, for a <c>secret_key</c>: one of the four WebCrypto import formats. Null for a text
    /// secret.
    /// </summary>
    [JsonPropertyName("format")]
    public WorkerSecretKeyFormat? Format { get; set; }

    /// <summary>
    /// What the key may be used for, for a <c>secret_key</c>. Null for a text secret, and empty is not the
    /// same as null: a key with no usages cannot do anything.
    /// </summary>
    [JsonPropertyName("usages")]
    public List<WorkerSecretKeyUsage>? Usages { get; set; }

    /// <summary>
    /// The key's algorithm, for a <c>secret_key</c>, as the JSON Cloudflare sent. Its shape is published as
    /// <c>unknown</c> - an algorithm object differs per algorithm, the way WebCrypto's own do - so this keeps
    /// what arrived rather than guessing at fields.
    /// </summary>
    [JsonPropertyName("algorithm")]
    public JsonElement? Algorithm { get; set; }

    /// <summary>Whether this is a WebCrypto key rather than a string.</summary>
    [JsonIgnore]
    public bool IsKey => string.Equals(Type, WorkerSecretTypes.SecretKey, StringComparison.Ordinal);

    /// <summary>Captures any field Cloudflare adds.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Name and kind, on one line. There is no value to leave out, and no value ever arrives.</summary>
    /// <returns>The secret described in one line.</returns>
    public override string ToString() => $"{Name} ({Type})";
}

/// <summary>The kinds of secret the secrets endpoint publishes.</summary>
/// <remarks>
/// Literals rather than an enum, for the reason <see cref="WorkerBindingTypes"/> gives: Cloudflare adds these
/// kinds regularly, and a value this library has not heard of should read as itself rather than as a failure.
/// </remarks>
public static class WorkerSecretTypes
{
    /// <summary>A string secret, reached in a Worker as <c>env.NAME</c>.</summary>
    public const string SecretText = "secret_text";

    /// <summary>A WebCrypto key, reached in a Worker as a <c>CryptoKey</c>.</summary>
    public const string SecretKey = "secret_key";
}

/// <summary>One of the WebCrypto import formats a <c>secret_key</c> can be sent in.</summary>
[JsonConverter(typeof(CloudflareEnumConverter<WorkerSecretKeyFormat>))]
public enum WorkerSecretKeyFormat
{
    /// <summary>Value Cloudflare returned that this version of the library does not know.</summary>
    Unknown = 0,

    /// <summary>A raw, algorithm-specific byte string.</summary>
    [CloudflareValue("raw")]
    Raw = 1,

    /// <summary>A PKCS#8 private key.</summary>
    [CloudflareValue("pkcs8")]
    Pkcs8 = 2,

    /// <summary>An SPKI public key.</summary>
    [CloudflareValue("spki")]
    Spki = 3,

    /// <summary>A JSON Web Key.</summary>
    [CloudflareValue("jwk")]
    Jwk = 4,
}

/// <summary>One of the usages a <c>secret_key</c> can be granted, in WebCrypto's own camelCase words.</summary>
[JsonConverter(typeof(CloudflareEnumConverter<WorkerSecretKeyUsage>))]
public enum WorkerSecretKeyUsage
{
    /// <summary>Value Cloudflare returned that this version of the library does not know.</summary>
    Unknown = 0,

    /// <summary>The key may encrypt.</summary>
    [CloudflareValue("encrypt")]
    Encrypt = 1,

    /// <summary>The key may decrypt.</summary>
    [CloudflareValue("decrypt")]
    Decrypt = 2,

    /// <summary>The key may sign.</summary>
    [CloudflareValue("sign")]
    Sign = 3,

    /// <summary>The key may verify a signature.</summary>
    [CloudflareValue("verify")]
    Verify = 4,

    /// <summary>The key may derive another key.</summary>
    [CloudflareValue("deriveKey")]
    DeriveKey = 5,

    /// <summary>The key may derive bits.</summary>
    [CloudflareValue("deriveBits")]
    DeriveBits = 6,

    /// <summary>The key may wrap another key.</summary>
    [CloudflareValue("wrapKey")]
    WrapKey = 7,

    /// <summary>The key may unwrap another key.</summary>
    [CloudflareValue("unwrapKey")]
    UnwrapKey = 8,
}

/// <summary>
/// A secret to write onto a Worker: a name, a kind, and the value or key material behind it.
/// </summary>
/// <remarks>
/// <para>
/// Writing a secret is the only way its value reaches Cloudflare, and reading one back is impossible, so a
/// value here is never an echo of something already stored - putting a secret with a name that exists
/// replaces it. Use <see cref="OfText"/> for the ordinary case and <see cref="OfKey"/> for a WebCrypto key.
/// </para>
/// <para>
/// A name has to be one a Worker can reach as <c>env.NAME</c>, which is what makes it an identifier; nothing
/// here checks that, because Cloudflare refuses a name it cannot bind and says why.
/// </para>
/// </remarks>
public sealed class WorkerSecretRequest
{
    /// <summary>The name the script will use to reach the secret.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Secret kind literal; see <see cref="WorkerSecretTypes"/>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>The secret's value, for a <c>secret_text</c>. Never answered back by any read.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>The key's bytes, base64'd, for a <c>secret_key</c> sent in anything but <c>jwk</c> format.</summary>
    [JsonPropertyName("key_base64")]
    public string? KeyBase64 { get; set; }

    /// <summary>
    /// The key as a JSON Web Key, for a <c>secret_key</c> sent in <c>jwk</c> format. Its shape is WebCrypto's
    /// rather than Cloudflare's, so it is the JSON a caller has.
    /// </summary>
    [JsonPropertyName("key_jwk")]
    public JsonElement? KeyJwk { get; set; }

    /// <summary>The key's format, for a <c>secret_key</c>.</summary>
    [JsonPropertyName("format")]
    public WorkerSecretKeyFormat? Format { get; set; }

    /// <summary>What the key may be used for, for a <c>secret_key</c>.</summary>
    [JsonPropertyName("usages")]
    public List<WorkerSecretKeyUsage>? Usages { get; set; }

    /// <summary>
    /// The key's algorithm, for a <c>secret_key</c>, as the JSON WebCrypto wants it - published as
    /// <c>unknown</c>, so it is a caller's own object rather than fields this library invented.
    /// </summary>
    [JsonPropertyName("algorithm")]
    public JsonElement? Algorithm { get; set; }

    /// <summary>A string secret, which is the ordinary case.</summary>
    /// <param name="name">The name the script reaches it by.</param>
    /// <param name="text">The value, which no read ever answers back.</param>
    /// <returns>The request to put.</returns>
    public static WorkerSecretRequest OfText(string name, string text) =>
        new() { Name = name, Type = WorkerSecretTypes.SecretText, Text = text };

    /// <summary>A WebCrypto key secret, sent as base64'd bytes.</summary>
    /// <param name="name">The name the script reaches it by.</param>
    /// <param name="keyBase64">The key's bytes, base64'd.</param>
    /// <param name="format">Which WebCrypto import format those bytes are.</param>
    /// <param name="algorithm">The algorithm object WebCrypto wants, as JSON.</param>
    /// <param name="usages">What the key may be used for.</param>
    /// <returns>The request to put.</returns>
    public static WorkerSecretRequest OfKey(
        string name,
        string keyBase64,
        WorkerSecretKeyFormat format,
        JsonElement? algorithm,
        IEnumerable<WorkerSecretKeyUsage>? usages) =>
        new()
        {
            Name = name,
            Type = WorkerSecretTypes.SecretKey,
            KeyBase64 = keyBase64,
            Format = format,
            Algorithm = algorithm,
            Usages = usages?.ToList(),
        };

    /// <summary>Refuses a request Cloudflare would refuse, naming everything wrong with it at once.</summary>
    /// <exception cref="CFNet.Exceptions.CloudflareConfigurationException">
    /// Thrown when a field the kind needs is missing, or a field another kind owns is set; its
    /// <c>Problems</c> lists every reason.
    /// </exception>
    public void EnsureValid()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
        {
            problems.Add("name is required: a secret is reached by the name it is stored under.");
        }

        if (string.Equals(Type, WorkerSecretTypes.SecretText, StringComparison.Ordinal))
        {
            if (Text is null)
            {
                problems.Add("text is required for a secret_text, and an empty string is a value: it is the secret.");
            }

            if (KeyBase64 is not null || KeyJwk is not null || Format is not null || Usages is not null || Algorithm is not null)
            {
                problems.Add(
                    "key_base64, key_jwk, format, usages and algorithm belong to a secret_key, and this is a " +
                    "secret_text: Cloudflare would store a string and ignore the rest.");
            }
        }
        else if (string.Equals(Type, WorkerSecretTypes.SecretKey, StringComparison.Ordinal))
        {
            if (KeyBase64 is null && KeyJwk is null)
            {
                problems.Add("a secret_key needs its key material: key_base64, or key_jwk for a jwk format.");
            }

            if (KeyBase64 is not null && KeyJwk is not null)
            {
                problems.Add("a secret_key carries its key material once: key_base64 and key_jwk are two ways of sending one key.");
            }

            if (Format is null or WorkerSecretKeyFormat.Unknown)
            {
                problems.Add("format is required for a secret_key, and has to be one of raw, pkcs8, spki or jwk.");
            }

            if (Usages is not { Count: > 0 })
            {
                problems.Add("usages is required for a secret_key and cannot be empty: a key with no usages cannot do anything.");
            }

            if (Text is not null)
            {
                problems.Add("text belongs to a secret_text, and this is a secret_key.");
            }
        }
        else
        {
            problems.Add(
                $"type has to be one Cloudflare publishes for a secret - '{WorkerSecretTypes.SecretText}' or " +
                $"'{WorkerSecretTypes.SecretKey}' - and this is '{Type}'.");
        }

        if (problems.Count > 0)
        {
            throw new CFNet.Exceptions.CloudflareConfigurationException(problems);
        }
    }
}

/// <summary>
/// Several secrets written in one call: <c>PATCH …/workers/scripts/{script_name}/secrets-bulk</c>.
/// </summary>
/// <remarks>
/// <para>
/// The body is a map of secret name to secret, and <b>a name mapped to null removes that secret</b> - which is
/// why this type has its own converter. This library's writer omits nulls, so a null in an ordinary dictionary
/// would be dropped and a deletion would silently become nothing at all;
/// <see cref="WorkerSecretBulkRequestConverter"/> writes each null on purpose.
/// </para>
/// <para>
/// Use <see cref="WithText"/>, <see cref="WithKey"/> and <see cref="WithRemoved"/> rather than filling the map
/// by hand: they know which is which, and <see cref="WithRemoved"/> is the only way to express a deletion.
/// </para>
/// </remarks>
[JsonConverter(typeof(WorkerSecretBulkRequestConverter))]
public sealed class WorkerSecretBulkRequest
{
    /// <summary>The secrets to write, keyed by name; a null value removes that secret.</summary>
    public IDictionary<string, WorkerSecretRequest?>? Secrets { get; set; }

    /// <summary>
    /// Version tags to set on the version this write creates, as Cloudflare's own JSON. Their values are
    /// published as <c>unknown</c>, so they are kept as they are given.
    /// </summary>
    public IDictionary<string, JsonElement>? VersionTags { get; set; }

    /// <summary>Adds a string secret to the write.</summary>
    /// <param name="name">The name the script reaches it by.</param>
    /// <param name="text">The value.</param>
    /// <returns>This request, for chaining.</returns>
    public WorkerSecretBulkRequest WithText(string name, string text) =>
        With(WorkerSecretRequest.OfText(name, text));

    /// <summary>Adds a WebCrypto key secret to the write.</summary>
    /// <param name="name">The name the script reaches it by.</param>
    /// <param name="keyBase64">The key's bytes, base64'd.</param>
    /// <param name="format">Which WebCrypto import format those bytes are.</param>
    /// <param name="algorithm">The algorithm object WebCrypto wants, as JSON.</param>
    /// <param name="usages">What the key may be used for.</param>
    /// <returns>This request, for chaining.</returns>
    public WorkerSecretBulkRequest WithKey(
        string name,
        string keyBase64,
        WorkerSecretKeyFormat format,
        JsonElement? algorithm,
        IEnumerable<WorkerSecretKeyUsage>? usages) =>
        With(WorkerSecretRequest.OfKey(name, keyBase64, format, algorithm, usages));

    /// <summary>Removes a secret, which is a name mapped to null in the body.</summary>
    /// <param name="name">The name to remove.</param>
    /// <returns>This request, for chaining.</returns>
    public WorkerSecretBulkRequest WithRemoved(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Secrets ??= new Dictionary<string, WorkerSecretRequest?>();
        Secrets[name] = null;
        return this;
    }

    /// <summary>Sets a version tag on the version this write creates.</summary>
    /// <param name="key">The tag's name.</param>
    /// <param name="value">The tag's value, as JSON.</param>
    /// <returns>This request, for chaining.</returns>
    public WorkerSecretBulkRequest WithVersionTag(string key, JsonElement value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        VersionTags ??= new Dictionary<string, JsonElement>();
        VersionTags[key] = value;
        return this;
    }

    /// <summary>Adds one secret to the write.</summary>
    /// <param name="secret">The secret, whose name is the key it is stored under.</param>
    /// <returns>This request, for chaining.</returns>
    public WorkerSecretBulkRequest With(WorkerSecretRequest secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        Secrets ??= new Dictionary<string, WorkerSecretRequest?>();
        Secrets[secret.Name ?? string.Empty] = secret;
        return this;
    }

    /// <summary>Refuses a write Cloudflare would refuse, naming everything wrong with it at once.</summary>
    /// <exception cref="CFNet.Exceptions.CloudflareConfigurationException">
    /// Thrown when the write is empty or one of its secrets is not something Cloudflare can store; its
    /// <c>Problems</c> lists every reason.
    /// </exception>
    public void EnsureValid()
    {
        var problems = new List<string>();

        if (Secrets is not { Count: > 0 })
        {
            problems.Add(
                "a bulk secret write needs at least one secret in it: an empty map would create a version " +
                "and change nothing.");
        }
        else
        {
            foreach (var (name, secret) in Secrets)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    problems.Add("every secret in a bulk write is keyed by the name it is stored under, and one key is blank.");
                }

                if (secret is null)
                {
                    // A removal, which is the point of a null here rather than something wrong with it.
                    continue;
                }

                try
                {
                    secret.EnsureValid();
                }
                catch (CFNet.Exceptions.CloudflareConfigurationException exception)
                {
                    problems.AddRange(exception.Problems.Select(problem => $"secret '{name}': {problem}"));
                }

                if (!string.Equals(secret.Name, name, StringComparison.Ordinal))
                {
                    problems.Add(
                        $"secret '{name}' carries the name '{secret.Name}': the key and the secret's own name " +
                        "have to agree, because Cloudflare stores it under the key.");
                }
            }
        }

        if (problems.Count > 0)
        {
            throw new CFNet.Exceptions.CloudflareConfigurationException(problems);
        }
    }
}

/// <summary>
/// Writes and reads a <see cref="WorkerSecretBulkRequest"/>, so that a secret mapped to null is sent as the
/// deletion it means.
/// </summary>
/// <remarks>
/// This library's serializer omits nulls, which is right for every other body it writes and wrong for this
/// one: in a bulk secret write, a null value is how Cloudflare is told to remove a secret, so dropping it
/// would turn a deletion into nothing at all. Writing the map by hand is the shortest way to be sure.
/// </remarks>
public sealed class WorkerSecretBulkRequestConverter : JsonConverter<WorkerSecretBulkRequest>
{
    /// <inheritdoc />
    public override WorkerSecretBulkRequest? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A bulk secret write is a JSON object of secrets and version tags.");
        }

        var request = new WorkerSecretBulkRequest();

        if (document.RootElement.TryGetProperty("secrets", out var secrets) &&
            secrets.ValueKind == JsonValueKind.Object)
        {
            var map = new Dictionary<string, WorkerSecretRequest?>();
            foreach (var property in secrets.EnumerateObject())
            {
                map[property.Name] = property.Value.ValueKind == JsonValueKind.Null
                    ? null
                    : property.Value.Deserialize<WorkerSecretRequest>(options);
            }

            request.Secrets = map;
        }

        if (document.RootElement.TryGetProperty("version_tags", out var tags) &&
            tags.ValueKind == JsonValueKind.Object)
        {
            request.VersionTags = tags.Deserialize<IDictionary<string, JsonElement>>(options);
        }

        return request;
    }

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        WorkerSecretBulkRequest value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();

        if (value.Secrets is { } secrets)
        {
            writer.WritePropertyName("secrets");
            writer.WriteStartObject();
            foreach (var entry in secrets)
            {
                writer.WritePropertyName(entry.Key);
                if (entry.Value is { } secret)
                {
                    JsonSerializer.Serialize(writer, secret, options);
                }
                else
                {
                    // The deletion. Written on purpose, where this library's writer would have dropped it.
                    writer.WriteNullValue();
                }
            }

            writer.WriteEndObject();
        }

        if (value.VersionTags is { Count: > 0 } tags)
        {
            writer.WritePropertyName("version_tags");
            JsonSerializer.Serialize(writer, tags, options);
        }

        writer.WriteEndObject();
    }
}
