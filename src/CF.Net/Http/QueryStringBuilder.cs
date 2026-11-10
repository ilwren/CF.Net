using System.Globalization;
using System.Text;

namespace CFNet.Http;

/// <summary>
/// Builds a query string with correct RFC 3986 percent-encoding.
/// </summary>
/// <remarks>
/// Values are escaped with <see cref="Uri.EscapeDataString(string)"/>, which unlike <c>WebUtility.UrlEncode</c>
/// also encodes <c>/</c>, <c>+</c> and <c>~</c>. That matters for parameters such as the DNS record
/// <c>name</c> filter or a KV <c>prefix</c>. Parameters with a <c>null</c> value are skipped so callers
/// can pass optional filters straight through. Duplicate names are preserved - Cloudflare accepts
/// repeated parameters for some filters (<c>tag=</c> on DNS records, for instance).
/// </remarks>
public sealed class QueryStringBuilder
{
    private readonly List<KeyValuePair<string, string>> _items = new();

    /// <summary>Number of parameters added so far.</summary>
    public int Count => _items.Count;

    /// <summary>True when no parameter was added.</summary>
    public bool IsEmpty => _items.Count == 0;

    /// <summary>The parameters in insertion order, already percent-encoded.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Items => _items;

    /// <summary>Adds a string parameter, skipping it when <paramref name="value"/> is null.</summary>
    public QueryStringBuilder Add(string name, string? value)
    {
        if (value is not null)
        {
            _items.Add(new(name, value));
        }

        return this;
    }

    /// <summary>Adds a string parameter, skipping it when the value is null or whitespace.</summary>
    public QueryStringBuilder AddIfNotEmpty(string name, string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? this : Add(name, value.Trim());
    }

    /// <summary>Adds an integer parameter, skipping it when null.</summary>
    public QueryStringBuilder Add(string name, int? value)
    {
        return value.HasValue
            ? Add(name, value.Value.ToString(CultureInfo.InvariantCulture))
            : this;
    }

    /// <summary>Adds a long parameter, skipping it when null.</summary>
    public QueryStringBuilder Add(string name, long? value)
    {
        return value.HasValue
            ? Add(name, value.Value.ToString(CultureInfo.InvariantCulture))
            : this;
    }

    /// <summary>Adds a boolean parameter as <c>true</c>/<c>false</c>, skipping it when null.</summary>
    public QueryStringBuilder Add(string name, bool? value)
    {
        return value.HasValue
            ? Add(name, value.Value ? "true" : "false")
            : this;
    }

    /// <summary>Adds a timestamp in the <c>yyyy-MM-ddTHH:mm:ssZ</c> form Cloudflare expects.</summary>
    public QueryStringBuilder Add(string name, DateTimeOffset? value)
    {
        return value.HasValue
            ? Add(name, value.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))
            : this;
    }

    /// <summary>Adds a timestamp using a caller supplied format.</summary>
    public QueryStringBuilder Add(string name, DateTimeOffset? value, string format)
    {
        return value.HasValue
            ? Add(name, value.Value.UtcDateTime.ToString(format, CultureInfo.InvariantCulture))
            : this;
    }

    /// <summary>Adds every element of a sequence under the same name.</summary>
    public QueryStringBuilder AddMany(string name, IEnumerable<string>? values)
    {
        if (values is null)
        {
            return this;
        }

        foreach (var value in values)
        {
            Add(name, value);
        }

        return this;
    }

    /// <summary>Adds a pre-formatted parameter without touching the name (the value is still escaped).</summary>
    public QueryStringBuilder AddRaw(string nameAndEquals, string value)
    {
        _items.Add(new(nameAndEquals, value));
        return this;
    }

    /// <summary>Renders the query string, without the leading <c>?</c>.</summary>
    public override string ToString()
    {
        if (_items.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < _items.Count; i++)
        {
            if (i > 0)
            {
                builder.Append('&');
            }

            builder.Append(EscapeName(_items[i].Key));
            builder.Append('=');
            builder.Append(Uri.EscapeDataString(_items[i].Value));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Escapes a parameter name. Names such as <c>account.id</c> must keep their dot, and must not
    /// be fully escaped, otherwise Cloudflare no longer recognises them.
    /// </summary>
    private static string EscapeName(string name)
    {
        // Escape everything except the characters that are legal (and meaningful) in a query name.
        return Uri.EscapeDataString(name)
            .Replace("%2E", ".", StringComparison.Ordinal)
            .Replace("%5B", "[", StringComparison.Ordinal)
            .Replace("%5D", "]", StringComparison.Ordinal);
    }
}
