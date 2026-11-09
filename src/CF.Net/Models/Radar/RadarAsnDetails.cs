using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Exceptions;
using CFNet.Http;
using CFNet.Serialization;

namespace CFNet.Models.Radar;

/// <summary>What Cloudflare ranks the botnet threat feed by.</summary>
[JsonConverter(typeof(CloudflareEnumConverter<RadarBotnetMetric>))]
public enum RadarBotnetMetric
{
    /// <summary>A value this version of the library has not met. Sending one is not possible.</summary>
    Unknown = 0,

    /// <summary>How many offenses Cloudflare saw.</summary>
    [CloudflareValue("OFFENSE_COUNT")]
    OffenseCount = 1,

    /// <summary>How many offending addresses Cloudflare saw.</summary>
    [CloudflareValue("NUMBER_OF_OFFENDING_IPS")]
    NumberOfOffendingIps = 2,
}

/// <summary>Which way to sort a ranked radar list.</summary>
[JsonConverter(typeof(CloudflareEnumConverter<RadarSortOrder>))]
public enum RadarSortOrder
{
    /// <summary>A value this version of the library has not met. Sending one is not possible.</summary>
    Unknown = 0,

    /// <summary>Ascending.</summary>
    [CloudflareValue("ASC")]
    Ascending = 1,

    /// <summary>Descending.</summary>
    [CloudflareValue("DESC")]
    Descending = 2,
}

/// <summary>
/// One AS-SET: a named group of autonomous systems that a routing registry publishes, and how big Cloudflare
/// measures it to be.
/// </summary>
/// <remarks>
/// Every field here is snake_case - <c>as_members_count</c>, <c>irr_sources</c>, <c>asn_cone_size</c> - while
/// the annotation and dataset models beside it are camelCase. Radar publishes both spellings in the same
/// resource, so each property is pinned to the one its endpoint uses rather than inferred from a convention.
/// <para>
/// Nine of these fields are in no published schema at all. Cloudflare's reference for this endpoint lists the
/// three counts, the cone size, the IRR sources, the name and the three optional ASNs; a live answer also
/// carried <c>as_members</c>, <c>as_set_members</c>, <c>descr</c>, <c>mnt_by</c>, <c>admin_c</c>,
/// <c>tech_c</c>, <c>org</c>, <c>inference_matches</c> and <c>is_cf_transit</c> - the IRR record behind the
/// set, and how Cloudflare worked out which ASN it belongs to. They are modelled because they are the
/// interesting half of the answer.
/// </para>
/// <para>
/// A field this library has seen filled in is typed as it arrived. One it has only ever seen empty or null is
/// read as the JSON it arrives as, because a guessed type that turns out wrong does not cost one field - it
/// fails the whole read.
/// </para>
/// </remarks>
public sealed class RadarAsSet
{
    /// <summary>How many ASNs the set has as members.</summary>
    [JsonPropertyName("as_members_count")]
    public long? AsMembersCount { get; set; }

    /// <summary>How many members are themselves AS-SETs.</summary>
    [JsonPropertyName("as_set_members_count")]
    public long? AsSetMembersCount { get; set; }

    /// <summary>How many upstreams the set's members have.</summary>
    [JsonPropertyName("as_set_upstreams_count")]
    public long? AsSetUpstreamsCount { get; set; }

    /// <summary>How many ASNs can be reached through this one, by Cloudflare's measurement.</summary>
    [JsonPropertyName("asn_cone_size")]
    public long? AsnConeSize { get; set; }

    /// <summary>The Internet Routing Registry sources the set was read from.</summary>
    [JsonPropertyName("irr_sources")]
    public List<string>? IrrSources { get; set; }

    /// <summary>The set's name, such as <c>AS-CLOUDFLARE</c>.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>The ASN Cloudflare inferred the set belongs to, when it inferred one.</summary>
    [JsonPropertyName("hierarchical_asn")]
    public long? HierarchicalAsn { get; set; }

    /// <summary>The ASN Cloudflare inferred the set belongs to, by its other route.</summary>
    [JsonPropertyName("inferred_asn")]
    public long? InferredAsn { get; set; }

    /// <summary>The ASN PeeringDB has for the set, when PeeringDB has one.</summary>
    [JsonPropertyName("peeringdb_asn")]
    public long? PeeringdbAsn { get; set; }

