using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Exceptions;
using CFNet.Http;
using CFNet.Serialization;

namespace CFNet.Models.Radar;

/// <summary>Which of Cloudflare's own data a radar annotation was drawn from.</summary>
/// <remarks>
/// A filter, not a description: the <c>dataSource</c> field an annotation answers with is left as the string
/// Cloudflare sends, because turning an answer into <see cref="RadarAnnotationDataSource.Unknown"/> would
/// throw away the value a caller asked for. Only what a caller sends is typed.
/// </remarks>
[JsonConverter(typeof(CloudflareEnumConverter<RadarAnnotationDataSource>))]
public enum RadarAnnotationDataSource
{
    /// <summary>A value this version of the library has not met. Sending one is not possible.</summary>
    Unknown = 0,

    /// <summary>Every source at once.</summary>
    [CloudflareValue("ALL")]
    All = 1,

    /// <summary>AI bots.</summary>
    [CloudflareValue("AI_BOTS")]
    AiBots = 2,

    /// <summary>AI Gateway.</summary>
    [CloudflareValue("AI_GATEWAY")]
    AiGateway = 3,

    /// <summary>BGP routing data.</summary>
    [CloudflareValue("BGP")]
    Bgp = 4,

    /// <summary>Bot traffic.</summary>
    [CloudflareValue("BOTS")]
    Bots = 5,

    /// <summary>Connection anomalies.</summary>
    [CloudflareValue("CONNECTION_ANOMALY")]
    ConnectionAnomaly = 6,

    /// <summary>Certificate transparency.</summary>
    [CloudflareValue("CT")]
    CertificateTransparency = 7,

    /// <summary>DNS.</summary>
    [CloudflareValue("DNS")]
    Dns = 8,

    /// <summary>DNS query magnitude.</summary>
    [CloudflareValue("DNS_MAGNITUDE")]
    DnsMagnitude = 9,

    /// <summary>AS112 DNS traffic.</summary>
    [CloudflareValue("DNS_AS112")]
    DnsAs112 = 10,

    /// <summary>Denial of service.</summary>
    [CloudflareValue("DOS")]
    DenialOfService = 11,

    /// <summary>Email routing.</summary>
    [CloudflareValue("EMAIL_ROUTING")]
    EmailRouting = 12,

    /// <summary>Email security.</summary>
    [CloudflareValue("EMAIL_SECURITY")]
    EmailSecurity = 13,

    /// <summary>The firewall.</summary>
    [CloudflareValue("FW")]
    Firewall = 14,

    /// <summary>Firewall permission groups.</summary>
    [CloudflareValue("FW_PG")]
    FirewallPermissionGroups = 15,

    /// <summary>HTTP.</summary>
    [CloudflareValue("HTTP")]
    Http = 16,

    /// <summary>HTTP, counted as control data.</summary>
    [CloudflareValue("HTTP_CONTROL")]
    HttpControl = 17,

    /// <summary>HTTP crawler referrers.</summary>
    [CloudflareValue("HTTP_CRAWLER_REFERER")]
    HttpCrawlerReferer = 18,

    /// <summary>HTTP origins.</summary>
    [CloudflareValue("HTTP_ORIGINS")]
    HttpOrigins = 19,

    /// <summary>Internet quality.</summary>
    [CloudflareValue("IQI")]
    InternetQuality = 20,

    /// <summary>Leaked credentials.</summary>
    [CloudflareValue("LEAKED_CREDENTIALS")]
    LeakedCredentials = 21,

    /// <summary>Network flow data.</summary>
    [CloudflareValue("NET")]
    Network = 22,

    /// <summary>robots.txt files.</summary>
    [CloudflareValue("ROBOTS_TXT")]
    RobotsText = 23,

    /// <summary>Speed observations.</summary>
    [CloudflareValue("SPEED")]
    Speed = 24,

    /// <summary>Workers AI.</summary>
    [CloudflareValue("WORKERS_AI")]
    WorkersAi = 25,
}

