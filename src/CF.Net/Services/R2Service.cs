using System.Runtime.CompilerServices;
using CFNet.Http;
using CFNet.Models.R2;
using CFNet.Serialization;

namespace CFNet.Services;

/// <summary>
/// R2 bucket management: <c>/accounts/{account_id}/r2/buckets</c>.
/// </summary>
/// <remarks>
/// <para>
/// This covers the Cloudflare management API - creating, listing, inspecting and deleting buckets.
/// Reading and writing <em>objects</em> inside a bucket is not part of the v4 REST API: R2 exposes an
/// S3-compatible endpoint at <c>https://{account_id}.r2.cloudflarestorage.com</c> that requires
/// separately issued R2 access keys and AWS SigV4 signing. Use an S3 client for object operations and
/// this service for bucket lifecycle.
/// </para>
/// <para>
/// Jurisdictional buckets are addressed with the <c>cf-r2-jurisdiction</c> request header rather than a
/// query parameter, which is why the jurisdiction lives on the parameters/request objects and is turned
/// into a header here.
/// </para>
/// </remarks>
public interface IR2Service
{
    /// <summary>Lists buckets one page at a time.</summary>
    /// <param name="parameters">Optional filters, cursor and jurisdiction.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CloudflarePage<R2Bucket>> ListBucketsAsync(
        R2BucketListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Enumerates every bucket, following cursor pagination automatically.</summary>
    /// <param name="parameters">Optional filters and jurisdiction; <see cref="R2BucketListParameters.Cursor"/> is ignored.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<R2Bucket> ListAllBucketsAsync(
        R2BucketListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches one bucket by name.</summary>
    /// <param name="bucketName">Bucket name.</param>
    /// <param name="jurisdiction">Optional jurisdiction header value.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The bucket, or <c>null</c> when it does not exist.</returns>
    Task<R2Bucket?> GetBucketAsync(
        string bucketName,
        R2Jurisdiction? jurisdiction = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a bucket.</summary>
    /// <param name="request">Name, optional location hint, storage class and jurisdiction. Validated locally first.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<R2Bucket> CreateBucketAsync(CreateR2BucketRequest request, string? accountId = null, CancellationToken cancellationToken = default);

    /// <summary>Creates a bucket with the given name.</summary>
    /// <param name="bucketName">Bucket name: 3-63 characters of lowercase letters, digits and hyphens.</param>
    /// <param name="locationHint">Optional preferred location.</param>
    /// <param name="storageClass">Optional default storage class.</param>
    /// <param name="jurisdiction">Optional data residency jurisdiction.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<R2Bucket> CreateBucketAsync(
        string bucketName,
        R2LocationHint? locationHint = null,
        R2StorageClass? storageClass = null,
        R2Jurisdiction? jurisdiction = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Changes the default storage class of an existing bucket.</summary>
    /// <param name="bucketName">Bucket name.</param>
    /// <param name="storageClass">The new default storage class. Existing objects keep their class.</param>
    /// <param name="jurisdiction">Optional jurisdiction header value.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<R2Bucket> UpdateBucketAsync(
        string bucketName,
        R2StorageClass storageClass,
        R2Jurisdiction? jurisdiction = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a bucket. The bucket must be empty; Cloudflare rejects the call otherwise.</summary>
    /// <param name="bucketName">Bucket name.</param>
    /// <param name="jurisdiction">Optional jurisdiction header value.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteBucketAsync(
        string bucketName,
        R2Jurisdiction? jurisdiction = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class R2Service : CloudflareService, IR2Service
{
    /// <summary>Creates the service.</summary>
    /// <param name="transport">The transport to execute requests through.</param>
    public R2Service(ICloudflareTransport transport)
        : base(transport)
    {
    }

    /// <inheritdoc />
    public async Task<CloudflarePage<R2Bucket>> ListBucketsAsync(
        R2BucketListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var account = ResolveAccountId(accountId);
        var request = Apply(CloudflareRequest.Get($"{AccountPath(account)}/r2/buckets"), parameters);
        request.OperationName = "List R2 Buckets";
        ApplyJurisdiction(request, parameters?.Jurisdiction);

        var response = await Transport.SendAsync<R2BucketListResult>(request, cancellationToken).ConfigureAwait(false);

        return new CloudflarePage<R2Bucket>(
            response.Result?.Buckets ?? new List<R2Bucket>(),
            response.ResultInfo);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<R2Bucket> ListAllBucketsAsync(
        R2BucketListParameters? parameters = null,
        string? accountId = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var account = ResolveAccountId(accountId);
        string? cursor = null;
        var guard = 0;

        while (true)
        {
            var effective = new R2BucketListParameters
            {
                PerPage = parameters?.PerPage,
                NameContains = parameters?.NameContains,
                Order = parameters?.Order,
                Direction = parameters?.Direction,
                StartAfter = parameters?.StartAfter,
                Jurisdiction = parameters?.Jurisdiction,
                Cursor = cursor,
            };

            var page = await ListBucketsAsync(effective, account, cancellationToken).ConfigureAwait(false);
            foreach (var bucket in page.Items)
            {
                yield return bucket;
            }

            var next = page.NextCursor;
            if (string.IsNullOrEmpty(next) || string.Equals(next, cursor, StringComparison.Ordinal) || page.Items.Count == 0)
            {
                yield break;
            }

            cursor = next;
            if (++guard > 10_000)
            {
                yield break;
            }
        }
    }

    /// <inheritdoc />
    public async Task<R2Bucket?> GetBucketAsync(
        string bucketName,
        R2Jurisdiction? jurisdiction = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var path = BucketPath(ResolveAccountId(accountId), bucketName);
        var request = CloudflareRequest.Get(path).Tolerate(System.Net.HttpStatusCode.NotFound);
        request.OperationName = "Get R2 Bucket";
        ApplyJurisdiction(request, jurisdiction);

        var response = await Transport.SendAsync<R2Bucket>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<R2Bucket> CreateBucketAsync(CreateR2BucketRequest request, string? accountId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateBucketName(request.Name);

        var account = ResolveAccountId(accountId);
        var httpRequest = CloudflareRequest.Post($"{AccountPath(account)}/r2/buckets", request);
        httpRequest.OperationName = "Create R2 Bucket";
        ApplyJurisdiction(httpRequest, request.Jurisdiction);

        var response = await Transport.SendAsync<R2Bucket>(httpRequest, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public Task<R2Bucket> CreateBucketAsync(
        string bucketName,
        R2LocationHint? locationHint = null,
        R2StorageClass? storageClass = null,
        R2Jurisdiction? jurisdiction = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        return CreateBucketAsync(
            new CreateR2BucketRequest(bucketName, locationHint, storageClass) { Jurisdiction = jurisdiction },
            accountId,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<R2Bucket> UpdateBucketAsync(
        string bucketName,
        R2StorageClass storageClass,
        R2Jurisdiction? jurisdiction = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var path = BucketPath(ResolveAccountId(accountId), bucketName);
        var httpRequest = CloudflareRequest.Put(path, new UpdateR2BucketRequest(storageClass));
        httpRequest.OperationName = "Update R2 Bucket";
        ApplyJurisdiction(httpRequest, jurisdiction);

        var response = await Transport.SendAsync<R2Bucket>(httpRequest, cancellationToken).ConfigureAwait(false);
        return response.Result ?? new R2Bucket { Name = bucketName, StorageClass = storageClass };
    }

    /// <inheritdoc />
    public async Task DeleteBucketAsync(
        string bucketName,
        R2Jurisdiction? jurisdiction = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var path = BucketPath(ResolveAccountId(accountId), bucketName);
        var request = CloudflareRequest.Delete(path);
        request.OperationName = "Delete R2 Bucket";
        ApplyJurisdiction(request, jurisdiction);

        await SendVoidAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static string BucketPath(string accountId, string bucketName) =>
        $"{AccountPath(accountId)}/r2/buckets/{CloudflareUrl.EncodeSegment(Require(bucketName, nameof(bucketName)))}";

    private static void ApplyJurisdiction(CloudflareRequest request, R2Jurisdiction? jurisdiction)
    {
        if (jurisdiction is null || jurisdiction == R2Jurisdiction.Unknown)
        {
            return;
        }

        if (CloudflareEnumConverter<R2Jurisdiction>.TryToWire(jurisdiction.Value, out var value))
        {
            request.WithHeader(CloudflareDefaults.Headers.R2Jurisdiction, value);
        }
    }

    private static void ValidateBucketName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new Exceptions.CloudflareConfigurationException("An R2 bucket name is required.");
        }

        var trimmed = name.Trim();

        if (trimmed.Length < 3 || trimmed.Length > 63)
        {
            throw new Exceptions.CloudflareConfigurationException(
                $"R2 bucket names must be between 3 and 63 characters long (got {trimmed.Length}).");
        }

        if (!char.IsAsciiLetterLower(trimmed[0]) && !char.IsAsciiDigit(trimmed[0]))
        {
            throw new Exceptions.CloudflareConfigurationException(
                $"R2 bucket name '{trimmed}' must start with a lowercase letter or a digit.");
        }

        foreach (var character in trimmed)
        {
            if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '-')
            {
                throw new Exceptions.CloudflareConfigurationException(
                    $"R2 bucket name '{trimmed}' may only contain lowercase letters, digits and hyphens.");
            }
        }

        if (trimmed.Contains("--", StringComparison.Ordinal))
        {
            throw new Exceptions.CloudflareConfigurationException(
                $"R2 bucket name '{trimmed}' must not contain consecutive hyphens.");
        }

        var last = trimmed[^1];
        if (!char.IsAsciiLetterLower(last) && !char.IsAsciiDigit(last))
        {
            throw new Exceptions.CloudflareConfigurationException(
                $"R2 bucket name '{trimmed}' must end with a lowercase letter or a digit.");
        }

        // Both prefixes are reserved by the S3 compatibility layer.
        if (trimmed.StartsWith("xn--", StringComparison.Ordinal))
        {
            throw new Exceptions.CloudflareConfigurationException(
                $"R2 bucket name '{trimmed}' must not begin with 'xn--'.");
        }
    }
}