    /// <summary>The set's description in the routing registry.</summary>
    [JsonPropertyName("descr")]
    public string? Description { get; set; }

    /// <summary>
    /// The ASNs the set lists as members. Every answer this library has seen carried it empty, so the item
    /// type is neither published nor observed and is read as the JSON it arrives as.
    /// </summary>
    [JsonPropertyName("as_members")]
    public List<JsonElement>? AsMembers { get; set; }

    /// <summary>
    /// The AS-SETs the set lists as members. Empty in every answer seen, like <see cref="AsMembers"/>.
    /// </summary>
    [JsonPropertyName("as_set_members")]
    public List<JsonElement>? AsSetMembers { get; set; }

    /// <summary>The maintainers the registry record names.</summary>
    [JsonPropertyName("mnt_by")]
    public List<RadarAsSetMaintainer>? MaintainedBy { get; set; }

    /// <summary>The administrative contact handles, such as <c>DUMY-RIPE</c>.</summary>
    [JsonPropertyName("admin_c")]
    public List<string>? AdminContacts { get; set; }

    /// <summary>The technical contact handles.</summary>
    [JsonPropertyName("tech_c")]
    public List<string>? TechnicalContacts { get; set; }

    /// <summary>
    /// The organisation behind the set. Null in every answer this library has seen, and its type is not
    /// published, so it is read as the JSON it arrives as rather than guessed at.
    /// </summary>
    [JsonPropertyName("org")]
    public JsonElement? Organisation { get; set; }

    /// <summary>How Cloudflare worked out which ASN this set belongs to.</summary>
    [JsonPropertyName("inference_matches")]
    public List<RadarAsSetInference>? InferenceMatches { get; set; }

    /// <summary>
    /// Whether Cloudflare provides transit for this set, as the JSON it arrived in.
    /// </summary>
    /// <remarks>
    /// Cloudflare answers this field but does not publish it, and no answer this library has seen showed its
    /// value - only its name. Typing it as a <see langword="bool"/> is a guess whose failure is not local: a
    /// value that will not read as one fails the whole answer, every AS-SET in it included. So it is read as
    /// JSON and offered as <see cref="IsCloudflareTransit"/> beside it, which answers
    /// <see langword="null"/> for anything that is not a boolean in some spelling.
    /// </remarks>
    [JsonPropertyName("is_cf_transit")]
    public JsonElement? IsCloudflareTransitRaw { get; set; }

    /// <summary>
    /// Whether Cloudflare provides transit for this set, read out of <see cref="IsCloudflareTransitRaw"/> -
    /// <see langword="null"/> when the field is absent or arrived as something that is not a boolean.
    /// </summary>
    [JsonIgnore]
    public bool? IsCloudflareTransit
    {
        get
        {
            if (IsCloudflareTransitRaw is not { } raw)
            {
                return null;
            }

            return raw.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => raw.TryGetInt64(out var value) ? value != 0 : null,
                JsonValueKind.String => bool.TryParse(raw.GetString(), out var parsed) ? parsed : null,
                _ => null,
            };
        }
    }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"{Name ?? "?"}: " +
        $"{AsMembersCount?.ToString(CultureInfo.InvariantCulture) ?? "?"} member(s), " +
        $"cone {AsnConeSize?.ToString(CultureInfo.InvariantCulture) ?? "?"}";
}

/// <summary>One maintainer of an AS-SET, as the Internet Routing Registry records it.</summary>
public sealed class RadarAsSetMaintainer
{
    /// <summary>The maintainer's handle, such as <c>AS6830-MNT</c>.</summary>
    [JsonPropertyName("mntner")]
    public string? Maintainer { get; set; }

    /// <summary>The registry this record came from, in the spelling Cloudflare normalised it to.</summary>
    [JsonPropertyName("irr")]
    public string? Irr { get; set; }

    /// <summary>The registry's own name for itself, such as <c>RIPE</c>.</summary>
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    /// <summary>The record's description, when it has one.</summary>
    [JsonPropertyName("descr")]
    public string? Description { get; set; }

    /// <summary>The administrative contact handles.</summary>
    [JsonPropertyName("admin_c")]
    public List<string>? AdminContacts { get; set; }

    /// <summary>The technical contact handles.</summary>
    [JsonPropertyName("tech_c")]
    public List<string>? TechnicalContacts { get; set; }