/// <summary>What kind of event a radar annotation records.</summary>
[JsonConverter(typeof(CloudflareEnumConverter<RadarAnnotationEventType>))]
public enum RadarAnnotationEventType
{
    /// <summary>A value this version of the library has not met. Sending one is not possible.</summary>
    Unknown = 0,

    /// <summary>A legacy alias for <see cref="General"/>.</summary>
    [CloudflareValue("EVENT")]
    Event = 1,

    /// <summary>A general annotation.</summary>
    [CloudflareValue("GENERAL")]
    General = 2,

    /// <summary>An outage.</summary>
    [CloudflareValue("OUTAGE")]
    Outage = 3,

    /// <summary>A partial projection.</summary>
    [CloudflareValue("PARTIAL_PROJECTION")]
    PartialProjection = 4,

    /// <summary>A pipeline event.</summary>
    [CloudflareValue("PIPELINE")]
    Pipeline = 5,

    /// <summary>A traffic anomaly.</summary>
    [CloudflareValue("TRAFFIC_ANOMALY")]
    TrafficAnomaly = 6,
}

/// <summary>What caused an outage a radar annotation records.</summary>
[JsonConverter(typeof(CloudflareEnumConverter<RadarAnnotationOutageCause>))]
public enum RadarAnnotationOutageCause
{
    /// <summary>A value this version of the library has not met. Sending one is not possible.</summary>
    Unknown = 0,

    /// <summary>Blocking.</summary>
    [CloudflareValue("BLOCKING")]
    Blocking = 1,

    /// <summary>A cable cut.</summary>
    [CloudflareValue("CABLE_CUT")]
    CableCut = 2,

    /// <summary>A cyberattack.</summary>
    [CloudflareValue("CYBERATTACK")]
    Cyberattack = 3,

    /// <summary>A DNS problem.</summary>
    [CloudflareValue("DNS")]
    Dns = 4,

    /// <summary>Fire.</summary>
    [CloudflareValue("FIRE")]
    Fire = 5,

    /// <summary>A government-directed shutdown.</summary>
    [CloudflareValue("GOVERNMENT_DIRECTED")]
    GovernmentDirected = 6,

    /// <summary>Maintenance.</summary>
    [CloudflareValue("MAINTENANCE")]
    Maintenance = 7,

    /// <summary>A mechanical failure.</summary>
    [CloudflareValue("MECHANICAL")]
    Mechanical = 8,

    /// <summary>Military action.</summary>
    [CloudflareValue("MILITARY_ACTION")]
    MilitaryAction = 9,

    /// <summary>A misconfiguration.</summary>
    [CloudflareValue("MISCONFIGURATION")]
    Misconfiguration = 10,

    /// <summary>A natural disaster.</summary>
    [CloudflareValue("NATURAL_DISASTER")]
    NaturalDisaster = 11,

    /// <summary>A network problem.</summary>
    [CloudflareValue("NETWORK_PROBLEM")]
    NetworkProblem = 12,

    /// <summary>A power outage.</summary>
    [CloudflareValue("POWER_OUTAGE")]
    PowerOutage = 13,

    /// <summary>A software fault.</summary>
    [CloudflareValue("SOFTWARE")]
    Software = 14,

    /// <summary>A technical problem.</summary>
    [CloudflareValue("TECHNICAL_PROBLEM")]
    TechnicalProblem = 15,

    /// <summary>An unknown cause.</summary>
    [CloudflareValue("UNKNOWN")]
    UnknownCause = 16,

    /// <summary>Weather.</summary>
    [CloudflareValue("WEATHER")]
    Weather = 17,
}

/// <summary>How wide an outage a radar annotation records was.</summary>
[JsonConverter(typeof(CloudflareEnumConverter<RadarAnnotationOutageType>))]
public enum RadarAnnotationOutageType
{
    /// <summary>A value this version of the library has not met. Sending one is not possible.</summary>
    Unknown = 0,

    /// <summary>Nationwide.</summary>
    [CloudflareValue("NATIONWIDE")]
    Nationwide = 1,

