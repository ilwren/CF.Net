using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.TestKit;

/// <summary>
/// What a <see cref="CassetteHandler"/> does with the network.
/// </summary>
public enum CassetteMode
{
    /// <summary>Forward every call; the cassette is left alone.</summary>
    Passthrough = 0,

    /// <summary>Forward every call and append the exchange to the cassette.</summary>
    Record = 1,

    /// <summary>Answer from the cassette and never open a socket.</summary>
    Replay = 2,
}

/// <summary>
/// One request/response pair exactly as it crossed the wire, minus the credentials.
/// </summary>
/// <remarks>
/// The shape is deliberately flat and JSON friendly: a cassette is meant to be committed, reviewed
/// in a pull request and diffed when Cloudflare changes a payload.
/// </remarks>
public sealed class RecordedExchange
{
    /// <summary>Optional label, usually the test that produced the exchange.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>HTTP method, upper case.</summary>
    [JsonPropertyName("method")]
    public string? Method { get; set; }

    /// <summary>
    /// Path and query of the request URI, for example <c>/client/v4/zones?page=1&amp;per_page=50</c>.
    /// The scheme and host are left out on purpose so a cassette recorded against the production API
    /// still replays when <see cref="CFNet.CloudflareDefaults.BaseAddress"/> is pointed elsewhere.
    /// </summary>
    [JsonPropertyName("pathAndQuery")]
    public string? PathAndQuery { get; set; }

    /// <summary>Request headers with credentials replaced by <c>&lt;redacted&gt;</c>.</summary>
    [JsonPropertyName("requestHeaders")]
    public Dictionary<string, string>? RequestHeaders { get; set; }

    /// <summary>Request body as text, when it was text.</summary>
    [JsonPropertyName("requestBody")]
    public string? RequestBody { get; set; }

    /// <summary>Request body as base64, when it was not text (a module upload, for example).</summary>
    [JsonPropertyName("requestBodyBase64")]
    public string? RequestBodyBase64 { get; set; }

    /// <summary>Response status code.</summary>
    [JsonPropertyName("statusCode")]
    public int StatusCode { get; set; }

    /// <summary>Response headers, excluding content headers and anything that sets a cookie.</summary>
    [JsonPropertyName("responseHeaders")]
    public Dictionary<string, string>? ResponseHeaders { get; set; }

    /// <summary>Value of the response <c>Content-Type</c> header.</summary>
    [JsonPropertyName("responseContentType")]
    public string? ResponseContentType { get; set; }

    /// <summary>Response body as text, when it was text.</summary>
    [JsonPropertyName("responseBody")]
    public string? ResponseBody { get; set; }

    /// <summary>Response body as base64, when it was not text.</summary>
    [JsonPropertyName("responseBodyBase64")]
    public string? ResponseBodyBase64 { get; set; }

    /// <summary>When the exchange was captured.</summary>
    [JsonPropertyName("recordedAt")]
    public DateTimeOffset RecordedAt { get; set; }

    /// <summary>The key used to match a replayed request: <c>METHOD path?query</c>.</summary>
    [JsonIgnore]
    public string MatchKey => $"{Method?.ToUpperInvariant() ?? "GET"} {PathAndQuery}";

    /// <summary>Decodes whichever body field is populated.</summary>
    /// <param name="text">The text body.</param>
    /// <param name="base64">The base64 body.</param>
    /// <returns>The body bytes, or an empty array when there was none.</returns>
    public static byte[] DecodeBody(string? text, string? base64)
    {
        if (!string.IsNullOrEmpty(base64))
        {
            return Convert.FromBase64String(base64);
        }

        return text is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(text);
    }

    /// <summary>Response body as bytes, whatever encoding it was stored in.</summary>
    [JsonIgnore]
    public byte[] ResponseBytes => DecodeBody(ResponseBody, ResponseBodyBase64);

    /// <summary>Request body as bytes, whatever encoding it was stored in.</summary>
    [JsonIgnore]
    public byte[] RequestBytes => DecodeBody(RequestBody, RequestBodyBase64);
}

