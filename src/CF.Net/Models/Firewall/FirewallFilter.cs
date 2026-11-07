using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Http;

namespace CFNet.Models.Firewall;

/// <summary>
/// A firewall filter: one expression in the ruleset language, saved on its own so that more than one legacy
/// firewall rule could point at the same thing.
/// </summary>
/// <remarks>
/// <para>
/// <b>This API is deprecated.</b> Cloudflare deprecated the filters API together with the legacy firewall rules
/// API that consumed it, in favour of the ruleset engine — a ruleset rule carries its expression inline instead
/// of referring to a saved filter, which is why nothing new should be built here. It is still served and still
/// answered, and an account with older rules has filters lying around that it can only read or remove through
/// this path, so it is wrapped rather than skipped. <c>client.Rulesets</c> is the place to build something new.
/// </para>
/// <para>
/// The published limits are an <c>id</c> of exactly 32 characters, a <c>description</c> of at most 500 and a
/// <c>ref</c> of at most 50. The <c>expression</c> is the ordinary ruleset language —
/// <c>http.request.uri.path contains "/api"</c> — and this library passes it through untouched: the grammar is
/// large, it is Cloudflare's to validate, and a wrong one comes back as a refusal that names the problem.
/// </para>
/// </remarks>
public sealed class FirewallFilter
{
    /// <summary>The filter's identifier, published as exactly 32 characters.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>An informative summary of what the filter is for, at most 500 characters.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>The expression itself, in the ruleset language.</summary>
    [JsonPropertyName("expression")]
    public string? Expression { get; set; }

    /// <summary>Whether the filter is paused, which is Cloudflare's word for "saved but not applied".</summary>
    [JsonPropertyName("paused")]
    public bool? Paused { get; set; }

    /// <summary>
    /// A short reference tag, at most 50 characters, whose only job is to let related filters be selected
    /// together. The wire name is <c>ref</c>, which is a keyword here, so the property is <c>Ref</c>.
    /// </summary>
    [JsonPropertyName("ref")]
    public string? Ref { get; set; }

    /// <summary>Fields Cloudflare answers that this version of the library does not model.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Names the filter and quotes the expression, which is the only part that does anything.</summary>
    /// <returns>A one-line description of the filter.</returns>
    public override string ToString()
    {
        var name = Description is { Length: > 0 } description ? $" ({description})" : string.Empty;
        var state = Paused == true ? ", paused" : string.Empty;
        return $"filter {Id ?? "(no id)"}{name}: {Expression ?? "(no expression)"}{state}";
    }
}

/// <summary>
/// One filter to save. The same type serves all three writes this deprecated API has — a member of the array a
/// <c>POST</c> or a bulk <c>PUT</c> takes, and the body of a single <c>PUT</c> — because the three bodies differ
/// only in whether an <c>id</c> belongs in them: a single update carries its id in the path instead.
/// </summary>
public sealed class FirewallFilterRequest
{
    /// <summary>
    /// The filter to change, which a bulk <c>PUT</c> needs on every member and a single <c>PUT</c> or a
    /// <c>POST</c> must not carry, because Cloudflare assigns the id.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>An informative summary of what the filter is for, at most 500 characters.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>The expression, in the ruleset language. This is the part that does anything.</summary>
    [JsonPropertyName("expression")]
    public string? Expression { get; set; }

    /// <summary>Whether the filter is saved but not applied.</summary>
    [JsonPropertyName("paused")]
    public bool? Paused { get; set; }

    /// <summary>A short reference tag, at most 50 characters, for selecting related filters together.</summary>
    [JsonPropertyName("ref")]
    public string? Ref { get; set; }

    /// <summary>Builds a filter to create.</summary>
    /// <param name="expression">The expression, in the ruleset language.</param>
    /// <param name="description">An informative summary; none when null.</param>
    /// <returns>A request ready to send, with no id, because Cloudflare assigns one.</returns>
    public static FirewallFilterRequest For(string expression, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(expression);

        return new FirewallFilterRequest { Expression = expression, Description = description };
    }