    /// <summary>The maintainer handles this record names.</summary>
    [JsonPropertyName("mnt_by")]
    public List<string>? MaintainedBy { get; set; }

    /// <summary>When the record was created, as Cloudflare sends it.</summary>
    [JsonPropertyName("created")]
    public string? Created { get; set; }

    /// <summary>When the record was last modified, as Cloudflare sends it.</summary>
    [JsonPropertyName("last_modified")]
    public string? LastModified { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"{Maintainer ?? "?"}" + (Source is { Length: > 0 } source ? $" ({source})" : string.Empty);
}

/// <summary>One route by which Cloudflare inferred the ASN an AS-SET belongs to.</summary>
public sealed class RadarAsSetInference
{
    /// <summary>The ASN the inference landed on.</summary>
    [JsonPropertyName("asn")]
    public long? Asn { get; set; }

    /// <summary>How it was inferred - <c>peeringdb</c>, <c>hierarchical</c>.</summary>
    [JsonPropertyName("method")]
    public string? Method { get; set; }

    /// <summary>
    /// Whatever Cloudflare adds by way of detail. Null in every answer this library has seen, and its type is
    /// not published, so it is read as the JSON it arrives as.
    /// </summary>
    [JsonPropertyName("detail")]
    public JsonElement? Detail { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"AS{Asn?.ToString(CultureInfo.InvariantCulture) ?? "?"} by {Method ?? "?"}";
}

/// <summary>
/// The <c>result</c> of an AS-SET read: the sets an ASN belongs to, and the paths from each of them up to its
/// upstreams.
/// </summary>
/// <remarks>
/// <see cref="Paths"/> is a list of lists because Cloudflare publishes it that way: each inner list is one path,
/// nearest hop first, and the paths are "from the AS-SET that include the given AS to its upstreams
/// recursively". Nothing here flattens them, because the shape is the information.
/// </remarks>
public sealed class RadarAsnAsSet
{
    /// <summary>The AS-SETs.</summary>
    [JsonPropertyName("as_sets")]
    public List<RadarAsSet>? AsSets { get; set; }

    /// <summary>The paths from those sets to their upstreams, one list of ASNs per path.</summary>
    [JsonPropertyName("paths")]
    public List<List<string>>? Paths { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"{AsSets?.Count.ToString(CultureInfo.InvariantCulture) ?? "0"} AS-SET(s), " +
        $"{Paths?.Count.ToString(CultureInfo.InvariantCulture) ?? "0"} path(s)";
}

/// <summary>When Cloudflare measured an ASN's relationships, and how many peers it counted.</summary>
public sealed class RadarAsnRelationshipMeta
{
    /// <summary>When the underlying data is from.</summary>
    [JsonPropertyName("data_time")]
    public string? DataTime { get; set; }

    /// <summary>When Cloudflare answered.</summary>
    [JsonPropertyName("query_time")]
    public string? QueryTime { get; set; }

    /// <summary>How many peers the ASN has.</summary>
    [JsonPropertyName("total_peers")]
    public long? TotalPeers { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"{TotalPeers?.ToString(CultureInfo.InvariantCulture) ?? "?"} peer(s) as of {DataTime ?? "?"}";
}

/// <summary>One relationship between two autonomous systems, as Cloudflare sees it from its own traffic.</summary>
public sealed class RadarAsnRelationship
{
    /// <summary>The first ASN.</summary>
    [JsonPropertyName("asn1")]
    public long? Asn1 { get; set; }

    /// <summary>Where the first ASN is registered.</summary>
    [JsonPropertyName("asn1_country")]
    public string? Asn1Country { get; set; }

    /// <summary>The first ASN's name.</summary>
    [JsonPropertyName("asn1_name")]
    public string? Asn1Name { get; set; }

    /// <summary>The second ASN.</summary>
    [JsonPropertyName("asn2")]
    public long? Asn2 { get; set; }

    /// <summary>Where the second ASN is registered.</summary>
    [JsonPropertyName("asn2_country")]
    public string? Asn2Country { get; set; }

    /// <summary>The second ASN's name.</summary>
    [JsonPropertyName("asn2_name")]
    public string? Asn2Name { get; set; }