    /// <summary>Regional.</summary>
    [CloudflareValue("REGIONAL")]
    Regional = 2,

    /// <summary>One network.</summary>
    [CloudflareValue("NETWORK")]
    Network = 3,

    /// <summary>One platform.</summary>
    [CloudflareValue("PLATFORM")]
    Platform = 4,
}

/// <summary>
/// One radar annotation: something Cloudflare noticed in its own data - an outage, a traffic anomaly, a
/// pipeline event - and what it was about.
/// </summary>
/// <remarks>
/// An annotation points at the things it describes rather than nesting them: <see cref="Asns"/> carries numbers
/// while <see cref="AsnsDetails"/> carries the same ASNs with names, and <see cref="Locations"/> carries alpha-2
/// codes while <see cref="LocationsDetails"/> and <see cref="GeoIds"/> carry the places behind them. Both halves
/// are modelled, because a caller grouping annotations by country wants the details and a caller drawing them
/// on a chart wants the codes.
/// <para>
/// <see cref="DataSource"/>, <see cref="EventType"/> and the two outage fields are the strings Cloudflare
/// answers with, not the enums its filters are typed with: an answer carrying a value this library has not met
/// is worth reading, and turning it into <c>Unknown</c> would drop what Cloudflare said.
/// </para>
/// </remarks>
public sealed class RadarAnnotation
{
    /// <summary>The annotation's id.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>The ASNs this annotation is about.</summary>
    [JsonPropertyName("asns")]
    public List<long>? Asns { get; set; }

    /// <summary>The same ASNs, with names and locations.</summary>
    [JsonPropertyName("asnsDetails")]
    public List<RadarAnnotationAsn>? AsnsDetails { get; set; }

    /// <summary>Which of Cloudflare's data this came from, as Cloudflare spells it.</summary>
    [JsonPropertyName("dataSource")]
    public string? DataSource { get; set; }

    /// <summary>What the annotation says, when it says anything.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>When the event ended, when it has.</summary>
    [JsonPropertyName("endDate")]
    public string? EndDate { get; set; }

    /// <summary>The entities this annotation is linked to.</summary>
    [JsonPropertyName("entities")]
    public List<RadarAnnotationEntity>? Entities { get; set; }

    /// <summary>What kind of event this is, as Cloudflare spells it.</summary>
    [JsonPropertyName("eventType")]
    public string? EventType { get; set; }

    /// <summary>The GeoNames ids of the places this is about.</summary>
    [JsonPropertyName("geoIds")]
    public List<string>? GeoIds { get; set; }

    /// <summary>A link Cloudflare published beside the annotation, when there is one.</summary>
    [JsonPropertyName("linkedUrl")]
    public string? LinkedUrl { get; set; }

    /// <summary>The alpha-2 location codes this is about.</summary>
    [JsonPropertyName("locations")]
    public List<string>? Locations { get; set; }

    /// <summary>
    /// The same locations, with names. Cloudflare publishes the items as nullable, so an entry can be
    /// <see langword="null"/> - a location it could not resolve - and the list keeps its place for it.
    /// </summary>
    [JsonPropertyName("locationsDetails")]
    public List<RadarAnnotationLocation?>? LocationsDetails { get; set; }

    /// <summary>The origins this is about.</summary>
    [JsonPropertyName("origins")]
    public List<string>? Origins { get; set; }

    /// <summary>The same origins, with names.</summary>
    [JsonPropertyName("originsDetails")]
    public List<RadarAnnotationOrigin>? OriginsDetails { get; set; }

    /// <summary>What went wrong and how widely, when this annotation records an outage.</summary>
    [JsonPropertyName("outage")]
    public RadarAnnotationOutage? Outage { get; set; }

    /// <summary>How far the annotation reaches, when Cloudflare reports a scope.</summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    /// <summary>When the event started.</summary>
    [JsonPropertyName("startDate")]
    public string? StartDate { get; set; }