    /// <summary>Builds a filter to change as part of a bulk <c>PUT</c>, which needs the id in the body.</summary>
    /// <param name="id">The filter's identifier.</param>
    /// <param name="expression">The expression it should carry.</param>
    /// <param name="description">An informative summary; none when null.</param>
    /// <returns>A request ready to send.</returns>
    public static FirewallFilterRequest WithId(string id, string expression, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(id);

        var request = For(expression, description);
        request.Id = id;
        return request;
    }

    /// <summary>Copies a filter that was read, so it can be changed and sent back.</summary>
    /// <param name="filter">The filter as Cloudflare answered it.</param>
    /// <returns>A request carrying the filter's id and every field it has.</returns>
    public static FirewallFilterRequest FromFilter(FirewallFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return new FirewallFilterRequest
        {
            Id = filter.Id,
            Description = filter.Description,
            Expression = filter.Expression,
            Paused = filter.Paused,
            Ref = filter.Ref,
        };
    }

    /// <summary>Sets whether the filter is saved but not applied, and returns the request.</summary>
    /// <param name="paused">True to save it without applying it.</param>
    /// <returns>The same request, so calls can be chained.</returns>
    public FirewallFilterRequest SetPaused(bool paused = true)
    {
        Paused = paused;
        return this;
    }

    /// <summary>Sets the short reference tag, and returns the request.</summary>
    /// <param name="reference">The tag, at most 50 characters.</param>
    /// <returns>The same request, so calls can be chained.</returns>
    public FirewallFilterRequest Tagged(string reference)
    {
        Ref = reference;
        return this;
    }

    /// <summary>Refuses a filter Cloudflare would refuse, naming everything wrong with it at once.</summary>
    /// <exception cref="CFNet.Exceptions.CloudflareConfigurationException">
    /// Thrown when there is no expression, or when the description or the reference tag is longer than the
    /// length Cloudflare publishes; its <c>Problems</c> lists every reason.
    /// </exception>
    public void EnsureValid()
    {
        var problems = Problems(requireId: false);

        if (problems.Count > 0)
        {
            throw new CFNet.Exceptions.CloudflareConfigurationException(problems);
        }
    }

    /// <summary>
    /// Refuses a member of a bulk <c>PUT</c> that Cloudflare would refuse: the same checks as
    /// <see cref="EnsureValid"/>, plus the id, because a bulk update says which filter to change in the body
    /// rather than in the path.
    /// </summary>
    /// <exception cref="CFNet.Exceptions.CloudflareConfigurationException">
    /// Thrown when there is no id, no expression, or a field longer than Cloudflare publishes; its
    /// <c>Problems</c> lists every reason.
    /// </exception>
    public void EnsureValidForBulkUpdate()
    {
        var problems = Problems(requireId: true);

        if (problems.Count > 0)
        {
            throw new CFNet.Exceptions.CloudflareConfigurationException(problems);
        }
    }

    private List<string> Problems(bool requireId)
    {
        var problems = new List<string>();

        if (requireId && string.IsNullOrWhiteSpace(Id))
        {
            problems.Add(
                "a bulk filter update says which filter to change in the body, so every member needs an id - " +
                "a single update carries it in the path instead and must not.");
        }

        if (string.IsNullOrWhiteSpace(Expression))
        {
            problems.Add(
                "a filter needs an expression: it is the only part that matches anything, and an empty one " +
                "would be a filter that Cloudflare has to refuse or that matches nothing at all.");
        }

        if (Description is { Length: > 500 })
        {
            problems.Add(
                $"a description of {Description.Length} characters is longer than the 500 Cloudflare publishes " +
                "for this field.");
        }

        if (Ref is { Length: > 50 })
        {
            problems.Add(
                $"a reference tag of {Ref.Length} characters is longer than the 50 Cloudflare publishes for " +
                "this field.");
        }

        return problems;
    }

