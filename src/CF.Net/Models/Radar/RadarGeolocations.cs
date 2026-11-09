using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Http;
using CFNet.Serialization;

namespace CFNet.Models.Radar;

/// <summary>What kind of place a radar geolocation is.</summary>
[JsonConverter(typeof(CloudflareEnumConverter<RadarGeolocationType>))]
public enum RadarGeolocationType
{
    /// <summary>A value this version of the library has not met. Reading one is tolerated; sending one is not.</summary>
    Unknown = 0,

    /// <summary>A continent.</summary>
    [CloudflareValue("CONTINENT")]
    Continent = 1,

    /// <summary>A country.</summary>
    [CloudflareValue("COUNTRY")]
    Country = 2,

    /// <summary>A first-level administrative division - a state, province or equivalent.</summary>
    [CloudflareValue("ADM1")]
    AdministrativeDivision = 3,
}

/// <summary>
/// One place in Cloudflare's radar geolocation tree: a continent, a country or a first-level administrative
/// division, with the place above it.
/// </summary>
/// <remarks>
/// <see cref="Parent"/> is the same type, because Cloudflare publishes the tree by nesting it: an administrative
/// division carries its country, which carries its continent. A walk up the tree is therefore a property chain
/// rather than a second call, and the nesting ends where <see cref="Parent"/> is <see langword="null"/>.
/// Cloudflare requires <c>latitude</c>, <c>longitude</c>, <c>name</c>, <c>parent</c> and <c>type</c> and
/// publishes <c>code</c> and <c>locale</c> as optional; every property here stays nullable anyway, because a
/// read that throws over one absent field is worse than one that reports it as missing. One field arrives that
/// the published schema does not mention at all - <see cref="GeoId"/> - and it is modelled because it is the id
/// Cloudflare reads a single place by.
/// </remarks>
public sealed class RadarGeolocation
{
    /// <summary>The place's code - an alpha-2 country code, or the identifier of a division.</summary>
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    /// <summary>
    /// The geolocation id - <c>geoId</c>, for example <c>3175395</c> for Italy, or <c>3183560</c> for Abruzzo.
    /// Pass this one to <c>IRadarService.GetGeolocationAsync</c>.
    /// </summary>
    /// <remarks>
    /// Cloudflare's published schema for the geolocation tree does not list this field, yet every place in a
    /// live answer carries it, and so does each of its parents. It is the only way to read one place by id:
    /// asking by <see cref="Code"/> is answered <c>400 [2001]</c>, a refusal that reads like a bad credential
    /// or a wrong path but is neither. A field the schema omits that answers are full of is worth modelling, so
    /// it does not end up in <see cref="AdditionalData"/> where a caller has to know to look.
    /// </remarks>
    [JsonPropertyName("geoId")]
    public string? GeoId { get; set; }

    /// <summary>Latitude, as the string Cloudflare publishes it.</summary>
    [JsonPropertyName("latitude")]
    public string? Latitude { get; set; }

    /// <summary>Longitude, as the string Cloudflare publishes it.</summary>
    [JsonPropertyName("longitude")]
    public string? Longitude { get; set; }

    /// <summary>The locale this place's name is given in, when Cloudflare reports one.</summary>
    [JsonPropertyName("locale")]
    public string? Locale { get; set; }

    /// <summary>The place's name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>The place above this one in the tree, if there is one.</summary>
    [JsonPropertyName("parent")]
    public RadarGeolocation? Parent { get; set; }

    /// <summary>What kind of place this is.</summary>
    [JsonPropertyName("type")]
    public RadarGeolocationType? Type { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>
    /// The places above this one, nearest first, ending with the continent. Useful for turning a division back
    /// into the country and continent a caller usually wants to group by.
    /// </summary>
    [JsonIgnore]
    public IEnumerable<RadarGeolocation> Ancestors
    {
        get
        {
            var current = Parent;
            var guard = 0;

            // A published tree cannot cycle, but an answer that did would hang a caller's foreach.
            while (current is not null && guard++ < 32)
            {
                yield return current;
                current = current.Parent;
            }
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var kind = Type is { } type && type != RadarGeolocationType.Unknown
            ? CloudflareEnumConverter<RadarGeolocationType>.ToWire(type)
            : "an unpublished kind";
        return $"{Code ?? "?"} {Name ?? "?"} ({kind})";
    }
}

/// <summary>The <c>result</c> of a geolocation list.</summary>
public sealed class RadarGeolocationListResult
{
    /// <summary>The geolocations.</summary>
    [JsonPropertyName("geolocations")]
    public List<RadarGeolocation>? Geolocations { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>The <c>result</c> of a single-geolocation read.</summary>
public sealed class RadarGeolocationResult
{
    /// <summary>The geolocation.</summary>
    [JsonPropertyName("geolocation")]
    public RadarGeolocation? Geolocation { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>
/// Query parameters for listing radar geolocations, which page with <c>offset</c> and <c>limit</c> and answer
/// no <c>result_info</c>.
/// </summary>
public sealed class RadarGeolocationListParameters : ICloudflareQueryParameters
{
    /// <summary>Restricts the list to one place, by its code.</summary>
    public string? Location { get; set; }

    /// <summary>How many geolocations to return.</summary>
    public int? Limit { get; set; }

    /// <summary>How many geolocations to skip first.</summary>
    public int? Offset { get; set; }

    /// <inheritdoc />
    public void Apply(QueryStringBuilder query)
    {
        query.AddIfNotEmpty("location", Location);
        query.Add("limit", Limit);
        query.Add("offset", Offset);
    }

    /// <summary>A copy of these parameters that starts further along the list.</summary>
    /// <param name="offset">How many geolocations to skip.</param>
    /// <returns>The new parameters.</returns>
    public RadarGeolocationListParameters WithOffset(int offset) => new()
    {
        Location = Location,
        Limit = Limit,
        Offset = offset,
    };
}
