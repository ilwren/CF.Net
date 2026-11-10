using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Models.Workers;

/// <summary>Media type wrangler sends for an asset whose type it cannot guess.</summary>
/// <remarks>
/// <para>
/// <c>application/null</c> is not a registered media type: Cloudflare's asset API parses it to mean "store no
/// content type for this asset", so the asset is later served without a <c>Content-Type</c> header rather than
/// with a wrong one. Sending <c>application/octet-stream</c> instead would make a browser download an asset it
/// should display, which is why the sentinel exists.
/// </para>
/// </remarks>
public static class WorkersAssetContentTypes
{
    /// <summary>The sentinel for "no content type", used when an asset's type is unknown.</summary>
    public const string Unknown = "application/null";

    /// <summary>Media type for a UTF-8 text asset.</summary>
    public const string Text = "text/plain; charset=utf-8";

    /// <summary>Media type for an HTML asset.</summary>
    public const string Html = "text/html; charset=utf-8";

    /// <summary>Media type for a JavaScript module asset.</summary>
    public const string JavaScript = "application/javascript; charset=utf-8";

    /// <summary>Media type for a CSS asset.</summary>
    public const string Css = "text/css; charset=utf-8";

    /// <summary>Media type for a JSON asset.</summary>
    public const string Json = "application/json";
}

/// <summary>One entry of an asset manifest: how Cloudflare identifies the file and how big it is.</summary>
/// <remarks>
/// The <see cref="Hash"/> is the manifest's key into Cloudflare's asset storage and the path segment of the
/// upload call, so it must be the same string in both places. Cloudflare publishes it as an opaque
/// <c>string</c>; wrangler computes it as the BLAKE3 hash of the file's base64-encoded bytes concatenated with
/// its extension without the dot, hex encoded and cut to the first 32 characters. This library does not
/// reproduce that algorithm - it takes the hash the caller supplies - so a deployment built here must either
/// hash the same way or use a key scheme Cloudflare accepts for the account.
/// </remarks>
public sealed class WorkersAssetManifestEntry
{
    /// <summary>Creates an empty entry, for deserialisation.</summary>
    public WorkersAssetManifestEntry()
    {
    }

    /// <summary>Creates an entry.</summary>
    /// <param name="hash">The asset's content hash, as the upload endpoints key it.</param>
    /// <param name="size">The asset's size in bytes.</param>
    public WorkersAssetManifestEntry(string hash, long size)
    {
        Hash = hash;
        Size = size;
    }

    /// <summary>The asset's content hash.</summary>
    [JsonPropertyName("hash")]
    public string? Hash { get; set; }

    /// <summary>The asset's size in bytes.</summary>
    [JsonPropertyName("size")]
    public long? Size { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"{Hash ?? "?"} ({Size?.ToString() ?? "?"} bytes)";
}

/// <summary>
/// The files a Worker deployment wants to serve as static assets, keyed by the path Cloudflare matches requests
/// against.
/// </summary>
/// <remarks>
/// <para>
/// Keys are root-relative URL paths, not file system paths: the published schema's own example is
/// <c>/blog/hello-world.html</c>, and wrangler builds every key as a slash plus the file's path relative to the
/// assets directory with backslashes turned into slashes. <see cref="NormalizePath"/> applies that rule.
/// </para>
/// <para>
/// The manifest is what Cloudflare uses to decide what is missing: it answers with the hashes it does not
/// already hold, so an unchanged file is uploaded once per account and reused by later deployments.
/// </para>
/// </remarks>
public sealed class WorkersAssetManifest
{
    private readonly Dictionary<string, WorkersAssetManifestEntry> _entries = new(StringComparer.Ordinal);

    /// <summary>Creates an empty manifest.</summary>
    public WorkersAssetManifest()
    {
    }

    /// <summary>Creates a manifest holding one asset.</summary>
    /// <param name="path">The asset's request path, e.g. <c>/index.html</c>.</param>
    /// <param name="hash">The asset's content hash.</param>
    /// <param name="size">The asset's size in bytes.</param>
    public WorkersAssetManifest(string path, string hash, long size)
    {
        Add(path, hash, size);
    }

    /// <summary>The entries.</summary>
    public IReadOnlyDictionary<string, WorkersAssetManifestEntry> Entries => _entries;

    /// <summary>How many assets the manifest names.</summary>
    public int Count => _entries.Count;

    /// <summary>Adds an asset, or replaces the entry for a path that is already there.</summary>
    /// <param name="path">The asset's request path, e.g. <c>/index.html</c>.</param>
    /// <param name="hash">The asset's content hash.</param>
    /// <param name="size">The asset's size in bytes.</param>
    /// <returns>This manifest, for chaining.</returns>
    public WorkersAssetManifest Add(string path, string hash, long size) =>
        Add(path, new WorkersAssetManifestEntry(hash, size));