    /// <summary>The tags Cloudflare attached.</summary>
    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"{Id ?? "?"} {EventType ?? "?"}" +
        (StartDate is { Length: > 0 } start ? $" from {start}" : string.Empty) +
        (Outage is { } outage ? $" ({outage})" : string.Empty);
}

/// <summary>An ASN an annotation is about, with the name and place Cloudflare has for it.</summary>
public sealed class RadarAnnotationAsn
{
    /// <summary>
    /// The ASN, as the string Cloudflare sends it here - the same annotation carries the number in
    /// <see cref="RadarAnnotation.Asns"/>, and one list is a number while the other is a string.
    /// </summary>
    [JsonPropertyName("asn")]
    public string? Asn { get; set; }

    /// <summary>Where the ASN is registered, when Cloudflare knows.</summary>
    [JsonPropertyName("location")]
    public RadarAnnotationAsnLocation? Location { get; set; }

    /// <summary>The ASN's name, when Cloudflare has one.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>
    /// The ASN as a number, or <see langword="null"/> when Cloudflare sent something that is not one.
    /// </summary>
    [JsonIgnore]
    public long? AsnNumber =>
        long.TryParse(Asn, NumberStyles.Integer, CultureInfo.InvariantCulture, out var asn) ? asn : null;

    /// <inheritdoc />
    public override string ToString() => $"AS{Asn ?? "?"} {Name ?? "?"}";
}

/// <summary>The place an annotation's ASN is registered in.</summary>
public sealed class RadarAnnotationAsnLocation
{
    /// <summary>The alpha-2 location code.</summary>
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    /// <summary>The location's name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"{Code ?? "?"} {Name ?? "?"}";
}

/// <summary>One entity an annotation is linked to - a bot, a certificate authority, a vertical.</summary>
public sealed class RadarAnnotationEntity
{
    /// <summary>Where the entity is, when that means anything for its kind.</summary>
    [JsonPropertyName("entityLocation")]
    public string? EntityLocation { get; set; }

    /// <summary>The entity's name, when Cloudflare has one.</summary>
    [JsonPropertyName("entityName")]
    public string? EntityName { get; set; }

    /// <summary>What kind of entity this is.</summary>
    [JsonPropertyName("entityType")]
    public string? EntityType { get; set; }

    /// <summary>The entity's value - a bot's identifier, a CA's name, a vertical's code.</summary>
    [JsonPropertyName("entityValue")]
    public string? EntityValue { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"{EntityType ?? "?"}: {EntityValue ?? "?"}";
}

/// <summary>A location an annotation is about, by code and name.</summary>
public sealed class RadarAnnotationLocation
{
    /// <summary>The alpha-2 location code.</summary>
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    /// <summary>The location's name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"{Code ?? "?"} {Name ?? "?"}";
}

/// <summary>An origin an annotation is about.</summary>
public sealed class RadarAnnotationOrigin
{
    /// <summary>The origin's name, when Cloudflare has one.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>The origin itself.</summary>
    [JsonPropertyName("origin")]
    public string? Origin { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => Origin ?? "?";
}

/// <summary>What went wrong in an outage, and how widely.</summary>
public sealed class RadarAnnotationOutage
{
    /// <summary>The cause, as Cloudflare spells it.</summary>
    [JsonPropertyName("outageCause")]
    public string? OutageCause { get; set; }

