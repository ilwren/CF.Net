using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Models.WorkersAi;

/// <summary>
/// One of the formats <c>POST …/ai/tomarkdown</c> accepts: a file extension and the MIME type that goes with
/// it.
/// </summary>
/// <remarks>
/// <c>mimeType</c> is camelCase on the wire, which nearly nothing else Cloudflare answers is, so it is named
/// explicitly rather than left to this library's snake_case policy.
/// </remarks>
public sealed class WorkersAiToMarkdownFormat
{
    /// <summary>
    /// The file's extension, as Cloudflare lists it - its own example writes <c>.html</c>, dot included, so
    /// this is the string that arrived rather than one with the dot taken off.
    /// </summary>
    [JsonPropertyName("extension")]
    public string? Extension { get; set; }

    /// <summary>The MIME type that goes with the extension, such as <c>application/pdf</c>.</summary>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    /// <summary>Captures any field Cloudflare adds.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Extension and MIME type, on one line.</summary>
    /// <returns>The format described in one line.</returns>
    public override string ToString() => $"{Extension} ({MimeType})";
}

/// <summary>One file's conversion to Markdown, as <c>POST …/ai/tomarkdown</c> answers it.</summary>
/// <remarks>
/// A conversion of several files answers one of these per file, and **a file that did not convert is not a
/// failed request**: the call answers 200 with that file's <see cref="Format"/> as <c>error</c>, its
/// <see cref="Error"/> saying why and no <see cref="Data"/> at all. <see cref="Succeeded"/> tells the two
/// apart, so a caller converting ten files reads nine and one reason.
/// </remarks>
public sealed class WorkersAiToMarkdownConversion
{
    /// <summary>The conversion's own id, which Cloudflare gives each file's answer.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>The file's name, as it went in.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>The MIME type the file was read as, camelCase on the wire as everywhere else in this pair.</summary>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    /// <summary>
    /// What came out of the file: <c>markdown</c> when it converted, and <b><c>error</c></b> when it did not.
    /// It is the output's format rather than the input's, which is the MIME type beside it.
    /// </summary>
    [JsonPropertyName("format")]
    public string? Format { get; set; }

    /// <summary>The Markdown, when the file converted.</summary>
    [JsonPropertyName("data")]
    public string? Data { get; set; }

    /// <summary>Why a file did not convert, when its <see cref="Format"/> is <c>error</c>.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>
    /// How many tokens the conversion cost, as the JSON Cloudflare sent - and its own two accounts of this
    /// endpoint disagree about the kind: the REST documentation's example answers the number <c>49</c> and the
    /// generated client types the field as a <b>string</b>. So it stays the JSON that arrived and
    /// <see cref="TokenCount"/> reads either.
    /// </summary>
    [JsonPropertyName("tokens")]
    public JsonElement? Tokens { get; set; }

    /// <summary>The tokens as a number, whichever JSON kind Cloudflare sent them as.</summary>
    [JsonIgnore]
    public long? TokenCount
    {
        get
        {
            if (Tokens is not { } tokens)
            {
                return null;
            }

            if (tokens.ValueKind == JsonValueKind.Number)
            {
                return tokens.TryGetInt64(out var count) ? count : null;
            }

            return tokens.ValueKind == JsonValueKind.String &&
                   long.TryParse(
                       tokens.GetString(),
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out var parsed)
                ? parsed
                : null;
        }
    }

    /// <summary>Whether this file converted, rather than answering 200 with an error of its own.</summary>
    [JsonIgnore]
    public bool Succeeded =>
        Data is not null &&
        !string.Equals(Format, "error", StringComparison.OrdinalIgnoreCase);

    /// <summary>Captures any field Cloudflare adds.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Name and what came of it, on one line.</summary>
    /// <returns>The conversion described in one line.</returns>
    public override string ToString() => Succeeded ? $"{Name} ({Format})" : $"{Name} (error: {Error})";
}

/// <summary>A file to convert to Markdown: a name, its bytes, and the type to send them as.</summary>
/// <remarks>
/// <para>
/// The conversion is a multipart upload, and Cloudflare works out what a file is from its name and the type it
/// arrives with, so both go out: the name as the part's filename and the type as the part's content type. The
/// formats the endpoint accepts are what <c>ListToMarkdownFormatsAsync</c> answers.
/// </para>
/// <para>
/// <see cref="OfText"/> and <see cref="OfHtml"/> are the two most common ways in - a caller with a string has
/// usually already decoded the file - and <see cref="OfBytes"/> takes anything else.
/// </para>
/// </remarks>
public sealed class WorkersAiToMarkdownFile
{
    /// <summary>The type a file with no better one is sent as.</summary>
    public const string DefaultContentType = "application/octet-stream";

    /// <summary>The type <see cref="OfText"/> sends its bytes as.</summary>
    public const string TextContentType = "text/plain";

    /// <summary>The type <see cref="OfHtml"/> sends its bytes as.</summary>
    public const string HtmlContentType = "text/html";

    /// <summary>Makes a file to convert.</summary>
    /// <param name="fileName">The file's name, whose extension is what tells Cloudflare what it is.</param>
    /// <param name="content">The file's bytes.</param>
    /// <param name="contentType">The type to send it as, or <see cref="DefaultContentType"/> when null.</param>
    public WorkersAiToMarkdownFile(string fileName, byte[] content, string? contentType = null)
    {
        FileName = fileName;
        Content = content;
        ContentType = string.IsNullOrWhiteSpace(contentType) ? DefaultContentType : contentType;
    }

    /// <summary>The file's name, whose extension says what it is.</summary>
    public string? FileName { get; }

    /// <summary>The file's bytes.</summary>
    public byte[]? Content { get; }

    /// <summary>The type the bytes go out as, which is never blank.</summary>
    public string ContentType { get; }

    /// <summary>A UTF-8 text file, sent as <see cref="TextContentType"/>.</summary>
    /// <param name="fileName">The file's name, such as <c>notes.txt</c>.</param>
    /// <param name="text">The text.</param>
    /// <returns>The file to convert.</returns>
    public static WorkersAiToMarkdownFile OfText(string fileName, string text) =>
        new(fileName, Encoding.UTF8.GetBytes(text ?? string.Empty), TextContentType);

    /// <summary>A UTF-8 HTML file, sent as <see cref="HtmlContentType"/>.</summary>
    /// <param name="fileName">The file's name, such as <c>page.html</c>.</param>
    /// <param name="html">The HTML.</param>
    /// <returns>The file to convert.</returns>
    public static WorkersAiToMarkdownFile OfHtml(string fileName, string html) =>
        new(fileName, Encoding.UTF8.GetBytes(html ?? string.Empty), HtmlContentType);

    /// <summary>A file whose bytes are already decoded.</summary>
    /// <param name="fileName">The file's name, whose extension says what it is.</param>
    /// <param name="content">The file's bytes.</param>
    /// <param name="contentType">The type to send it as, or <see cref="DefaultContentType"/> when null.</param>
    /// <returns>The file to convert.</returns>
    public static WorkersAiToMarkdownFile OfBytes(string fileName, byte[] content, string? contentType = null) =>
        new(fileName, content, contentType);

    /// <summary>Name and type, on one line.</summary>
    /// <returns>The file described in one line.</returns>
    public override string ToString() => $"{FileName} ({ContentType})";
}
