using System.Runtime.CompilerServices;
using System.Text.Json;
using CFNet.Http;
using CFNet.Models.Kv;

namespace CFNet.Services;

/// <summary>
/// Workers KV operations: namespaces, key listing and value read/write.
/// </summary>
/// <remarks>
/// KV keys are arbitrary strings up to 512 bytes and routinely contain <c>/</c>. This service encodes
/// every key with <see cref="CloudflareUrl.EncodeSegment"/> and the transport builds the URI with path
/// canonicalisation disabled, so <c>users/42</c> really addresses the key <c>users/42</c> instead of
/// two path segments.
/// </remarks>
public interface IKvService
{
    /// <summary>Lists namespaces one page at a time.</summary>
    /// <param name="parameters">Optional filters and paging.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CloudflarePage<KvNamespace>> ListNamespacesAsync(
        KvNamespaceListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Enumerates every namespace, following page-number pagination automatically.</summary>
    /// <param name="parameters">Optional filters; <c>Page</c> is ignored.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<KvNamespace> ListAllNamespacesAsync(
        KvNamespaceListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Finds a namespace by title.</summary>
    /// <param name="title">Namespace title.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The namespace, or <c>null</c> when no namespace has that title.</returns>
    Task<KvNamespace?> GetNamespaceByTitleAsync(string title, string? accountId = null, CancellationToken cancellationToken = default);

    /// <summary>Fetches one namespace.</summary>
    /// <param name="namespaceId">Namespace id.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The namespace, or <c>null</c> when it does not exist.</returns>
    Task<KvNamespace?> GetNamespaceAsync(string namespaceId, string? accountId = null, CancellationToken cancellationToken = default);

    /// <summary>Creates a namespace.</summary>
    /// <param name="title">Namespace title.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<KvNamespace> CreateNamespaceAsync(string title, string? accountId = null, CancellationToken cancellationToken = default);

    /// <summary>Renames a namespace.</summary>
    /// <param name="namespaceId">Namespace id.</param>
    /// <param name="title">The new title.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<KvNamespace> RenameNamespaceAsync(string namespaceId, string title, string? accountId = null, CancellationToken cancellationToken = default);

    /// <summary>Deletes a namespace together with every key it holds.</summary>
    /// <param name="namespaceId">Namespace id.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteNamespaceAsync(string namespaceId, string? accountId = null, CancellationToken cancellationToken = default);

    /// <summary>Lists keys in a namespace one page at a time.</summary>
    /// <param name="namespaceId">Namespace id.</param>
    /// <param name="parameters">Optional prefix, limit and cursor.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CloudflarePage<KvKey>> ListKeysAsync(
        string namespaceId,
        KvKeyListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Enumerates every key in a namespace, following cursor pagination automatically.</summary>
    /// <param name="namespaceId">Namespace id.</param>
    /// <param name="prefix">Optional key prefix filter.</param>
    /// <param name="limit">Page size, 10-1000.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<KvKey> ListAllKeysAsync(
        string namespaceId,
        string? prefix = null,
        int? limit = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a value. Returns a <see cref="KvValue"/> with <see cref="KvValue.Found"/> set to
    /// <c>false</c> when the key does not exist, rather than throwing.
    /// </summary>
    /// <param name="namespaceId">Namespace id.</param>
    /// <param name="key">The key.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<KvValue> GetValueAsync(string namespaceId, string key, string? accountId = null, CancellationToken cancellationToken = default);

    /// <summary>Reads a value as UTF-8 text, or <c>null</c> when the key does not exist.</summary>
    /// <param name="namespaceId">Namespace id.</param>
    /// <param name="key">The key.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string?> GetStringAsync(string namespaceId, string key, string? accountId = null, CancellationToken cancellationToken = default);

    /// <summary>Reads a value and deserialises it from JSON, or <c>default</c> when the key does not exist.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="namespaceId">Namespace id.</param>
    /// <param name="key">The key.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<T?> GetJsonAsync<T>(string namespaceId, string key, string? accountId = null, CancellationToken cancellationToken = default);

