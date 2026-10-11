using CFNet.Models.Common;

namespace CFNet.Http;

/// <summary>
/// One page of a paginated Cloudflare list call, plus the metadata needed to fetch the next one.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
public sealed class CloudflarePage<T>
{
    /// <summary>Creates an empty page.</summary>
    public CloudflarePage()
    {
        Items = Array.Empty<T>();
    }

    /// <summary>Creates a page from items and pagination metadata.</summary>
    /// <param name="items">The items on this page.</param>
    /// <param name="info">The <c>result_info</c> block, may be <c>null</c>.</param>
    public CloudflarePage(IReadOnlyList<T> items, CloudflareResultInfo? info)
    {
        Items = items ?? Array.Empty<T>();
        Info = info;
    }

    /// <summary>Items on this page.</summary>
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

    /// <summary>Raw pagination metadata from Cloudflare.</summary>
    public CloudflareResultInfo? Info { get; init; }

    /// <summary>Number of items on this page.</summary>
    public int Count => Items.Count;

    /// <summary>Total number of items across all pages, when Cloudflare reported it.</summary>
    public int? TotalCount => Info?.TotalCount;

    /// <summary>Current page number for page-number pagination, otherwise <c>null</c>.</summary>
    public int? Page => Info?.Page;

    /// <summary>Page size actually applied by the server.</summary>
    public int? PerPage => Info?.PerPage;

    /// <summary>True when another page can be fetched.</summary>
    public bool HasMore => Info?.HasNextPage ?? false;

    /// <summary>
    /// Cursor to pass to the next call for cursor paginated endpoints; <c>null</c> at the end.
    /// </summary>
    public string? NextCursor => Info?.NextCursor;

    /// <summary>
    /// Page number to pass to the next call for page-number paginated endpoints; <c>null</c> at the end.
    /// </summary>
    public int? NextPage => HasMore && Info?.Page is { } page ? page + 1 : null;

    /// <summary>Builds a page from a parsed response.</summary>
    /// <param name="response">The response whose <c>result</c> is the item list.</param>
    public static CloudflarePage<T> FromResponse(CloudflareResponse<List<T>?> response)
    {
        return new CloudflarePage<T>(response.Result ?? new List<T>(), response.ResultInfo);
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"Page of {Count} item(s)" +
        (TotalCount is { } total ? $" out of {total}" : string.Empty) +
        (HasMore ? " (more available)" : " (last page)");
}

/// <summary>Convenience helpers for turning responses into pages.</summary>
public static class CloudflareResponseExtensions
{
    /// <summary>Wraps a list response into a <see cref="CloudflarePage{T}"/>.</summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="response">The list response.</param>
    public static CloudflarePage<T> ToPage<T>(this CloudflareResponse<List<T>?> response) =>
        CloudflarePage<T>.FromResponse(response);

    /// <summary>
    /// Wraps a response whose <c>result</c> is an object containing a named array property
    /// (R2 returns <c>{ "buckets": [...] }</c>) into a page.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="response">The response.</param>
    /// <param name="selector">Extractor picking the array out of the result object.</param>
    public static CloudflarePage<T> ToPage<TWrapper, T>(
        this CloudflareResponse<TWrapper?> response,
        Func<TWrapper?, IReadOnlyList<T>> selector)
    {
        var items = selector(response.Result) ?? Array.Empty<T>();
        return new CloudflarePage<T>(items, response.ResultInfo);
    }
}