    /// <summary>
    /// How the two are related - upstream, downstream, peer - in the word Cloudflare uses. It publishes this as
    /// a string with no list of values, so it stays one rather than becoming an enum that would turn an
    /// unpublished relationship into <c>Unknown</c>.
    /// </summary>
    [JsonPropertyName("rel")]
    public string? Rel { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"AS{Asn1?.ToString(CultureInfo.InvariantCulture) ?? "?"} - " +
        $"AS{Asn2?.ToString(CultureInfo.InvariantCulture) ?? "?"}" +
        (Rel is { Length: > 0 } rel ? $" ({rel})" : string.Empty);
}

/// <summary>The <c>result</c> of an ASN relationship read: the relationships, and when they were measured.</summary>
public sealed class RadarAsnRelationships
{
    /// <summary>When Cloudflare measured these, and how many peers it counted.</summary>
    [JsonPropertyName("meta")]
    public RadarAsnRelationshipMeta? Meta { get; set; }

    /// <summary>The relationships.</summary>
    [JsonPropertyName("rels")]
    public List<RadarAsnRelationship>? Rels { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"{Rels?.Count.ToString(CultureInfo.InvariantCulture) ?? "0"} relationship(s)" +
        (Meta is { } meta ? $", {meta}" : string.Empty);
}

/// <summary>One ASN's place in Cloudflare's botnet threat feed ranking.</summary>
public sealed class RadarBotnetThreatFeedEntry
{
    /// <summary>The ASN.</summary>
    [JsonPropertyName("asn")]
    public long? Asn { get; set; }

    /// <summary>Where the ASN is registered.</summary>
    [JsonPropertyName("country")]
    public string? Country { get; set; }

    /// <summary>The ASN's name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Where the ASN ranks, best first.</summary>
    [JsonPropertyName("rank")]
    public long? Rank { get; set; }

    /// <summary>
    /// How far the rank moved against the comparison date, when one was asked for. Cloudflare publishes this
    /// field as optional, and answers it only when <c>compareDateRange</c> was sent.
    /// </summary>
    [JsonPropertyName("rankChange")]
    public long? RankChange { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"#{Rank?.ToString(CultureInfo.InvariantCulture) ?? "?"} " +
        $"AS{Asn?.ToString(CultureInfo.InvariantCulture) ?? "?"} {Name ?? "?"}" +
        (RankChange is { } change
            ? $" ({(change > 0 ? "+" : string.Empty)}{change.ToString(CultureInfo.InvariantCulture)})"
            : string.Empty);
}

/// <summary>Which day a botnet threat feed ranking is for.</summary>
/// <remarks>
/// Cloudflare notes that the date is when the report was generated, from the day before's data: the
/// <c>2026-02-23</c> entry describes <c>2026-02-22</c>.
/// </remarks>
public sealed class RadarBotnetThreatFeedMeta
{
    /// <summary>The date the report was generated for.</summary>
    [JsonPropertyName("date")]
    public string? Date { get; set; }

    /// <summary>How many ASNs the ranking covers in total.</summary>
    [JsonPropertyName("total")]
    public long? Total { get; set; }

    /// <summary>The date the ranks were compared against, when a comparison was asked for.</summary>
    [JsonPropertyName("compareDate")]
    public string? CompareDate { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"{Date ?? "?"}: {Total?.ToString(CultureInfo.InvariantCulture) ?? "?"} AS(s)" +
        (CompareDate is { Length: > 0 } compare ? $" against {compare}" : string.Empty);
}

/// <summary>
/// The <c>result</c> of a botnet threat feed read: the ranked ASNs, and the day they were ranked for.
/// </summary>
/// <remarks>
/// This is not returned as a <see cref="CloudflarePage{T}"/> even though the endpoint pages with
/// <c>offset</c> and <c>limit</c>, because Cloudflare answers a <c>meta</c> object beside the list and its
/// <c>total</c> is the only total this endpoint offers. Wrapping the list alone would drop it.
/// </remarks>
public sealed class RadarBotnetThreatFeed
{
    /// <summary>The ranked ASNs.</summary>
    [JsonPropertyName("ases")]
    public List<RadarBotnetThreatFeedEntry>? Ases { get; set; }