    /// <summary>Names the filter this request would save.</summary>
    /// <returns>A one-line description of the request.</returns>
    public override string ToString()
    {
        var name = Description is { Length: > 0 } description ? $" ({description})" : string.Empty;
        var id = Id is { Length: > 0 } identifier ? $" {identifier}" : string.Empty;
        return $"filter request{id}{name}: {Expression ?? "(no expression)"}";
    }
}

/// <summary>
/// The body of the filters API's bulk delete, which is a <c>DELETE</c> that carries the identifiers to remove
/// rather than a query string of them.
/// </summary>
public sealed class FirewallFilterBulkDeleteRequest
{
    /// <summary>The identifiers of the filters to remove. The wire name is <c>id</c>, singular, for a list.</summary>
    [JsonPropertyName("id")]
    public List<string>? Ids { get; set; }

    /// <summary>Builds a bulk delete for a set of filter ids.</summary>
    /// <param name="ids">The identifiers to remove.</param>
    /// <returns>A request ready to send.</returns>
    public static FirewallFilterBulkDeleteRequest Of(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        return new FirewallFilterBulkDeleteRequest { Ids = ids.ToList() };
    }

    /// <summary>Refuses a bulk delete that would delete nothing, or that names a filter with no id.</summary>
    /// <exception cref="CFNet.Exceptions.CloudflareConfigurationException">
    /// Thrown when the list is empty or holds a blank id; its <c>Problems</c> lists every reason.
    /// </exception>
    public void EnsureValid()
    {
        var problems = new List<string>();

        if (Ids is not { Count: > 0 })
        {
            problems.Add(
                "a bulk delete needs at least one filter id: an empty list would spend a call and remove " +
                "nothing, and Cloudflare refuses it.");
        }
        else
        {
            foreach (var id in Ids)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    problems.Add("every id in a bulk delete has to name a filter, and one of them is blank.");
                }
            }
        }

        if (problems.Count > 0)
        {
            throw new CFNet.Exceptions.CloudflareConfigurationException(problems);
        }
    }

    /// <summary>Counts the filters this request would remove.</summary>
    /// <returns>A one-line description of the request.</returns>
    public override string ToString() =>
        $"bulk delete of {Ids?.Count ?? 0} filter(s)";
}

/// <summary>What a filter delete answers: the identifier of the filter that is gone.</summary>
/// <remarks>
/// The single delete answers one of these and the bulk delete answers a list of them, so both are read into the
/// same small type rather than into two that would say the same thing.
/// </remarks>
public sealed class FirewallFilterDeletion
{
    /// <summary>The identifier of the filter that was removed.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Fields Cloudflare answers that this version of the library does not model.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Names the filter that was removed.</summary>
    /// <returns>A one-line description of the deletion.</returns>
    public override string ToString() => $"deleted filter {Id ?? "(no id)"}";
}

/// <summary>
/// Query parameters for <c>GET /zones/{zone_id}/filters</c>, which pages by page and per_page and filters by
/// any of the fields a filter has.
/// </summary>
/// <remarks>
/// The filters here are exact matches on the fields, not searches: an <c>expression</c> filter names the whole
/// expression. Cloudflare publishes no <c>match</c> or <c>order</c> for this list, so this library invents
/// neither.
/// </remarks>
public sealed class FirewallFilterListParameters : CloudflarePagedParameters
{
    /// <summary>Only the filter with this identifier.</summary>
    public string? Id { get; set; }

    /// <summary>Only filters with this description.</summary>
    public string? Description { get; set; }

    /// <summary>Only filters with this expression.</summary>
    public string? Expression { get; set; }

    /// <summary>Only filters that are paused, or only ones that are not.</summary>
    public bool? Paused { get; set; }

    /// <summary>Only filters carrying this reference tag.</summary>
    public string? Ref { get; set; }

    /// <inheritdoc />
    public override void Apply(QueryStringBuilder query)
    {
        base.Apply(query);
        query.AddIfNotEmpty("id", Id);
        query.AddIfNotEmpty("description", Description);
        query.AddIfNotEmpty("expression", Expression);
        query.Add("paused", Paused);
        query.AddIfNotEmpty("ref", Ref);
    }
}