    /// <summary>How wide the outage was, as Cloudflare spells it.</summary>
    [JsonPropertyName("outageType")]
    public string? OutageType { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"{OutageCause ?? "?"} {OutageType ?? "?"}";
}

/// <summary>The <c>result</c> of an annotation list: Cloudflare wraps the array in a named property.</summary>
public sealed class RadarAnnotationListResult
{
    /// <summary>The annotations.</summary>
    [JsonPropertyName("annotations")]
    public List<RadarAnnotation>? Annotations { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>
/// One location an outage annotation reached, as <c>GET /radar/annotations/outages/locations</c> answers it.
/// </summary>
/// <remarks>
/// This is a different shape from <see cref="RadarAnnotation"/> although the endpoint answers it under the same
/// <c>annotations</c> property: an outage's locations are counted per client country rather than described.
/// </remarks>
public sealed class RadarOutageLocation
{
    /// <summary>The client country's alpha-2 code.</summary>
    [JsonPropertyName("clientCountryAlpha2")]
    public string? ClientCountryAlpha2 { get; set; }

    /// <summary>The client country's name.</summary>
    [JsonPropertyName("clientCountryName")]
    public string? ClientCountryName { get; set; }

    /// <summary>What Cloudflare measured for that country, as the string it sends.</summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"{ClientCountryAlpha2 ?? "?"} {ClientCountryName ?? "?"}: {Value ?? "?"}";
}

/// <summary>The <c>result</c> of an outage-locations list.</summary>
public sealed class RadarOutageLocationsResult
{
    /// <summary>The locations, under the same property name the annotation lists use.</summary>
    [JsonPropertyName("annotations")]
    public List<RadarOutageLocation>? Annotations { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>
/// Query parameters for listing radar annotations, which page with <c>offset</c> and <c>limit</c> and answer no
/// <c>result_info</c>.
/// </summary>
/// <remarks>
/// The filters are typed where Cloudflare publishes a closed list and left as strings where it does not: the
/// data source, event type and both outage fields are enums, while a bot, a certificate authority, an origin, a
/// log, a top-level domain and a free-text query are whatever a caller has. The <c>tags</c> filter publishes
/// around a hundred values that Cloudflare adds to as it adds measurements, so it stays a list of strings -
/// typing it would turn every new tag into a library release.
/// <para>
/// <c>dateRange</c> is Cloudflare's own relative grammar rather than a date: <c>7d</c>, <c>52w</c>, and the
/// same with <c>control</c> appended to ask for the equivalent previous period. It is mutually exclusive with
/// <see cref="DateStart"/> and <see cref="DateEnd"/>, which Cloudflare wants as timestamps and which are sent
/// in the <c>yyyy-MM-ddTHH:mm:ssZ</c> form the rest of the API uses.
/// </para>
/// <para>
/// The <c>format</c> parameter is deliberately absent. It can ask for CSV, which would replace the JSON envelope
/// this transport reads with a body it cannot, and an empty result is a worse answer than no option at all.
/// </para>
/// </remarks>
public sealed class RadarAnnotationListParameters : ICloudflareQueryParameters
{
    /// <summary>Filters to annotations about one ASN.</summary>
    public long? Asn { get; set; }

    /// <summary>Filters to annotations about one bot.</summary>
    public string? Bot { get; set; }

    /// <summary>Filters to annotations about one certificate authority.</summary>
    public string? Ca { get; set; }

    /// <summary>Filters to annotations drawn from one of Cloudflare's data sources.</summary>
    public RadarAnnotationDataSource? DataSource { get; set; }

    /// <summary>The end of the date range, inclusive. Send it with <see cref="DateStart"/>.</summary>
    public DateTimeOffset? DateEnd { get; set; }

    /// <summary>
    /// A relative date range ending now, in Cloudflare's own grammar - <c>7d</c>, <c>12w</c>, or either with
    /// <c>control</c> appended for the equivalent previous period. Mutually exclusive with the two dates.
    /// </summary>
    public string? DateRange { get; set; }

    /// <summary>The start of the date range, inclusive. Send it with <see cref="DateEnd"/>.</summary>
    public DateTimeOffset? DateStart { get; set; }

    /// <summary>Filters to one kind of event.</summary>
    public RadarAnnotationEventType? EventType { get; set; }

    /// <summary>Filters to one geolocation, by its GeoNames id.</summary>
    public string? GeoId { get; set; }

    /// <summary>How many annotations to return.</summary>
    public int? Limit { get; set; }

    /// <summary>Filters to one alpha-2 location code.</summary>
    public string? Location { get; set; }

    /// <summary>Filters to one certificate transparency log.</summary>
    public string? Log { get; set; }

    /// <summary>How many annotations to skip first.</summary>
    public int? Offset { get; set; }

    /// <summary>Filters to one origin.</summary>
    public string? Origin { get; set; }

    /// <summary>Filters to outages with one cause.</summary>
    public RadarAnnotationOutageCause? OutageCause { get; set; }

    /// <summary>Filters to outages of one width.</summary>
    public RadarAnnotationOutageType? OutageType { get; set; }

    /// <summary>A free-text match on the description, the id, or a linked location, ASN or origin.</summary>
    public string? Query { get; set; }

    /// <summary>
    /// Filters to annotations carrying at least one of these tags. Cloudflare publishes around a hundred of
    /// them and adds more as it adds measurements, so they are strings rather than an enum.
    /// </summary>
    public List<string>? Tags { get; set; }

    /// <summary>Filters to one top-level domain.</summary>
    public string? Tld { get; set; }

    /// <inheritdoc />
    public void Apply(QueryStringBuilder query)
    {
        // Every name here is camelCase, which is how radar publishes its query parameters and unlike the rest
        // of the v4 API.
        query.Add("asn", Asn);
        query.AddIfNotEmpty("bot", Bot);
        query.AddIfNotEmpty("ca", Ca);
        query.AddIfNotEmpty("dataSource", Wire(DataSource, "a radar data source"));
        query.Add("dateEnd", DateEnd);
        query.AddIfNotEmpty("dateRange", DateRange);
        query.Add("dateStart", DateStart);
        query.AddIfNotEmpty("eventType", Wire(EventType, "a radar event type"));
        query.AddIfNotEmpty("geoId", GeoId);
        query.Add("limit", Limit);
        query.AddIfNotEmpty("location", Location);
        query.AddIfNotEmpty("log", Log);
        query.Add("offset", Offset);
        query.AddIfNotEmpty("origin", Origin);
        query.AddIfNotEmpty("outageCause", Wire(OutageCause, "a radar outage cause"));
        query.AddIfNotEmpty("outageType", Wire(OutageType, "a radar outage type"));
        query.AddIfNotEmpty("query", Query);
        query.AddMany("tags", Tags);
        query.AddIfNotEmpty("tld", Tld);
    }

    /// <summary>A copy of these parameters that starts further along the list.</summary>
    /// <param name="offset">How many annotations to skip.</param>
    /// <returns>The new parameters.</returns>
    public RadarAnnotationListParameters WithOffset(int offset) => new()
    {
        Asn = Asn,
        Bot = Bot,
        Ca = Ca,
        DataSource = DataSource,
        DateEnd = DateEnd,
        DateRange = DateRange,
        DateStart = DateStart,
        EventType = EventType,
        GeoId = GeoId,
        Limit = Limit,
        Location = Location,
        Log = Log,
        Offset = offset,
        Origin = Origin,
        OutageCause = OutageCause,
        OutageType = OutageType,
        Query = Query,
        Tags = Tags,
        Tld = Tld,
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

/// <summary>
/// Query parameters for <c>GET /radar/annotations/outages/locations</c>, which takes a date range and a limit
/// and nothing else - no offset, so it cannot be walked the way the annotation list can.
/// </summary>
public sealed class RadarOutageLocationsParameters : ICloudflareQueryParameters
{
    /// <summary>The end of the date range, inclusive. Send it with <see cref="DateStart"/>.</summary>
    public DateTimeOffset? DateEnd { get; set; }

    /// <summary>A relative date range ending now, in Cloudflare's grammar - <c>7d</c>, <c>12w</c>.</summary>
    public string? DateRange { get; set; }

    /// <summary>The start of the date range, inclusive. Send it with <see cref="DateEnd"/>.</summary>
    public DateTimeOffset? DateStart { get; set; }

    /// <summary>How many locations to return.</summary>
    public int? Limit { get; set; }

    /// <inheritdoc />
    public void Apply(QueryStringBuilder query)
    {
        query.Add("dateEnd", DateEnd);
        query.AddIfNotEmpty("dateRange", DateRange);
        query.Add("dateStart", DateStart);
        query.Add("limit", Limit);
    }
}
