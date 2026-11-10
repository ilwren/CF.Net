using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Exceptions;
using CFNet.Http;
using CFNet.Serialization;

namespace CFNet.Models.Radar;

/// <summary>The kinds of thing a radar global search can be asked for, or told to leave out.</summary>
[JsonConverter(typeof(CloudflareEnumConverter<RadarSearchCategory>))]
public enum RadarSearchCategory
{
    /// <summary>A value this version of the library has not met. Sending one is not possible.</summary>
    Unknown = 0,

    /// <summary>First-level administrative divisions.</summary>
    [CloudflareValue("ADM1S")]
    AdministrativeDivisions = 1,

    /// <summary>Autonomous systems.</summary>
    [CloudflareValue("ASNS")]
    Asns = 2,

    /// <summary>Bots.</summary>
    [CloudflareValue("BOTS")]
    Bots = 3,

    /// <summary>Certificate authorities.</summary>
    [CloudflareValue("CERTIFICATE_AUTHORITIES")]
    CertificateAuthorities = 4,

    /// <summary>Certificate transparency logs.</summary>
    [CloudflareValue("CERTIFICATE_LOGS")]
    CertificateLogs = 5,

    /// <summary>Origins.</summary>
    [CloudflareValue("ORIGINS")]
    Origins = 6,

    /// <summary>Origin regions.</summary>
    [CloudflareValue("ORIGIN_REGIONS")]
    OriginRegions = 7,

    /// <summary>Industries.</summary>
    [CloudflareValue("INDUSTRIES")]
    Industries = 8,

    /// <summary>Locations.</summary>
    [CloudflareValue("LOCATIONS")]
    Locations = 9,

    /// <summary>Notebooks - the worked examples Cloudflare publishes beside radar's data.</summary>
    [CloudflareValue("NOTEBOOKS")]
    Notebooks = 10,

    /// <summary>Top-level domains.</summary>
    [CloudflareValue("TLDS")]
    TopLevelDomains = 11,

    /// <summary>Industry verticals.</summary>
    [CloudflareValue("VERTICALS")]
    Verticals = 12,
}

/// <summary>One hit from a radar global search.</summary>
/// <remarks>
/// <c>type</c> is the category the hit belongs to, as the plain string Cloudflare sends - the same spellings
/// <see cref="RadarSearchCategory"/> carries, but published here as a string rather than as an enum, so a
/// category this library has not met arrives as itself instead of as <c>Unknown</c>.
/// </remarks>
public sealed class RadarSearchHit
{
    /// <summary>The hit's code - an ASN, an alpha-2 location, a bot id, and so on, depending on the type.</summary>
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    /// <summary>The hit's name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>What kind of hit this is.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"{Type ?? "?"}: {Code ?? "?"} {Name ?? "?"}";
}

/// <summary>The <c>result</c> of a global search.</summary>
public sealed class RadarSearchResults
{
    /// <summary>The hits, in the order Cloudflare ranked them.</summary>
    [JsonPropertyName("search")]
    public List<RadarSearchHit>? Search { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>
/// Query parameters for a radar global search. <c>query</c> is the one Cloudflare requires; the rest narrow it.
/// </summary>
/// <remarks>
/// <see cref="Include"/> and <see cref="Exclude"/> are sent as repeated parameters - <c>include=ASNS</c> once per
/// category - which is how Cloudflare asks for a list in a query string. The parameter names here are camelCase
/// (<c>limitPerGroup</c>), because the radar endpoints take camelCase query parameters while the rest of the v4
/// API takes snake_case.
/// </remarks>
public sealed class RadarSearchParameters : ICloudflareQueryParameters
{
    /// <summary>The string to search for. Cloudflare requires it.</summary>
    public string? Query { get; set; }

    /// <summary>Categories to search, or every one when null or empty.</summary>
    public List<RadarSearchCategory>? Include { get; set; }

    /// <summary>Categories to leave out.</summary>
    public List<RadarSearchCategory>? Exclude { get; set; }

    /// <summary>How many hits to return in total.</summary>
    public int? Limit { get; set; }

    /// <summary>How many hits to return per category.</summary>
    public int? LimitPerGroup { get; set; }

    /// <summary>Builds a search for one string.</summary>
    /// <param name="query">The string to search for.</param>
    /// <returns>Parameters ready to apply.</returns>
    public static RadarSearchParameters For(string query) => new() { Query = query };

    /// <summary>
    /// Checks the search can be sent: Cloudflare requires the query, and a category this library has not met
    /// has no spelling to send.
    /// </summary>
    /// <exception cref="CloudflareConfigurationException">Thrown when the query is missing or a category is
    /// unpublished.</exception>
    public void EnsureValid()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(Query))
        {
            problems.Add("a radar search needs something to search for, and this one carries no query.");
        }

        foreach (var (where, categories) in new[] { ("include", Include), ("exclude", Exclude) })
        {
            foreach (var category in categories ?? new List<RadarSearchCategory>())
            {
                if (!CloudflareEnumConverter<RadarSearchCategory>.TryToWire(category, out _))
                {
                    problems.Add(
                        $"'{category}' is not a search category this version of the library knows about, so it " +
                        $"cannot be sent in the {where} list.");
                }
            }
        }

        if (problems.Count > 0)
        {
            throw new CloudflareConfigurationException(problems);
        }
    }

    /// <inheritdoc />
    public void Apply(QueryStringBuilder query)
    {
        query.AddIfNotEmpty("query", Query);
        query.AddMany("include", Wire(Include));
        query.AddMany("exclude", Wire(Exclude));
        query.Add("limit", Limit);
        query.Add("limitPerGroup", LimitPerGroup);
    }

    private static IEnumerable<string>? Wire(List<RadarSearchCategory>? categories) =>
        categories?.Select(category => CloudflareEnumConverter<RadarSearchCategory>.ToWire(category));
}
