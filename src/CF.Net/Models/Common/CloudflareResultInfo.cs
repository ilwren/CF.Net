using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Models.Common;

/// <summary>
/// The <c>result_info</c> block Cloudflare returns next to a paginated <c>result</c>.
/// </summary>
/// <remarks>
/// Two pagination styles coexist in the v4 API:
/// <list type="bullet">
///   <item><description>Page numbers - <see cref="Page"/>, <see cref="TotalPages"/>, <see cref="TotalCount"/> (zones, DNS records, D1, Pages).</description></item>
///   <item><description>Cursors - <see cref="Cursor"/> and/or <see cref="Cursors"/> (KV keys, R2 buckets, Worker scripts).</description></item>
/// </list>
/// All members are nullable so a single type can represent either style.
/// </remarks>
public sealed class CloudflareResultInfo
{
    /// <summary>Current page number (1 based).</summary>
    [JsonPropertyName("page")]
    public int? Page { get; set; }

    /// <summary>Page size actually applied by the server.</summary>
    [JsonPropertyName("per_page")]
    public int? PerPage { get; set; }

    /// <summary>Total number of pages.</summary>
    [JsonPropertyName("total_pages")]
    public int? TotalPages { get; set; }

    /// <summary>Number of items in the current page.</summary>
    [JsonPropertyName("count")]
    public int? Count { get; set; }

    /// <summary>Total number of items across all pages.</summary>
    [JsonPropertyName("total_count")]
    public int? TotalCount { get; set; }

    /// <summary>Continuation token for the next page (cursor pagination).</summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; set; }

    /// <summary>Before/after cursor pair used by KV key listing.</summary>
    [JsonPropertyName("cursors")]
    public CloudflareCursors? Cursors { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>
    /// The token to pass back to the API to fetch the following page, regardless of which cursor
    /// flavour the endpoint used. <c>null</c> means "no more pages".
    /// </summary>
    [JsonIgnore]
    public string? NextCursor =>
        !string.IsNullOrEmpty(Cursors?.After) ? Cursors!.After
        : !string.IsNullOrEmpty(Cursor) ? Cursor
        : null;

    /// <summary>True when page-number pagination reports further pages.</summary>
    [JsonIgnore]
    public bool HasNextPage =>
        Page.HasValue && TotalPages.HasValue
            ? Page.Value < TotalPages.Value
            : NextCursor is not null;
}

/// <summary>The <c>cursors</c> object returned by cursor paginated endpoints.</summary>
public sealed class CloudflareCursors
{
    /// <summary>Token pointing at the previous page.</summary>
    [JsonPropertyName("before")]
    public string? Before { get; set; }

    /// <summary>Token pointing at the next page; empty when the end was reached.</summary>
    [JsonPropertyName("after")]
    public string? After { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}
