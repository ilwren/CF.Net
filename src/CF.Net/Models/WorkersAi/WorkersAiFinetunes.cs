using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Http;
using CFNet.Serialization;

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
/// integration run rather than guessed at here. Two more of the published shape's claims did not survive a
/// live answer either: <c>public</c> arrived as the number <c>1</c> rather than as a boolean, and the two
/// timestamps arrived without an offset and with a space in them, so both are read tolerantly - see
/// <see cref="Public"/> and <see cref="CreatedAt"/>.
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

    /// <summary>
    /// Whether the job is one of Cloudflare's public ones. Not every list answers it, and the published shape
    /// and a live answer disagree about its kind: the shape says <c>boolean</c> and
    /// <c>GET …/ai/finetunes/public</c> answered <c>"public": 1</c>, so this reads either.
    /// </summary>
    [JsonPropertyName("public")]
    [JsonConverter(typeof(TolerantBooleanConverter))]
    public bool? Public { get; set; }

    /// <summary>
    /// When the job was created, exactly as Cloudflare wrote it. A live answer wrote
    /// <c>2024-05-08 02:23:55.897</c> - a space where ISO 8601 puts a <c>T</c>, and no offset - where this
    /// endpoint's published example writes <c>2019-12-27T18:11:19.117Z</c>. Both arrive from one endpoint, so
    /// neither is parsed on the way in and the string is what is kept; <see cref="CreatedAtUtc"/> reads it.
    /// </summary>
    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    /// <summary>
    /// When the job was created, as an instant at offset zero, when the string Cloudflare wrote can be read as
    /// one. A timestamp with no offset in it is read as UTC; see <see cref="CloudflareTimestamps"/>.
    /// </summary>
    [JsonIgnore]
    public DateTimeOffset? CreatedAtUtc => CloudflareTimestamps.Read(CreatedAt);

    /// <summary>When the job last changed, exactly as Cloudflare wrote it. See <see cref="CreatedAt"/>.</summary>
    [JsonPropertyName("modified_at")]
    public string? ModifiedAt { get; set; }

    /// <summary>
    /// When the job last changed, as an instant at offset zero, when the string can be read as one.
    /// </summary>
    [JsonIgnore]
    public DateTimeOffset? ModifiedAtUtc => CloudflareTimestamps.Read(ModifiedAt);

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
