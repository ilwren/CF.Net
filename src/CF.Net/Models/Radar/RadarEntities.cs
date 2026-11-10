using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Exceptions;
using CFNet.Http;
using CFNet.Serialization;

namespace CFNet.Models.Radar;

/// <summary>
/// The continents Cloudflare's radar data is grouped by, as the entity endpoints spell them.
/// </summary>
[JsonConverter(typeof(CloudflareEnumConverter<RadarContinent>))]
public enum RadarContinent
{
    /// <summary>A value this version of the library has not met. Reading one is tolerated; sending one is not.</summary>
    Unknown = 0,

    /// <summary>Africa.</summary>
    [CloudflareValue("AF")]
    Africa = 1,

    /// <summary>Asia.</summary>
    [CloudflareValue("AS")]
    Asia = 2,

    /// <summary>Europe.</summary>
    [CloudflareValue("EU")]
    Europe = 3,

    /// <summary>North America.</summary>
    [CloudflareValue("NA")]
    NorthAmerica = 4,

    /// <summary>Oceania.</summary>
    [CloudflareValue("OC")]
    Oceania = 5,

    /// <summary>South America.</summary>
    [CloudflareValue("SA")]
    SouthAmerica = 6,
}

/// <summary>
/// What Cloudflare's radar entity endpoints answer for a location: an alpha-2 code, the continent, region and
/// subregion it sits in, and where it is.
/// </summary>
/// <remarks>
/// <c>GET /radar/entities/locations</c> answers a subset of this - it leaves out <c>confidenceLevel</c>, which
/// only the single-location read reports - so that property stays <see langword="null"/> for a list item rather
/// than meaning "no confidence".
/// </remarks>
public sealed class RadarLocation
{
    /// <summary>The alpha-2 location code, such as <c>GB</c>, which is what the rest of the radar API takes.</summary>
    [JsonPropertyName("alpha2")]
    public string? Alpha2 { get; set; }

    /// <summary>The continent code, such as <c>EU</c>.</summary>
    [JsonPropertyName("continent")]
    public string? Continent { get; set; }

    /// <summary>Latitude, as the string Cloudflare publishes it.</summary>
    [JsonPropertyName("latitude")]
    public string? Latitude { get; set; }

    /// <summary>Longitude, as the string Cloudflare publishes it.</summary>
    [JsonPropertyName("longitude")]
    public string? Longitude { get; set; }

    /// <summary>The location's name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>The region the location sits in, such as <c>Europe</c>.</summary>
    [JsonPropertyName("region")]
    public string? Region { get; set; }

    /// <summary>The subregion, such as <c>Northern Europe</c>.</summary>
    [JsonPropertyName("subregion")]
    public string? Subregion { get; set; }