/// <summary>
/// A named, ordered collection of <see cref="RecordedExchange"/>s stored as one JSON file.
/// </summary>
public sealed class Cassette
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Cassette name; also the file name without its extension.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Base URL the recording was made against.</summary>
    [JsonPropertyName("baseUrl")]
    public string? BaseUrl { get; set; }

    /// <summary>When recording started.</summary>
    [JsonPropertyName("recordedAt")]
    public DateTimeOffset RecordedAt { get; set; }

    /// <summary>CF.Net version that produced the cassette.</summary>
    [JsonPropertyName("libraryVersion")]
    public string? LibraryVersion { get; set; }

    /// <summary>The exchanges, in the order they happened.</summary>
    [JsonPropertyName("exchanges")]
    public List<RecordedExchange> Exchanges { get; set; } = new();

    /// <summary>Creates an empty, unnamed cassette.</summary>
    public Cassette()
    {
        RecordedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Creates an empty cassette.</summary>
    /// <param name="name">Cassette name.</param>
    /// <param name="baseUrl">Base URL being recorded.</param>
    public Cassette(string? name, string? baseUrl = null)
        : this()
    {
        Name = name;
        BaseUrl = baseUrl;
    }

    /// <summary>Reads a cassette from disk.</summary>
    /// <param name="path">Path to the JSON file.</param>
    /// <returns>The cassette.</returns>
    /// <exception cref="InvalidDataException">The file is not a valid cassette.</exception>
    public static Cassette Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        try
        {
            var cassette = JsonSerializer.Deserialize<Cassette>(File.ReadAllText(path), ReadOptions);
            if (cassette is null)
            {
                throw new InvalidDataException($"Cassette '{path}' deserialized to null.");
            }

            cassette.Name ??= Path.GetFileNameWithoutExtension(path);
            return cassette;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Cassette '{path}' is not valid JSON.", ex);
        }
    }

    /// <summary>
    /// Writes the cassette with stable formatting, so a re-recording that captured the same traffic
    /// produces no diff.
    /// </summary>
    /// <param name="path">Destination file. Parent directories are created.</param>
    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Normalise before writing: timestamps inside the payloads stay as Cloudflare sent them, but
        // the envelope only carries the recording date, not the time, so a same-day re-record is a
        // no-op diff.
        RecordedAt = new DateTimeOffset(RecordedAt.UtcDateTime.Date, TimeSpan.Zero);

        var json = JsonSerializer.Serialize(this, WriteOptions);
        File.WriteAllText(path, json.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n", new UTF8Encoding(false));
    }

    /// <summary>Finds every <c>*.json</c> cassette under a directory.</summary>
    /// <param name="directory">Directory to scan.</param>
    /// <returns>Full paths, sorted by name.</returns>
    public static IReadOnlyList<string> Discover(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);

        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        return Directory
            .EnumerateFiles(directory, "*.json", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
    }
}

/// <summary>
/// Thrown when a replayed request has no matching entry in the cassette. It means the code under test
/// asked Cloudflare something the recording never asked, which is exactly the drift a cassette exists
/// to catch.
/// </summary>
public sealed class CassetteMissException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="matchKey">The <c>METHOD path?query</c> that was not found.</param>
    /// <param name="cassetteName">The cassette that was searched.</param>
    public CassetteMissException(string matchKey, string? cassetteName)
        : base($"Cassette '{cassetteName ?? "(unnamed)"}' has no entry for '{matchKey}'. Re-record it, or fix the call.")
    {
        MatchKey = matchKey;
        CassetteName = cassetteName;
    }

    /// <summary>The request that could not be matched.</summary>
    public string MatchKey { get; }

    /// <summary>The cassette that was searched.</summary>
    public string? CassetteName { get; }
}

/// <summary>
/// Decides which headers and bodies are safe to persist. Cassettes are committed to the repository,
/// so this is the single place that keeps credentials out of them.
/// </summary>
public static class CassetteRedaction
{
    /// <summary>The placeholder a redacted value is replaced with.</summary>
    public const string Redacted = "<redacted>";

    private static readonly string[] SensitiveHeaders =
    {
        "authorization",
        "x-auth-key",
        "x-auth-email",
        "x-api-key",
        "cookie",
        "set-cookie",
    };

    /// <summary>True when a header must never be written to a cassette.</summary>
    /// <param name="headerName">Header name, any casing.</param>
    public static bool IsSensitive(string headerName) =>
        Array.Exists(SensitiveHeaders, candidate => string.Equals(candidate, headerName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Copies headers into a flat dictionary, one comma separated value per name, redacting the
    /// sensitive ones. Content headers are skipped: the cassette stores the content type separately.
    /// </summary>
    /// <param name="headers">Headers to copy.</param>
    /// <param name="skipContentHeaders">Whether to drop content headers.</param>
    /// <returns>An ordered dictionary suitable for JSON.</returns>
    public static Dictionary<string, string> Copy(
        IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers,
        bool skipContentHeaders = true)
    {
        var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in headers)
        {
            if (IsSensitive(header.Key))
            {
                copy[header.Key] = Redacted;
                continue;
            }

            if (skipContentHeaders && IsContentHeader(header.Key))
            {
                continue;
            }

            copy[header.Key] = string.Join(", ", header.Value);
        }

        return copy;
    }

    private static bool IsContentHeader(string name) =>
        name.StartsWith("content-", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "transfer-encoding", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Captures a body as text when it round trips through UTF-8, and as base64 otherwise.
    /// </summary>
    /// <param name="bytes">The body.</param>
    /// <param name="text">Receives the text form, or <see langword="null"/>.</param>
    /// <param name="base64">Receives the base64 form, or <see langword="null"/>.</param>
    public static void SplitBody(byte[]? bytes, out string? text, out string? base64)
    {
        text = null;
        base64 = null;

        if (bytes is null || bytes.Length == 0)
        {
            return;
        }

        // A strict decoder throws on invalid sequences, which is exactly the "is this text?" test.
        try
        {
            var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            var decoded = strict.GetString(bytes);
            if (!decoded.Contains('\0'))
            {
                text = decoded;
                return;
            }
        }
        catch (DecoderFallbackException)
        {
            // Not text; fall through to base64.
        }

        base64 = Convert.ToBase64String(bytes);
    }
}