    /// <summary>Adds an asset, or replaces the entry for a path that is already there.</summary>
    /// <param name="path">The asset's request path, e.g. <c>/index.html</c>.</param>
    /// <param name="entry">The asset's hash and size.</param>
    /// <returns>This manifest, for chaining.</returns>
    public WorkersAssetManifest Add(string path, WorkersAssetManifestEntry entry)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(entry);

        _entries[path] = entry;
        return this;
    }

    /// <summary>Reports whether the manifest names a path.</summary>
    /// <param name="path">The asset's request path.</param>
    /// <returns>True when the path is in the manifest.</returns>
    public bool Contains(string path) => !string.IsNullOrEmpty(path) && _entries.ContainsKey(path);

    /// <summary>Removes an asset from the manifest.</summary>
    /// <param name="path">The asset's request path.</param>
    /// <returns>True when an entry was removed.</returns>
    public bool Remove(string path) => !string.IsNullOrEmpty(path) && _entries.Remove(path);

    /// <summary>
    /// Turns a relative file path into the manifest key Cloudflare expects: a leading slash, then the path with
    /// every backslash turned into a slash.
    /// </summary>
    /// <param name="relativePath">A path relative to the assets directory, e.g. <c>blog\hello-world.html</c>.</param>
    /// <returns>The root-relative request path, e.g. <c>/blog/hello-world.html</c>.</returns>
    /// <exception cref="ArgumentException">The path is empty, or holds nothing but separators.</exception>
    public static string NormalizePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(relativePath);

        var normalized = relativePath.Replace('\\', '/').Trim('/');
        if (normalized.Length == 0)
        {
            throw new ArgumentException(
                "An asset path must name a file, not just separators.",
                nameof(relativePath));
        }

        return "/" + normalized;
    }

    /// <summary>The manifest as the dictionary the request body carries.</summary>
    /// <returns>A copy of the entries.</returns>
    public Dictionary<string, WorkersAssetManifestEntry> AsDictionary() =>
        new(_entries, StringComparer.Ordinal);

    /// <summary>
    /// Checks the manifest before it is sent, so a malformed entry is reported here rather than by Cloudflare.
    /// </summary>
    /// <exception cref="Exceptions.CloudflareConfigurationException">One or more entries are unusable.</exception>
    /// <remarks>
    /// An empty manifest is allowed: Cloudflare answers it with no buckets, and the JWT it returns still
    /// redeems against a deployment.
    /// </remarks>
    public void EnsureValid()
    {
        var problems = new List<string>();

        foreach (var entry in _entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                problems.Add("An asset path is empty.");
            }

            if (string.IsNullOrWhiteSpace(entry.Value.Hash))
            {
                problems.Add($"The asset '{entry.Key}' has no hash.");
            }

            if (entry.Value.Size is null)
            {
                problems.Add($"The asset '{entry.Key}' has no size.");
            }
            else if (entry.Value.Size < 0)
            {
                problems.Add($"The asset '{entry.Key}' has a negative size ({entry.Value.Size}).");
            }
        }

        if (problems.Count > 0)
        {
            throw new Exceptions.CloudflareConfigurationException(problems);
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"{Count} asset(s)";
}

/// <summary>
/// The answer to an asset upload session request: which of the manifest's hashes Cloudflare still wants, and the
/// JWT that authorises sending them.
/// </summary>
/// <remarks>
/// <para>
/// <c>buckets</c> is documented as "the requests to make to upload assets": an array of arrays of hashes. Each
/// inner array is one upload request, so Cloudflare decides how the work is grouped - a bucket of several hashes
/// goes up as one multipart call, a bucket of one goes up as a single call. Hashes that are already stored are
/// simply absent, which is how an unchanged asset is skipped.
/// </para>
/// <para>
/// The JWT authenticates the upload calls instead of the API token: it is sent as
/// <c>Authorization: Bearer …</c> on <c>POST …/workers/assets/upload/{hash}</c> and on the multipart
/// <c>POST …/workers/assets/upload?base64=true</c>. It also carries hints about how to upload, readable with
/// <see cref="ReadToken"/>.
/// </para>
/// <para>
/// Cloudflare has answered this endpoint with a null result in the past (cloudflare/workers-sdk issue 9465),
/// which <see cref="Http.CloudflareResponse{T}.EnsureResult"/> turns into an exception carrying the ray id
/// rather than a null reference later on.
/// </para>
/// </remarks>
public sealed class WorkersAssetsUploadSession
{
    /// <summary>The upload requests to make: each inner list is one call's hashes.</summary>
    [JsonPropertyName("buckets")]
    public List<List<string>>? Buckets { get; set; }

