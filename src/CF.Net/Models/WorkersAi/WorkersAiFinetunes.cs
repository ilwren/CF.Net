using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Http;

namespace CFNet.Models.WorkersAi;

/// <summary>
/// A fine-tuning job: a model an account is training on its own data, or one Cloudflare has published for
/// everyone.
/// </summary>
/// <remarks>
/// <para>
/// One type serves all three lists that answer fine-tunes - the account's own, Cloudflare's public ones, and
/// the answer to creating one - because they publish almost the same fields: <c>id</c>, <c>name</c>,
/// <c>model</c>, <c>description</c>, <c>created_at</c> and <c>modified_at</c>, with <c>public</c> on the two
/// that can be. A field an answer does not carry stays null rather than being invented.
/// </para>
/// <para>
/// The endpoint's own summary promises "status and metrics" for a fine-tuning job, and its published shape
/// carries neither, so whatever else arrives is preserved in <see cref="AdditionalData"/> and reported by the
/// integration run rather than guessed at here.
/// </para>
/// </remarks>
public sealed class WorkersAiFinetune
{
    /// <summary>The job's id, a UUID, which is what an asset upload addresses it by.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>The name the account gave the job.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>The model being fine-tuned, or the fine-tuned model's own name once it can be run.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>What the job is for, as the account worded it.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Whether the job is one of Cloudflare's public ones. Not every list answers it.</summary>
    [JsonPropertyName("public")]
    public bool? Public { get; set; }

    /// <summary>When the job was created.</summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>When the job last changed.</summary>
    [JsonPropertyName("modified_at")]
    public DateTimeOffset? ModifiedAt { get; set; }

    /// <summary>Captures any field Cloudflare adds, including the status and metrics its summary promises.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Name and model, on one line.</summary>
    /// <returns>The job described in one line.</returns>
    public override string ToString() => $"{Name} ({Model})";
}

/// <summary>
/// The filters for the public fine-tune list - and it has its own three, which are not the paging this
/// library's other lists use.
/// </summary>
/// <remarks>
/// <c>limit</c> and <c>offset</c> rather than <c>page</c> and <c>per_page</c>, and <b><c>orderBy</c> in
/// camelCase</b> where most of Cloudflare spells a query parameter in snake_case: the same endpoint that
/// answers <c>mimeType</c> in its to-markdown list. So this does not extend
/// <see cref="CloudflarePagedParameters"/>, whose names would go out wrong.
/// </remarks>
public sealed class WorkersAiPublicFinetuneListParameters : ICloudflareQueryParameters
{
    /// <summary>How many jobs to answer at most.</summary>
    public int? Limit { get; set; }

    /// <summary>How many jobs to skip, which is how this list is paged.</summary>
    public int? Offset { get; set; }

    /// <summary>What to order the jobs by, as the endpoint names the field.</summary>
    public string? OrderBy { get; set; }

    /// <inheritdoc />
    public void Apply(QueryStringBuilder query)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Add("limit", Limit);
        query.Add("offset", Offset);
        query.AddIfNotEmpty("orderBy", OrderBy);
    }
}

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
    /// <summary>The file's extension, without its dot, such as <c>pdf</c>.</summary>
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
