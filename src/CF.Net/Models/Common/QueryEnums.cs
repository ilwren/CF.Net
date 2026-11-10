using CFNet.Serialization;

namespace CFNet.Models.Common;

/// <summary>Sort direction understood by list endpoints (<c>?direction=asc|desc</c>).</summary>
public enum SortDirection
{
    /// <summary>Ascending order.</summary>
    [CloudflareValue("asc")]
    Ascending = 1,

    /// <summary>Descending order.</summary>
    [CloudflareValue("desc")]
    Descending = 2,
}

/// <summary>
/// How a list filter should be matched (<c>?match=all</c> or <c>?match=any</c>).
/// </summary>
public enum FilterMatchMode
{
    /// <summary>All supplied conditions must match.</summary>
    [CloudflareValue("all")]
    All = 1,

    /// <summary>Any supplied condition may match.</summary>
    [CloudflareValue("any")]
    Any = 2,
}

/// <summary>Static helpers converting the library enums into their query string representation.</summary>
public static class QueryEnums
{
    /// <summary>Converts a sort direction to <c>asc</c> / <c>desc</c>.</summary>
    public static string ToWire(SortDirection direction) => CloudflareEnumConverter<SortDirection>.ToWire(direction);

    /// <summary>Converts a match mode to <c>all</c> / <c>any</c>.</summary>
    public static string ToWire(FilterMatchMode mode) => CloudflareEnumConverter<FilterMatchMode>.ToWire(mode);
}
