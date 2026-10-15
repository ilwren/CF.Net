using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Models.Dns;

/// <summary>
/// Body of <c>POST /zones/{zone_id}/dns_records/batch</c>: deletes, patches, puts and posts sent as one
/// call.
/// </summary>
/// <remarks>
/// <para>
/// Cloudflare runs the four groups in a fixed order no matter how they are arranged in the body -
/// <b>deletes, then patches, then puts, then posts</b> - inside a single database transaction. Propagation
/// is still not atomic: the distributed store behind it treats every record change as its own key-value
/// pair, so a resolver can see the batch part-way through.
/// </para>
/// <para>
/// A <c>patch</c> carries the record's id plus only the fields to change, a <c>put</c> is a full replacement
/// of the record with that id, and a <c>post</c> creates a new one. All three reuse
/// <see cref="DnsRecord"/>, which is why the id rules are checked in <see cref="EnsureValid"/> rather than
/// by the type system.
/// </para>
/// <para>
/// An empty batch is not a no-op that Cloudflare accepts: it answers <c>9207 Request body is invalid</c>,
/// observed live, which is what <see cref="EnsureValid"/> rejects up front.
/// </para>
/// </remarks>
public sealed class DnsRecordBatch
{
    /// <summary>Records to remove, by id.</summary>
    [JsonPropertyName("deletes")]
    public List<DnsRecordBatchDelete>? Deletes { get; set; }

    /// <summary>Records to change partially. Each one has to carry the id of the record it changes.</summary>
    [JsonPropertyName("patches")]
    public List<DnsRecord>? Patches { get; set; }

    /// <summary>Records to replace wholesale.</summary>
    /// <remarks>
    /// Cloudflare's schema builds these the same way it builds a patch - a record plus an <c>id</c> - so a
    /// put without an id has nothing to replace. That is an inference from the published variant pattern
    /// rather than a rule this library enforces, so a put is sent as written and Cloudflare's own error is
    /// what comes back if the id is missing.
    /// </remarks>
    [JsonPropertyName("puts")]
    public List<DnsRecord>? Puts { get; set; }

    /// <summary>Records to create.</summary>
    [JsonPropertyName("posts")]
    public List<DnsRecord>? Posts { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>How many changes this batch carries, across all four groups.</summary>
    [JsonIgnore]
    public int ChangeCount =>
        (Deletes?.Count ?? 0) + (Patches?.Count ?? 0) + (Puts?.Count ?? 0) + (Posts?.Count ?? 0);

    /// <summary>True when there is nothing to send, which Cloudflare refuses rather than ignores.</summary>
    [JsonIgnore]
    public bool IsEmpty => ChangeCount == 0;

    /// <summary>Adds records to create and returns this batch, so calls can be chained.</summary>
    /// <param name="records">Records to create.</param>
    public DnsRecordBatch WithPosts(params DnsRecord[] records)
    {
        Posts ??= new List<DnsRecord>();
        Posts.AddRange(records);
        return this;
    }

    /// <summary>Adds record ids to delete and returns this batch, so calls can be chained.</summary>
    /// <param name="recordIds">Ids of the records to remove.</param>
    public DnsRecordBatch WithDeletes(params string[] recordIds)
    {
        Deletes ??= new List<DnsRecordBatchDelete>();
        foreach (var id in recordIds)
        {
            Deletes.Add(new DnsRecordBatchDelete { Id = id });
        }

        return this;
    }

    /// <summary>
    /// Checks the two rules Cloudflare documents: the batch is not empty, and every delete and patch names
    /// the record it applies to.
    /// </summary>
    /// <exception cref="Exceptions.CloudflareConfigurationException">A rule is broken.</exception>
    public void EnsureValid()
    {
        var problems = new List<string>();

        if (IsEmpty)
        {
            problems.Add("A batch needs at least one change: Cloudflare answers an empty body with error 9207 'Request body is invalid' rather than doing nothing.");
        }

        if (Deletes is { } deletes)
        {
            for (var index = 0; index < deletes.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(deletes[index].Id))
                {
                    problems.Add($"deletes[{index}] has no id, so there is no record to remove.");
                }
            }
        }

        if (Patches is { } patches)
        {
            for (var index = 0; index < patches.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(patches[index].Id))
                {
                    problems.Add($"patches[{index}] has no id, and a patch changes an existing record rather than creating one.");
                }
            }
        }

