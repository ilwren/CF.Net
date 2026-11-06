using System.Globalization;

namespace CFNet.Serialization;

/// <summary>
/// Reads the two ways Cloudflare writes a timestamp.
/// </summary>
/// <remarks>
/// <para>
/// Most of the API answers ISO 8601 with an offset - <c>2019-12-27T18:11:19.117Z</c> - and System.Text.Json
/// reads that by itself, which is why most of this library's timestamps are <see cref="DateTimeOffset"/>
/// properties. Workers AI is not most of the API: a model's <c>created_at</c>, and a fine-tune's
/// <c>created_at</c> and <c>modified_at</c>, arrive as <c>2024-05-08 02:23:55.897</c> - a space where the
/// standard puts a <c>T</c>, and no offset at all - which the strict ISO 8601 reader refuses outright. So
/// those fields stay the string that arrived, and this reads one for a caller who wants an instant.
/// </para>
/// <para>
/// A string with no offset is read as <b>UTC</b>, because that is the only zone Cloudflare's API reports in
/// and its ISO answers say so with their <c>Z</c>. It is an assumption all the same, which is why the raw
/// string is kept beside the instant: a caller who would rather not trust the assumption still has it.
/// </para>
/// <para>
/// Everything is returned at offset zero, including a string that carried its own offset, so two timestamps
/// from two endpoints can be compared without either of them being converted first.
/// </para>
/// </remarks>
public static class CloudflareTimestamps
{
    /// <summary>The shapes read exactly, before falling back to a general parse.</summary>
    private static readonly string[] Formats =
    {
        "yyyy-MM-dd HH:mm:ss.fffffff",
        "yyyy-MM-dd HH:mm:ss.fff",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd",
    };

    /// <summary>Reads a timestamp Cloudflare wrote, in whichever of its ways it wrote it.</summary>
    /// <param name="value">The string that arrived: null, blank, ISO 8601, or a date and a time with a space between them.</param>
    /// <returns>The instant it names at offset zero, or null when there is nothing to read or it cannot be read.</returns>
    public static DateTimeOffset? Read(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();

        // No offset in the string means UTC, and an offset in it is honoured and then adjusted to UTC.
        var styles = DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal;

        if (DateTime.TryParseExact(text, Formats, CultureInfo.InvariantCulture, styles, out var exact))
        {
            return new DateTimeOffset(exact);
        }

        // ISO 8601 with its offset, and anything else close enough for the general parser.
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, styles, out var parsed)
            ? parsed
            : null;
    }
}