    /// <summary>The JWT to authenticate the upload calls with.</summary>
    [JsonPropertyName("jwt")]
    public string? Jwt { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Every hash Cloudflare asked for, in bucket order.</summary>
    [JsonIgnore]
    public IEnumerable<string> RequestedHashes =>
        (Buckets ?? new List<List<string>>())
            .Where(bucket => bucket is not null)
            .SelectMany(bucket => bucket.Where(hash => !string.IsNullOrEmpty(hash)));

    /// <summary>How many hashes Cloudflare asked for.</summary>
    [JsonIgnore]
    public int RequestedCount => RequestedHashes.Count();

    /// <summary>True when Cloudflare already holds every asset in the manifest.</summary>
    [JsonIgnore]
    public bool NothingToUpload => RequestedCount == 0;

    /// <summary>Reads the session JWT's payload, when it has one.</summary>
    /// <returns>The token's claims and hints, or null when the JWT is absent or unparsable.</returns>
    public WorkersAssetsToken? ReadToken() => WorkersAssetsToken.TryRead(Jwt);

    /// <inheritdoc />
    public override string ToString() =>
        $"{RequestedCount} hash(es) to upload in {Buckets?.Count ?? 0} bucket(s)";
}

/// <summary>The answer to an asset upload call: the completion JWT a deployment redeems.</summary>
/// <remarks>
/// The published schema calls this "a completion JWT which can be redeemed when creating a Worker version":
/// set it as <see cref="WorkerAssetsConfiguration.Jwt"/> on the script or version upload that follows. It is
/// published as optional and it behaves like it - a live single-asset upload answered 200 with no token at all,
/// while the multipart call beside it answered one. Keep the last non-empty token and fall back to the
/// session's own JWT, which is what Cloudflare's own client does, and which is already the right answer when a
/// session asks for nothing to be uploaded.
/// </remarks>
public sealed class WorkersAssetUpload
{
    /// <summary>The completion JWT, redeemed by the deployment that follows.</summary>
    [JsonPropertyName("jwt")]
    public string? Jwt { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Reads the completion JWT's payload, when it has one.</summary>
    /// <returns>The token's claims and hints, or null when the JWT is absent or unparsable.</returns>
    public WorkersAssetsToken? ReadToken() => WorkersAssetsToken.TryRead(Jwt);

    /// <inheritdoc />
    public override string ToString() => Jwt is null ? "no completion token" : "completion token";
}

/// <summary>One asset's bytes, ready for an upload call.</summary>
/// <remarks>
/// The hash is both the part name and the file name of the multipart call, and the path segment of the
/// single-asset call, so it must match the manifest entry the session was asked about.
/// </remarks>
public sealed class WorkersAssetUploadItem
{
    /// <summary>Creates an upload item.</summary>
    /// <param name="hash">The asset's content hash, as the manifest names it.</param>
    /// <param name="content">The asset's bytes.</param>
    /// <param name="contentType">
    /// The media type to store for the asset; <see cref="WorkersAssetContentTypes.Unknown"/> when null, which
    /// tells Cloudflare to serve the asset with no content type.
    /// </param>
    public WorkersAssetUploadItem(string hash, byte[] content, string? contentType = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(hash);
        ArgumentNullException.ThrowIfNull(content);

        Hash = hash;
        Content = content;
        ContentType = contentType;
    }

    /// <summary>Creates an upload item from text.</summary>
    /// <param name="hash">The asset's content hash, as the manifest names it.</param>
    /// <param name="text">The asset's text, sent as UTF-8.</param>
    /// <param name="contentType">The media type to store; <see cref="WorkersAssetContentTypes.Text"/> when null.</param>
    /// <returns>The item.</returns>
    public static WorkersAssetUploadItem ForText(string hash, string text, string? contentType = null) =>
        new(
            hash,
            Encoding.UTF8.GetBytes(text ?? string.Empty),
            contentType ?? WorkersAssetContentTypes.Text);

    /// <summary>The asset's content hash.</summary>
    public string Hash { get; }

    /// <summary>The asset's bytes.</summary>
    public byte[] Content { get; }

    /// <summary>The media type supplied by the caller, or null.</summary>
    public string? ContentType { get; }

    /// <summary>The media type to send: the caller's, or the "no content type" sentinel.</summary>
    public string EffectiveContentType =>
        string.IsNullOrWhiteSpace(ContentType) ? WorkersAssetContentTypes.Unknown : ContentType.Trim();

    /// <summary>The asset's size in bytes, as a manifest entry reports it.</summary>
    public long Size => Content.Length;

    /// <summary>The asset's bytes as base64, which is what the multipart call sends.</summary>
    public string ContentAsBase64() => Convert.ToBase64String(Content);