        if (problems.Count > 0)
        {
            throw new Exceptions.CloudflareConfigurationException(problems);
        }
    }

    /// <inheritdoc />
    public override string ToString() =>
        IsEmpty
            ? "(empty batch)"
            : $"batch: {Deletes?.Count ?? 0} delete(s), {Patches?.Count ?? 0} patch(es), " +
              $"{Puts?.Count ?? 0} put(s), {Posts?.Count ?? 0} post(s)";
}

/// <summary>One entry of a batch's <c>deletes</c> array: the id of a record to remove.</summary>
public sealed class DnsRecordBatchDelete
{
    /// <summary>Id of the record to delete. Required.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => Id ?? "(no id)";
}

/// <summary>
/// The <c>result</c> of a successful <c>POST /zones/{zone_id}/dns_records/batch</c>: the same four groups
/// the request carried, each holding the records that group acted on.
/// </summary>
/// <remarks>
/// <para>
/// Cloudflare's reference documents the request body and not this response, so the shape here was read off
/// a live answer rather than inferred from the request. What came back, for a batch that created two TXT
/// records, was <c>{"deletes":null,"patches":null,"puts":null,"posts":[{…},{…}]}</c> - a group the request
/// did not use arrives as an explicit <c>null</c> rather than being left out, and a group it did use arrives
/// as an array of <b>complete</b> records, with the id Cloudflare assigned and the <c>created_on</c> and
/// <c>modified_on</c> stamps.
/// </para>
/// <para>
/// The same holds for deletes, which is worth knowing before relying on it: the answer to a delete batch
/// echoes the removed records in full - name, type, content, ttl and timestamps - not just their ids, so
/// this is the place to capture what a batch removed before it is gone.
/// </para>
/// </remarks>
public sealed class DnsRecordBatchResult
{
    /// <summary>The records a delete removed, in full. <c>null</c> when the batch deleted nothing.</summary>
    [JsonPropertyName("deletes")]
    public List<DnsRecord>? Deletes { get; set; }

    /// <summary>The records a patch changed, as they are after the change. <c>null</c> when unused.</summary>
    [JsonPropertyName("patches")]
    public List<DnsRecord>? Patches { get; set; }

    /// <summary>The records a put replaced, as they are after the replacement. <c>null</c> when unused.</summary>
    [JsonPropertyName("puts")]
    public List<DnsRecord>? Puts { get; set; }

    /// <summary>The records a post created, carrying the ids Cloudflare assigned. <c>null</c> when unused.</summary>
    [JsonPropertyName("posts")]
    public List<DnsRecord>? Posts { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>How many records this batch touched, across all four groups.</summary>
    [JsonIgnore]
    public int AffectedCount =>
        (Deletes?.Count ?? 0) + (Patches?.Count ?? 0) + (Puts?.Count ?? 0) + (Posts?.Count ?? 0);

    /// <summary>The ids of every record the batch touched, whichever group it came through.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> AffectedIds =>
        new[] { Deletes, Patches, Puts, Posts }
            .Where(group => group is not null)
            .SelectMany(group => group!)
            .Where(record => !string.IsNullOrWhiteSpace(record.Id))
            .Select(record => record.Id!)
            .ToList();

    /// <inheritdoc />
    public override string ToString() =>
        AffectedCount == 0
            ? "(no records affected)"
            : $"batch result: {Deletes?.Count ?? 0} deleted, {Patches?.Count ?? 0} patched, " +
              $"{Puts?.Count ?? 0} replaced, {Posts?.Count ?? 0} created";
}
