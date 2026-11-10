namespace CFNet.Http;

/// <summary>
/// Implemented by every "list parameters" object so a service can hand it to a request without
/// knowing which filters the endpoint supports.
/// </summary>
public interface ICloudflareQueryParameters
{
    /// <summary>Appends this object's filters to the request query string.</summary>
    /// <param name="query">The query string builder of the outgoing request.</param>
    void Apply(QueryStringBuilder query);
}

/// <summary>
/// Parameters shared by the endpoints that paginate with page numbers
/// (<c>?page=2&amp;per_page=50</c>): zones, DNS records, D1 databases, Pages projects.
/// </summary>
public abstract class CloudflarePagedParameters : ICloudflareQueryParameters
{
    /// <summary>1-based page number.</summary>
    public int? Page { get; set; }

    /// <summary>Page size. Cloudflare caps this per endpoint (usually 50, sometimes 1000).</summary>
    public int? PerPage { get; set; }

    /// <inheritdoc />
    public virtual void Apply(QueryStringBuilder query)
    {
        query.Add("page", Page);
        query.Add("per_page", PerPage);
    }
}

/// <summary>
/// Parameters shared by the endpoints that paginate with cursors
/// (<c>?cursor=...</c>): KV keys, R2 buckets, Worker scripts, Pages deployments.
/// </summary>
public abstract class CloudflareCursorParameters : ICloudflareQueryParameters
{
    /// <summary>Continuation cursor from a previous response.</summary>
    public string? Cursor { get; set; }

    /// <summary>Page size.</summary>
    public int? PerPage { get; set; }

    /// <inheritdoc />
    public virtual void Apply(QueryStringBuilder query)
    {
        query.AddIfNotEmpty("cursor", Cursor);
        query.Add("per_page", PerPage);
    }
}