    /// <summary>Which day this ranking is for, and how many ASNs it covers.</summary>
    [JsonPropertyName("meta")]
    public RadarBotnetThreatFeedMeta? Meta { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"{Ases?.Count.ToString(CultureInfo.InvariantCulture) ?? "0"} ranked AS(s)" +
        (Meta is { } meta ? $", {meta}" : string.Empty);
}

/// <summary>
/// Query parameters for the botnet threat feed ranking, which pages with <c>offset</c> and <c>limit</c> and
/// answers no <c>result_info</c>.
/// </summary>
/// <remarks>
/// <see cref="Asns"/> is sent as the comma-separated list Cloudflare describes, and a leading <c>-</c> excludes
/// an ASN rather than including one: <c>-174,3356</c> leaves AS174 out and keeps AS3356 in. The entries are
/// strings rather than numbers for that reason, because <c>-174</c> is not an ASN.
/// </remarks>
public sealed class RadarBotnetThreatFeedParameters : ICloudflareQueryParameters
{
    /// <summary>
    /// Filters to these ASNs, each as Cloudflare spells it - a bare number to include one, or a number with a
    /// leading <c>-</c> to exclude it.
    /// </summary>
    public List<string>? Asns { get; set; }

    /// <summary>
    /// A relative range to compare the ranks against - <c>1d</c>, <c>7d</c>, <c>30d</c> - which is what makes
    /// Cloudflare answer a <see cref="RadarBotnetThreatFeedEntry.RankChange"/>.
    /// </summary>
    public string? CompareDateRange { get; set; }

    /// <summary>Which day's ranking to read. Without it, Cloudflare answers the most recent one it has.</summary>
    public DateTimeOffset? Date { get; set; }

    /// <summary>How many ASNs to return.</summary>
    public int? Limit { get; set; }

    /// <summary>Filters to ASNs registered in one alpha-2 location code.</summary>
    public string? Location { get; set; }

    /// <summary>What to rank by.</summary>
    public RadarBotnetMetric? Metric { get; set; }

    /// <summary>How many ASNs to skip first.</summary>
    public int? Offset { get; set; }

    /// <summary>Which way to sort.</summary>
    public RadarSortOrder? SortOrder { get; set; }

    /// <summary>Builds a filter for one or more ASNs, excluding any that are negated.</summary>
    /// <param name="asns">
    /// The ASNs to ask for. A negative number excludes that ASN, which is the grammar Cloudflare documents and
    /// the reason these are numbers rather than strings: <c>-174</c> leaves AS174 out.
    /// </param>
    /// <returns>Parameters ready to apply.</returns>
    public static RadarBotnetThreatFeedParameters OfAsns(params long[] asns)
    {
        ArgumentNullException.ThrowIfNull(asns);

        return new RadarBotnetThreatFeedParameters
        {
            Asns = asns
                .Select(asn => asn.ToString(CultureInfo.InvariantCulture))
                .ToList(),
        };
    }

    /// <inheritdoc />
    public void Apply(QueryStringBuilder query)
    {
        if (Asns is { Count: > 0 } asns)
        {
            query.Add("asn", string.Join(",", asns));
        }

        query.AddIfNotEmpty("compareDateRange", CompareDateRange);

        // Published as a date, so it is sent without a time part.
        if (Date is { } date)
        {
            query.Add("date", date, "yyyy-MM-dd");
        }

        query.Add("limit", Limit);
        query.AddIfNotEmpty("location", Location);
        query.AddIfNotEmpty("metric", Wire(Metric, "a botnet threat feed metric"));
        query.Add("offset", Offset);
        query.AddIfNotEmpty("sortOrder", Wire(SortOrder, "a sort order"));
    }

    /// <summary>A copy of these parameters that starts further along the ranking.</summary>
    /// <param name="offset">How many ASNs to skip.</param>
    /// <returns>The new parameters.</returns>
    public RadarBotnetThreatFeedParameters WithOffset(int offset) => new()
    {
        Asns = Asns,
        CompareDateRange = CompareDateRange,
        Date = Date,
        Limit = Limit,
        Location = Location,
        Metric = Metric,
        Offset = offset,
        SortOrder = SortOrder,
    };

    private static string? Wire<TEnum>(TEnum? value, string what)
        where TEnum : struct, Enum
    {
        if (value is not { } present)
        {
            return null;
        }

        if (!CloudflareEnumConverter<TEnum>.TryToWire(present, out var wire))
        {
            throw new CloudflareConfigurationException(
                $"'{present}' is not {what} this version of the library knows about, so it cannot be sent as a " +
                "radar filter.");
        }

        return wire;
    }
}
