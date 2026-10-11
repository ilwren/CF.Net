using System.Globalization;
using System.Text;

namespace CFNet.Http;

/// <summary>
/// Percent-encodes the pieces of a Cloudflare URL and assembles them into a <see cref="Uri"/>.
/// </summary>
/// <remarks>
/// Two details matter here:
/// <list type="number">
///   <item>
///     <description>
///       Path segments must be escaped with <see cref="Uri.EscapeDataString"/> so a KV key such as
///       <c>users/42/profile</c> travels as <c>users%2F42%2Fprofile</c>. Cloudflare treats an encoded
///       slash as data and a literal slash as a path separator, so the difference changes which key is
///       addressed.
///     </description>
///   </item>
///   <item>
///     <description>
///       The resulting <see cref="Uri"/> is created with
///       <see cref="UriCreationOptions.DangerousDisablePathAndQueryCanonicalization"/>. Without that
///       flag the <c>Uri</c> class normalises the path and turns <c>%2F</c> back into <c>/</c>, which
///       silently changes the key the request refers to. CF.Net always builds the query string itself
///       and escapes it explicitly, so disabling canonicalisation is safe here.
///     </description>
///   </item>
/// </list>
/// </remarks>
public static class CloudflareUrl
{
    /// <summary>
    /// Escapes a single path segment so that reserved characters are treated as data.
    /// </summary>
    /// <param name="segment">The raw value, e.g. a KV key or a Worker script name.</param>
    public static string EncodeSegment(string segment)
    {
        if (segment is null)
        {
            throw new ArgumentNullException(nameof(segment));
        }

        return Uri.EscapeDataString(segment);
    }

    /// <summary>
    /// Escapes every segment of a slash separated path while keeping the slashes themselves as
    /// separators. Use for building paths from variable numbers of components.
    /// </summary>
    /// <param name="path">The raw path.</param>
    public static string EncodePath(string path)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        var segments = path.Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            segments[i] = EncodeSegment(segments[i]);
        }

        return string.Join('/', segments);
    }

    /// <summary>
    /// Combines a base address, a relative path and a query string into an absolute
    /// <see cref="Uri"/> that preserves every percent-encoded character.
    /// </summary>
    /// <param name="baseAddress">Root of the API, e.g. <c>https://api.cloudflare.com/client/v4/</c>.</param>
    /// <param name="path">Relative path, already escaped by the caller.</param>
    /// <param name="query">Optional query string, rendered without the leading <c>?</c>.</param>
    public static Uri Build(Uri baseAddress, string path, string? query = null)
    {
        if (baseAddress is null)
        {
            throw new ArgumentNullException(nameof(baseAddress));
        }

        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        if (!baseAddress.IsAbsoluteUri)
        {
            throw new ArgumentException("Base address must be absolute.", nameof(baseAddress));
        }

        var builder = new StringBuilder();
        builder.Append(baseAddress.GetLeftPart(UriPartial.Path).TrimEnd('/'));
        builder.Append('/');
        builder.Append(path.Trim('/'));

        if (!string.IsNullOrEmpty(query))
        {
            builder.Append('?');
            builder.Append(query);
        }

        var creationOptions = new UriCreationOptions
        {
            DangerousDisablePathAndQueryCanonicalization = true,
        };

        return new Uri(builder.ToString(), creationOptions);
    }

    /// <summary>Formats an integer for use in a query string or path.</summary>
    public static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Formats a long for use in a query string or path.</summary>
    public static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Formats a boolean as <c>true</c>/<c>false</c>.</summary>
    public static string Format(bool value) => value ? "true" : "false";

    /// <summary>Formats a timestamp in the ISO-8601 form Cloudflare accepts.</summary>
    public static string Format(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