    /// <summary>Writes a value.</summary>
    /// <param name="namespaceId">Namespace id.</param>
    /// <param name="key">The key.</param>
    /// <param name="value">The value and its expiry settings. Validated locally first.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PutValueAsync(string namespaceId, string key, KvPutValueRequest value, string? accountId = null, CancellationToken cancellationToken = default);

    /// <summary>Writes a UTF-8 text value.</summary>
    /// <param name="namespaceId">Namespace id.</param>
    /// <param name="key">The key.</param>
    /// <param name="value">The text to store.</param>
    /// <param name="expirationTtl">Optional relative expiry in seconds, at least 60.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PutStringAsync(
        string namespaceId,
        string key,
        string value,
        int? expirationTtl = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Serialises an object as JSON and stores it.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="namespaceId">Namespace id.</param>
    /// <param name="key">The key.</param>
    /// <param name="value">The object to store.</param>
    /// <param name="expirationTtl">Optional relative expiry in seconds.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PutJsonAsync<T>(
        string namespaceId,
        string key,
        T value,
        int? expirationTtl = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a key.</summary>
    /// <param name="namespaceId">Namespace id.</param>
    /// <param name="key">The key.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteValueAsync(string namespaceId, string key, string? accountId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes and deletes several keys in one call. Bulk writes are the only way to attach JSON
    /// metadata to a value.
    /// </summary>
    /// <param name="namespaceId">Namespace id.</param>
    /// <param name="operations">The operations to apply.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task BulkWriteAsync(
        string namespaceId,
        IEnumerable<KvBulkWriteOperation> operations,
        string? accountId = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class KvService : CloudflareService, IKvService
{
    /// <summary>Creates the service.</summary>
    /// <param name="transport">The transport to execute requests through.</param>
    public KvService(ICloudflareTransport transport)
        : base(transport)
    {
    }

    /// <inheritdoc />
    public async Task<CloudflarePage<KvNamespace>> ListNamespacesAsync(
        KvNamespaceListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var account = ResolveAccountId(accountId);
        var request = Apply(CloudflareRequest.Get($"{AccountPath(account)}/storage/kv/namespaces"), parameters);
        request.OperationName = "List KV Namespaces";

        var response = await Transport.SendAsync<List<KvNamespace>>(request, cancellationToken).ConfigureAwait(false);
        return response.ToPage();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<KvNamespace> ListAllNamespacesAsync(
        KvNamespaceListParameters? parameters = null,
        string? accountId = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var account = ResolveAccountId(accountId);
        var page = 1;

        while (true)
        {
            var effective = new KvNamespaceListParameters
            {
                Search = parameters?.Search,
                PerPage = parameters?.PerPage,
                Page = page,
            };

            var result = await ListNamespacesAsync(effective, account, cancellationToken).ConfigureAwait(false);
            foreach (var ns in result.Items)
            {
                yield return ns;
            }

            if (result.Items.Count == 0 || !result.HasMore)
            {
                yield break;
            }

            page++;
        }
    }

    /// <inheritdoc />
    public async Task<KvNamespace?> GetNamespaceByTitleAsync(string title, string? accountId = null, CancellationToken cancellationToken = default)
    {
        Require(title, nameof(title));

        var page = await ListNamespacesAsync(
            new KvNamespaceListParameters { Search = title.Trim(), PerPage = 100 },
            accountId,
            cancellationToken).ConfigureAwait(false);

        // "search" is a substring filter, so insist on an exact title match.
        return page.Items.FirstOrDefault(n => string.Equals(n.Title, title.Trim(), StringComparison.Ordinal));
    }

    /// <inheritdoc />
    public async Task<KvNamespace?> GetNamespaceAsync(string namespaceId, string? accountId = null, CancellationToken cancellationToken = default)
    {
        var path = NamespacePath(ResolveAccountId(accountId), namespaceId);
        var request = CloudflareRequest.Get(path).Tolerate(System.Net.HttpStatusCode.NotFound);
        request.OperationName = "Get KV Namespace";

        var response = await Transport.SendAsync<KvNamespace>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<KvNamespace> CreateNamespaceAsync(string title, string? accountId = null, CancellationToken cancellationToken = default)
    {
        Require(title, nameof(title));

        var account = ResolveAccountId(accountId);
        var request = CloudflareRequest.Post(
            $"{AccountPath(account)}/storage/kv/namespaces",
            new CreateKvNamespaceRequest(title.Trim()));
        request.OperationName = "Create KV Namespace";

        var response = await Transport.SendAsync<KvNamespace>(request, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<KvNamespace> RenameNamespaceAsync(
        string namespaceId,
        string title,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        Require(title, nameof(title));

        var path = NamespacePath(ResolveAccountId(accountId), namespaceId);
        var request = CloudflareRequest.Put(path, new UpdateKvNamespaceRequest(title.Trim()));
        request.OperationName = "Rename KV Namespace";

        var response = await Transport.SendAsync<KvNamespace>(request, cancellationToken).ConfigureAwait(false);
        return response.Result ?? new KvNamespace { Id = namespaceId, Title = title.Trim() };
    }

    /// <inheritdoc />
    public async Task DeleteNamespaceAsync(string namespaceId, string? accountId = null, CancellationToken cancellationToken = default)
    {
        var path = NamespacePath(ResolveAccountId(accountId), namespaceId);
        var request = CloudflareRequest.Delete(path);
        request.OperationName = "Delete KV Namespace";

        await SendVoidAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CloudflarePage<KvKey>> ListKeysAsync(
        string namespaceId,
        KvKeyListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var path = NamespacePath(ResolveAccountId(accountId), namespaceId);
        var request = Apply(CloudflareRequest.Get($"{path}/keys"), parameters);
        request.OperationName = "List KV Keys";

        var response = await Transport.SendAsync<List<KvKey>>(request, cancellationToken).ConfigureAwait(false);
        return response.ToPage();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<KvKey> ListAllKeysAsync(
        string namespaceId,
        string? prefix = null,
        int? limit = null,
        string? accountId = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var account = ResolveAccountId(accountId);
        string? cursor = null;
        var guard = 0;

        while (true)
        {
            var parameters = new KvKeyListParameters { Prefix = prefix, Limit = limit, Cursor = cursor };
            var page = await ListKeysAsync(namespaceId, parameters, account, cancellationToken).ConfigureAwait(false);

            foreach (var key in page.Items)
            {
                yield return key;
            }

            var next = page.NextCursor;
            if (string.IsNullOrEmpty(next) || string.Equals(next, cursor, StringComparison.Ordinal) || page.Items.Count == 0)
            {
                yield break;
            }

            cursor = next;
            if (++guard > 100_000)
            {
                yield break;
            }
        }
    }

    /// <inheritdoc />
    public async Task<KvValue> GetValueAsync(
        string namespaceId,
        string key,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var path = KeyPath(ResolveAccountId(accountId), namespaceId, key);

        var request = CloudflareRequest.Get(path)
            .Tolerate(System.Net.HttpStatusCode.NotFound);
        request.OperationName = "Get KV Value";

        // The KV value endpoint returns the stored bytes verbatim, not a JSON envelope, and reports
        // the value's JSON metadata in a response header.
        var raw = await Transport.SendRawAsync(request, cancellationToken).ConfigureAwait(false);

        var found = raw.IsSuccessStatusCode && raw.StatusCode != System.Net.HttpStatusCode.NotFound;
        JsonElement? metadata = ParseMetadataHeader(raw.Header(CloudflareDefaults.Headers.KvMetadata));

        return new KvValue
        {
            Key = key,
            Content = found ? raw.Content : Array.Empty<byte>(),
            Metadata = metadata,
            ContentType = raw.ContentType,
            StatusCode = raw.StatusCode,
            Found = found,
        };
    }

    /// <inheritdoc />
    public async Task<string?> GetStringAsync(
        string namespaceId,
        string key,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var value = await GetValueAsync(namespaceId, key, accountId, cancellationToken).ConfigureAwait(false);
        return value.Found ? value.Text : null;
    }

    /// <inheritdoc />
    public async Task<T?> GetJsonAsync<T>(
        string namespaceId,
        string key,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var value = await GetValueAsync(namespaceId, key, accountId, cancellationToken).ConfigureAwait(false);
        return value.Found ? value.AsJson<T>() : default;
    }

    /// <inheritdoc />
    public async Task PutValueAsync(
        string namespaceId,
        string key,
        KvPutValueRequest value,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Validate();

        var path = KeyPath(ResolveAccountId(accountId), namespaceId, key);

        var request = new CloudflareRequest(HttpMethod.Put, path)
        {
            Body = new BinaryRequestBody(value.Content, value.ContentType),
            OperationName = "Put KV Value",
        };

        request.Query.Add("expiration", value.Expiration);
        request.Query.Add("expiration_ttl", value.ExpirationTtl);

        await SendVoidAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task PutStringAsync(
        string namespaceId,
        string key,
        string value,
        int? expirationTtl = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        if (value is null)
        {
            throw new Exceptions.CloudflareConfigurationException("A KV value must not be null; use DeleteValueAsync to remove a key.");
        }

        return PutValueAsync(
            namespaceId,
            key,
            KvPutValueRequest.FromText(value).WithTtl(expirationTtl),
            accountId,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task PutJsonAsync<T>(
        string namespaceId,
        string key,
        T value,
        int? expirationTtl = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        if (value is null)
        {
            throw new Exceptions.CloudflareConfigurationException("A KV value must not be null; use DeleteValueAsync to remove a key.");
        }

        return PutValueAsync(
            namespaceId,
            key,
            KvPutValueRequest.FromJson(value).WithTtl(expirationTtl),
            accountId,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteValueAsync(
        string namespaceId,
        string key,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var path = KeyPath(ResolveAccountId(accountId), namespaceId, key);
        var request = CloudflareRequest.Delete(path);
        request.OperationName = "Delete KV Value";

        await SendVoidAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task BulkWriteAsync(
        string namespaceId,
        IEnumerable<KvBulkWriteOperation> operations,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);

        var list = operations.ToList();
        if (list.Count == 0)
        {
            throw new Exceptions.CloudflareConfigurationException("A KV bulk write needs at least one operation.");
        }

        foreach (var operation in list)
        {
            if (string.IsNullOrEmpty(operation.Key))
            {
                throw new Exceptions.CloudflareConfigurationException("Every KV bulk operation needs a key.");
            }

            if (operation.Delete != true && operation.Value is null)
            {
                throw new Exceptions.CloudflareConfigurationException(
                    $"KV bulk operation for key '{operation.Key}' has neither a value nor Delete = true.");
            }
        }

        var path = NamespacePath(ResolveAccountId(accountId), namespaceId);
        var request = CloudflareRequest.Put($"{path}/bulk", list);
        request.OperationName = "Bulk Write KV";

        await SendVoidAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static string NamespacePath(string accountId, string namespaceId) =>
        $"{AccountPath(accountId)}/storage/kv/namespaces/{CloudflareUrl.EncodeSegment(Require(namespaceId, nameof(namespaceId)))}";

    private static string KeyPath(string accountId, string namespaceId, string key) =>
        $"{NamespacePath(accountId, namespaceId)}/values/{CloudflareUrl.EncodeSegment(Require(key, nameof(key)))}";

    private static JsonElement? ParseMetadataHeader(string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(headerValue);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            // A malformed metadata header must not fail the whole read.
            return null;
        }
    }
}