    /// <summary>How confident Cloudflare is in this location's data. Only the single-location read reports it.</summary>
    [JsonPropertyName("confidenceLevel")]
    public int? ConfidenceLevel { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"{Alpha2 ?? "?"} {Name ?? "?"}" +
        (Subregion is { Length: > 0 } subregion ? $" ({subregion})" : string.Empty);
}

/// <summary>The <c>result</c> of a location list: Cloudflare wraps the array in a named property.</summary>
public sealed class RadarLocationListResult
{
    /// <summary>The locations.</summary>
    [JsonPropertyName("locations")]
    public List<RadarLocation>? Locations { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>The <c>result</c> of a single-location read.</summary>
public sealed class RadarLocationResult
{
    /// <summary>The location.</summary>
    [JsonPropertyName("location")]
    public RadarLocation? Location { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>
/// Query parameters for listing radar locations. The radar endpoints page with <c>offset</c> and <c>limit</c>
/// rather than the <c>page</c> and <c>per_page</c> the rest of the API uses, and they answer no
/// <c>result_info</c>, so there is nothing for a caller to read a total off - a page shorter than the limit it
/// asked for is the end.
/// </summary>
/// <remarks>
/// Cloudflare also publishes a <c>format</c> parameter that can ask for <c>CSV</c>. This library does not send
/// it: its transport reads the JSON envelope, and a CSV body would come back as an empty result rather than as
/// an error, which is the kind of silence worth not offering.
/// </remarks>
public sealed class RadarLocationListParameters : ICloudflareQueryParameters
{
    /// <summary>Restricts the list to one continent.</summary>
    public RadarContinent? Continent { get; set; }

    /// <summary>Restricts the list to one region, such as <c>Europe</c>.</summary>
    public string? Region { get; set; }

    /// <summary>Restricts the list to one subregion, such as <c>Northern Europe</c>.</summary>
    public string? Subregion { get; set; }

    /// <summary>Restricts the list to one alpha-2 location code.</summary>
    public string? Location { get; set; }

    /// <summary>How many locations to return.</summary>
    public int? Limit { get; set; }

    /// <summary>How many locations to skip first.</summary>
    public int? Offset { get; set; }

    /// <inheritdoc />
    public void Apply(QueryStringBuilder query)
    {
        if (Continent is { } continent)
        {
            if (!CloudflareEnumConverter<RadarContinent>.TryToWire(continent, out var wire))
            {
                throw new CloudflareConfigurationException(
                    $"'{continent}' is not a continent this version of the library knows about, so it cannot " +
                    "be sent as a radar filter.");
            }

            query.Add("continent", wire);
        }

        query.AddIfNotEmpty("region", Region);
        query.AddIfNotEmpty("subregion", Subregion);
        query.AddIfNotEmpty("location", Location);
        query.Add("limit", Limit);
        query.Add("offset", Offset);
    }

    /// <summary>A copy of these parameters that starts further along the list.</summary>
    /// <param name="offset">How many locations to skip.</param>
    /// <returns>The new parameters.</returns>
    public RadarLocationListParameters WithOffset(int offset) => new()
    {
        Continent = Continent,
        Region = Region,
        Subregion = Subregion,
        Location = Location,
        Limit = Limit,
        Offset = offset,
    };
}

/// <summary>What Cloudflare's radar answers for an autonomous system.</summary>
/// <remarks>
/// The list endpoint answers a subset of this: <c>confidenceLevel</c>, <c>source</c>, <c>related</c> and the
/// per-location breakdown of <c>estimatedUsers</c> arrive only on the single-ASN read and on the lookup by IP.
/// </remarks>
public sealed class RadarAsn
{
    /// <summary>The autonomous system number.</summary>
    /// <remarks>
    /// A <see cref="long"/> rather than an <see cref="int"/>: four-byte ASNs run past
    /// <see cref="int.MaxValue"/>, and the private range starts at 4200000000.
    /// </remarks>
    [JsonPropertyName("asn")]
    public long? Asn { get; set; }

    /// <summary>The alpha-2 code of the country the ASN is registered in.</summary>
    [JsonPropertyName("country")]
    public string? Country { get; set; }

    /// <summary>The name of that country.</summary>
    [JsonPropertyName("countryName")]
    public string? CountryName { get; set; }

    /// <summary>The ASN's name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Another name the ASN is known by, when Cloudflare has one.</summary>
    [JsonPropertyName("aka")]
    public string? Aka { get; set; }

    /// <summary>The organisation behind the ASN.</summary>
    [JsonPropertyName("orgName")]
    public string? OrgName { get; set; }

    /// <summary>The organisation's website.</summary>
    [JsonPropertyName("website")]
    public string? Website { get; set; }

    /// <summary>Where this data came from. Only the single-ASN read reports it.</summary>
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    /// <summary>How confident Cloudflare is in this ASN's data. Only the single-ASN read reports it.</summary>
    [JsonPropertyName("confidenceLevel")]
    public int? ConfidenceLevel { get; set; }

    /// <summary>Estimated users, in total and per location.</summary>
    [JsonPropertyName("estimatedUsers")]
    public RadarAsnEstimatedUsers? EstimatedUsers { get; set; }

    /// <summary>The ASNs related to this one. Only the single-ASN read reports them.</summary>
    [JsonPropertyName("related")]
    public List<RadarRelatedAsn>? Related { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"AS{Asn?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"} {Name ?? "?"}" +
        (Country is { Length: > 0 } country ? $" ({country})" : string.Empty);
}

/// <summary>How many users Cloudflare estimates reach an ASN, in total and by location.</summary>
public sealed class RadarAsnEstimatedUsers
{
    /// <summary>The total estimate.</summary>
    [JsonPropertyName("estimatedUsers")]
    public long? EstimatedUsers { get; set; }

    /// <summary>The estimate broken down by location. Only the single-ASN read reports it.</summary>
    [JsonPropertyName("locations")]
    public List<RadarAsnUsersByLocation>? Locations { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"{EstimatedUsers?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"} estimated user(s)" +
        (Locations is { Count: > 0 } locations ? $" across {locations.Count} location(s)" : string.Empty);
}

/// <summary>One location's share of an ASN's estimated users.</summary>
public sealed class RadarAsnUsersByLocation
{
    /// <summary>The alpha-2 location code.</summary>
    [JsonPropertyName("locationAlpha2")]
    public string? LocationAlpha2 { get; set; }

    /// <summary>The location's name.</summary>
    [JsonPropertyName("locationName")]
    public string? LocationName { get; set; }

    /// <summary>The estimate for this location.</summary>
    [JsonPropertyName("estimatedUsers")]
    public long? EstimatedUsers { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"{LocationAlpha2 ?? "?"} {LocationName ?? "?"}: " +
        $"{EstimatedUsers?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"} user(s)";
}

/// <summary>An ASN Cloudflare relates to another one.</summary>
public sealed class RadarRelatedAsn
{
    /// <summary>The related ASN's number.</summary>
    [JsonPropertyName("asn")]
    public long? Asn { get; set; }

    /// <summary>Its name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Another name it is known by, when Cloudflare has one.</summary>
    [JsonPropertyName("aka")]
    public string? Aka { get; set; }

    /// <summary>Its estimated users, when Cloudflare reports them here.</summary>
    [JsonPropertyName("estimatedUsers")]
    public long? EstimatedUsers { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"AS{Asn?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"} {Name ?? "?"}";
}

/// <summary>The <c>result</c> of an ASN list.</summary>
public sealed class RadarAsnListResult
{
    /// <summary>The autonomous systems.</summary>
    [JsonPropertyName("asns")]
    public List<RadarAsn>? Asns { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>The <c>result</c> of a single-ASN read, and of the lookup of the ASN behind an IP address.</summary>
public sealed class RadarAsnResult
{
    /// <summary>The autonomous system.</summary>
    [JsonPropertyName("asn")]
    public RadarAsn? Asn { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>What Cloudflare's radar answers about the entity behind an IP address.</summary>
/// <remarks>
/// Every field arrives as a string here, including the ASN - the same number the ASN endpoints answer as a
/// number. The properties are typed for what this endpoint sends rather than for what its neighbour sends.
/// </remarks>
public sealed class RadarIpEntity
{
    /// <summary>The address that was asked about.</summary>
    [JsonPropertyName("ip")]
    public string? Ip { get; set; }

    /// <summary>The IP version, as Cloudflare spells it.</summary>
    [JsonPropertyName("ipVersion")]
    public string? IpVersion { get; set; }

    /// <summary>The autonomous system number behind the address, as a string.</summary>
    [JsonPropertyName("asn")]
    public string? Asn { get; set; }

    /// <summary>That ASN's name.</summary>
    [JsonPropertyName("asnName")]
    public string? AsnName { get; set; }

    /// <summary>The organisation behind that ASN.</summary>
    [JsonPropertyName("asnOrgName")]
    public string? AsnOrgName { get; set; }

    /// <summary>The alpha-2 code of the country that ASN is registered in.</summary>
    [JsonPropertyName("asnLocation")]
    public string? AsnLocation { get; set; }

    /// <summary>The alpha-2 code of the location the address itself is in.</summary>
    [JsonPropertyName("location")]
    public string? Location { get; set; }

    /// <summary>That location's name.</summary>
    [JsonPropertyName("locationName")]
    public string? LocationName { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The ASN as a number, when the string Cloudflare sent is one.</summary>
    [JsonIgnore]
    public long? AsnNumber =>
        long.TryParse(Asn, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    /// <inheritdoc />
    public override string ToString() =>
        $"{Ip ?? "?"} in {LocationName ?? Location ?? "?"}: AS{Asn ?? "?"}" +
        (AsnName is { Length: > 0 } name ? $" {name}" : string.Empty);
}

/// <summary>The <c>result</c> of a lookup of the entity behind an IP address.</summary>
public sealed class RadarIpEntityResult
{
    /// <summary>The entity.</summary>
    [JsonPropertyName("ip")]
    public RadarIpEntity? Ip { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>What Cloudflare's radar ASN list can be ordered by.</summary>
[JsonConverter(typeof(CloudflareEnumConverter<RadarAsnOrderBy>))]
public enum RadarAsnOrderBy
{
    /// <summary>A value this version of the library has not met. Sending one is not possible.</summary>
    Unknown = 0,

    /// <summary>Order by ASN number.</summary>
    [CloudflareValue("ASN")]
    Asn = 1,

    /// <summary>Order by estimated users, which puts the biggest networks first.</summary>
    [CloudflareValue("POPULATION")]
    Population = 2,
}

/// <summary>
/// Query parameters for listing autonomous systems. Like the other radar endpoints this pages with
/// <c>offset</c> and <c>limit</c> and answers no <c>result_info</c>.
/// </summary>
public sealed class RadarAsnListParameters : ICloudflareQueryParameters
{
    /// <summary>
    /// Filters to one or more ASNs, as the comma-separated string Cloudflare asks for - <c>13335,15169</c>.
    /// </summary>
    public string? Asn { get; set; }

    /// <summary>Filters to ASNs registered in one alpha-2 location code.</summary>
    public string? Location { get; set; }

    /// <summary>How many ASNs to return.</summary>
    public int? Limit { get; set; }

    /// <summary>How many ASNs to skip first.</summary>
    public int? Offset { get; set; }

    /// <summary>What to order the list by.</summary>
    public RadarAsnOrderBy? OrderBy { get; set; }

    /// <summary>Builds a filter for one or more ASN numbers.</summary>
    /// <param name="asns">The ASNs to ask for.</param>
    /// <returns>Parameters ready to apply.</returns>
    public static RadarAsnListParameters OfAsns(params long[] asns)
    {
        ArgumentNullException.ThrowIfNull(asns);

        return new RadarAsnListParameters
        {
            Asn = string.Join(
                ",",
                asns.Select(asn => asn.ToString(System.Globalization.CultureInfo.InvariantCulture))),
        };
    }

    /// <inheritdoc />
    public void Apply(QueryStringBuilder query)
    {
        query.AddIfNotEmpty("asn", Asn);
        query.AddIfNotEmpty("location", Location);
        query.Add("limit", Limit);
        query.Add("offset", Offset);

        if (OrderBy is { } orderBy)
        {
            if (!CloudflareEnumConverter<RadarAsnOrderBy>.TryToWire(orderBy, out var wire))
            {
                throw new CloudflareConfigurationException(
                    $"'{orderBy}' is not an ordering this version of the library knows about, so it cannot be " +
                    "sent as a radar filter.");
            }

            // The radar endpoints take camelCase query parameters, unlike the rest of the v4 API.
            query.Add("orderBy", wire);
        }
    }

    /// <summary>A copy of these parameters that starts further along the list.</summary>
    /// <param name="offset">How many ASNs to skip.</param>
    /// <returns>The new parameters.</returns>
    public RadarAsnListParameters WithOffset(int offset) => new()
    {
        Asn = Asn,
        Location = Location,
        Limit = Limit,
        Offset = offset,
        OrderBy = OrderBy,
    };
}
