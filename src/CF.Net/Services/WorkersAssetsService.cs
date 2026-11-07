using System.Text;
using CFNet.Http;
using CFNet.Models.Workers;

namespace CFNet.Services;

/// <summary>
/// The static assets a Worker serves: asking Cloudflare which of them it is missing, and sending the bytes it
/// asks for.
/// </summary>
/// <remarks>
/// <para>
/// A deployment with assets is three calls, and only the first two live here:
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// <see cref="CreateUploadSessionAsync"/> sends the manifest - every path the Worker should serve, with its
/// content hash and size - and Cloudflare answers with the hashes it does not already hold, grouped into
/// buckets, plus a JWT. Assets it has seen before are simply absent from the answer, so an unchanged site
/// uploads nothing.
/// </description>
/// </item>
/// <item>
/// <description>
/// The bytes go up with that JWT as the credential, not the API token: <see cref="UploadAssetAsync"/> sends one
/// asset per call, <see cref="UploadAssetsAsync"/> sends a bucket of them as one multipart call. Either answers
/// with a completion JWT, and the last one is the token the deployment redeems.
/// </description>
/// </item>
/// <item>
/// <description>
/// The script or version upload carries the completion JWT as
/// <see cref="UploadWorkerScriptRequest.Assets"/>, whose <see cref="WorkerAssetsConfiguration.Jwt"/> is exactly
/// that value - see <c>IWorkersScriptsService.UploadAsync</c> and <c>CreateVersionAsync</c>.
/// </description>
/// </item>
/// </list>
/// <para>
/// <b>The hash is the caller's to compute.</b> Cloudflare publishes it as an opaque string and never says how it
/// is built; wrangler uses the BLAKE3 hash of the file's base64-encoded bytes with its extension without the dot
/// appended, hex encoded and cut to 32 characters. This library does not implement BLAKE3, so it takes the hash
/// as given and uses it verbatim in the manifest, the upload path and the multipart part name - the three places
/// that must agree.
/// </para>
/// <para>
/// The session endpoint is part of the Workers script API and wants the same permission as a script upload,
/// <c>Workers Scripts</c> Write. The two upload endpoints want no permission group at all: their only credential
/// is the session JWT, which is why <see cref="CloudflareRequest.WithBearerToken"/> leaves the configured API
/// credentials off those calls.
/// </para>
/// <para>
/// Workers for Platforms has its own copy of the session endpoint under
/// <c>/accounts/{account_id}/workers/dispatch/namespaces/{namespace}/scripts/{script}/assets-upload-session</c>.
/// This library does not wrap dispatch namespaces, here or anywhere else.
/// </para>
/// </remarks>
public interface IWorkersAssetsService
{
    /// <summary>
    /// Asks which of a manifest's assets Cloudflare is missing, and gets the JWT that authorises uploading them.
    /// </summary>
    /// <param name="scriptName">The Worker the assets belong to; it must already exist.</param>
    /// <param name="manifest">The assets to serve; <see cref="WorkersAssetManifest.EnsureValid"/> is applied first.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The hashes still wanted, grouped into buckets, and the upload JWT.</returns>
    /// <remarks>
    /// <c>POST /accounts/{account_id}/workers/scripts/{script_name}/assets-upload-session</c>. Cloudflare has
    /// answered this with a null result before (cloudflare/workers-sdk issue 9465), which surfaces as a
    /// <see cref="Exceptions.CloudflareApiException"/> naming the ray rather than a null return.
    /// </remarks>
    Task<WorkersAssetsUploadSession> CreateUploadSessionAsync(
        string scriptName,
        WorkersAssetManifest manifest,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Uploads one asset's bytes.</summary>
    /// <param name="hash">The asset's hash, exactly as the manifest named it.</param>
    /// <param name="content">The asset's bytes.</param>
    /// <param name="jwt">The session JWT; sent as the bearer token instead of the API credentials.</param>
    /// <param name="contentType">
    /// The media type to store for the asset. Null sends <see cref="WorkersAssetContentTypes.Unknown"/>, which
    /// tells Cloudflare to serve the asset with no content type rather than a guessed one.
    /// </param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The completion JWT the deployment redeems.</returns>
    /// <remarks>
    /// <c>POST /accounts/{account_id}/workers/assets/upload/{hash}</c>, with the raw bytes as the body. This
    /// path is not in the published API reference; it is the single-asset upload wrangler uses when the session
    /// JWT's <c>wrangler_single_asset_uploads</c> claim allows it, readable through
    /// <see cref="WorkersAssetsToken.SingleAssetUploads"/>.
    /// </remarks>
    Task<WorkersAssetUpload> UploadAssetAsync(
        string hash,
        byte[] content,
        string jwt,
        string? contentType = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Uploads a bucket of assets as one multipart call.</summary>
    /// <param name="assets">The assets; every part is named by its hash and holds the bytes as base64.</param>
    /// <param name="jwt">The session JWT; sent as the bearer token instead of the API credentials.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The completion JWT the deployment redeems.</returns>
    /// <remarks>
    /// <c>POST /accounts/{account_id}/workers/assets/upload?base64=true</c>. Cloudflare's own client sends at
    /// most a bucket's worth of assets per call and limits how many calls run at once; the session JWT's
    /// <c>edge_kv_upload_concurrency</c> claim says what that limit is, readable through
    /// <see cref="WorkersAssetsToken.EffectiveUploadConcurrency"/>.
    /// </remarks>
    Task<WorkersAssetUpload> UploadAssetsAsync(
        IEnumerable<WorkersAssetUploadItem> assets,
        string jwt,
        string? accountId = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class WorkersAssetsService : CloudflareService, IWorkersAssetsService
{
    /// <summary>Creates the service over a transport.</summary>
    /// <param name="transport">The transport that carries the call.</param>
    public WorkersAssetsService(ICloudflareTransport transport)
        : base(transport)
    {
    }

    /// <inheritdoc />
    public async Task<WorkersAssetsUploadSession> CreateUploadSessionAsync(
        string scriptName,
        WorkersAssetManifest manifest,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        manifest.EnsureValid();

        var request = CloudflareRequest.Post(
            SessionPath(accountId, scriptName),
            new { manifest = manifest.AsDictionary() });
        request.OperationName = "Create Worker Asset Upload Session";

        var response = await Transport
            .SendAsync<WorkersAssetsUploadSession>(request, cancellationToken)
            .ConfigureAwait(false);

        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<WorkersAssetUpload> UploadAssetAsync(
        string hash,
        byte[] content,
        string jwt,
        string? contentType = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var request = new CloudflareRequest(HttpMethod.Post, UploadPath(accountId, hash))
        {
            Body = new BinaryRequestBody(content, ContentTypeOrDefault(contentType)),
            OperationName = "Upload Worker Asset",
        };
        request.WithBearerToken(Require(jwt, nameof(jwt)));

        var response = await Transport
            .SendAsync<WorkersAssetUpload>(request, cancellationToken)
            .ConfigureAwait(false);

        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<WorkersAssetUpload> UploadAssetsAsync(
        IEnumerable<WorkersAssetUploadItem> assets,
        string jwt,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assets);

        var items = new List<WorkersAssetUploadItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in assets)
        {
            ArgumentNullException.ThrowIfNull(asset);

            // The hash is the part's name and the key Cloudflare matches it against, so a blank one is refused
            // here rather than sent as a part nothing can find again.
            var hash = Require(asset.Hash, nameof(asset.Hash));
            if (!seen.Add(hash))
            {
                throw new Exceptions.CloudflareConfigurationException(
                    $"The asset hash '{hash}' appears more than once in one upload. Every part of this " +
                    "form is named by its hash, so the API would receive two parts it cannot tell apart.");
            }

            items.Add(asset);
        }

        if (items.Count == 0)
        {
            throw new Exceptions.CloudflareConfigurationException(
                "At least one asset must be supplied. When a session asks for nothing there is no upload to " +
                "make: the session's own JWT is already the completion token a deployment redeems.");
        }

        var body = new MultipartRequestBody();
        foreach (var item in items)
        {
            // ?base64=true tells the API the part bodies are base64 text rather than raw bytes, and the part is
            // named - and file-named - by the hash, which is how the API matches a part to a manifest entry.
            body.AddFile(
                Require(item.Hash, nameof(item.Hash)),
                Encoding.ASCII.GetBytes(item.ContentAsBase64()),
                item.EffectiveContentType,
                Require(item.Hash, nameof(item.Hash)));
        }

        var request = new CloudflareRequest(HttpMethod.Post, UploadsPath(accountId))
        {
            Body = body,
            OperationName = "Upload Worker Assets",
        };
        request.Query.Add("base64", "true");
        request.WithBearerToken(Require(jwt, nameof(jwt)));

        var response = await Transport
            .SendAsync<WorkersAssetUpload>(request, cancellationToken)
            .ConfigureAwait(false);

        return response.EnsureResult();
    }

    private static string ContentTypeOrDefault(string? contentType) =>
        string.IsNullOrWhiteSpace(contentType) ? WorkersAssetContentTypes.Unknown : contentType.Trim();

    private string SessionPath(string? accountId, string scriptName) =>
        $"{AccountPath(ResolveAccountId(accountId))}/workers/scripts/" +
        $"{CloudflareUrl.EncodeSegment(Require(scriptName, nameof(scriptName)))}/assets-upload-session";

    private string UploadsPath(string? accountId) =>
        $"{AccountPath(ResolveAccountId(accountId))}/workers/assets/upload";

    private string UploadPath(string? accountId, string? hash) =>
        $"{UploadsPath(accountId)}/{CloudflareUrl.EncodeSegment(Require(hash, nameof(hash)))}";
}