    /// <inheritdoc />
    public override string ToString() => $"{Hash} ({Content.Length} bytes, {EffectiveContentType})";
}

/// <summary>
/// The payload of one of the asset JWTs: its claims, plus the two hints Cloudflare puts there for uploaders.
/// </summary>
/// <remarks>
/// <para>
/// These tokens are ordinary JWTs, and this reads the payload without verifying the signature - there is no
/// public key to verify against and the token is only ever sent back to Cloudflare. Callers should treat the
/// claims as advice, not as trusted input.
/// </para>
/// <para>
/// The two hints wrangler acts on are <c>wrangler_single_asset_uploads</c>, which says whether the
/// single-asset endpoint may be used, and <c>edge_kv_upload_concurrency</c>, which says how many buckets may be
/// uploaded at once. <c>exp</c> is the token's expiry, and a deployment that waits too long has to ask for a new
/// session.
/// </para>
/// </remarks>
public sealed class WorkersAssetsToken
{
    /// <summary>How many buckets wrangler uploads at once when the token does not say.</summary>
    public const int DefaultUploadConcurrency = 50;

    private WorkersAssetsToken(
        IReadOnlyDictionary<string, JsonElement> claims,
        bool? singleAssetUploads,
        int? uploadConcurrency,
        DateTimeOffset? expiresAt)
    {
        Claims = claims;
        SingleAssetUploads = singleAssetUploads;
        UploadConcurrency = uploadConcurrency;
        ExpiresAt = expiresAt;
    }

    /// <summary>Every claim in the payload.</summary>
    public IReadOnlyDictionary<string, JsonElement> Claims { get; }

    /// <summary>The <c>wrangler_single_asset_uploads</c> claim, or null when absent.</summary>
    public bool? SingleAssetUploads { get; }

    /// <summary>The <c>edge_kv_upload_concurrency</c> claim, or null when absent.</summary>
    public int? UploadConcurrency { get; }

    /// <summary>The <c>exp</c> claim as a timestamp, or null when absent.</summary>
    public DateTimeOffset? ExpiresAt { get; }

    /// <summary>
    /// How many buckets may be uploaded at once: the token's advice when it gives a positive number,
    /// <see cref="DefaultUploadConcurrency"/> otherwise.
    /// </summary>
    [JsonIgnore]
    public int EffectiveUploadConcurrency =>
        UploadConcurrency is > 0 ? UploadConcurrency!.Value : DefaultUploadConcurrency;

    /// <summary>True when the token carries an <c>exp</c> claim that has passed.</summary>
    [JsonIgnore]
    public bool IsExpired => ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow;

    /// <summary>
    /// Reads a JWT's payload without verifying its signature.
    /// </summary>
    /// <param name="jwt">The token; may be null.</param>
    /// <returns>The token's claims, or null when there is nothing readable.</returns>
    public static WorkersAssetsToken? TryRead(string? jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt))
        {
            return null;
        }

        var parts = jwt.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        var payload = DecodeBase64Url(parts[1]);
        if (payload is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var claims = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var claim in document.RootElement.EnumerateObject())
            {
                claims[claim.Name] = claim.Value.Clone();
            }

            bool? singleAssetUploads = null;
            if (claims.TryGetValue("wrangler_single_asset_uploads", out var single))
            {
                if (single.ValueKind == JsonValueKind.True)
                {
                    singleAssetUploads = true;
                }
                else if (single.ValueKind == JsonValueKind.False)
                {
                    singleAssetUploads = false;
                }
            }

            int? uploadConcurrency = null;
            if (claims.TryGetValue("edge_kv_upload_concurrency", out var concurrency)
                && concurrency.ValueKind == JsonValueKind.Number
                && concurrency.TryGetDouble(out var concurrencyValue))
            {
                uploadConcurrency = (int)concurrencyValue;
            }

            DateTimeOffset? expiresAt = null;
            if (claims.TryGetValue("exp", out var expiry)
                && expiry.ValueKind == JsonValueKind.Number
                && expiry.TryGetDouble(out var expirySeconds))
            {
                expiresAt = DateTimeOffset.FromUnixTimeSeconds((long)expirySeconds);
            }

            return new WorkersAssetsToken(claims, singleAssetUploads, uploadConcurrency, expiresAt);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Decodes a base64url segment, tolerating missing padding.</summary>
    /// <param name="value">The segment.</param>
    /// <returns>The decoded bytes, or null when the segment cannot be base64.</returns>
    private static byte[]? DecodeBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 0:
                break;
            case 2:
                padded += "==";
                break;
            case 3:
                padded += "=";
                break;
            default:
                return null;
        }

        try
        {
            return Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"{Claims.Count} claim(s)" +
        (ExpiresAt is { } expiry ? $", expires {expiry:O}" : string.Empty);
}
