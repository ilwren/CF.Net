using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Exceptions;
using CFNet.Http;
using CFNet.Serialization;

namespace CFNet.Models.Radar;

/// <summary>What kind of radar dataset a listing is filtered to.</summary>
[JsonConverter(typeof(CloudflareEnumConverter<RadarDatasetType>))]
public enum RadarDatasetType
{
    /// <summary>A value this version of the library has not met. Sending one is not possible.</summary>
    Unknown = 0,

    /// <summary>A ranking bucket - one of the "top N" datasets Cloudflare publishes.</summary>
    [CloudflareValue("RANKING_BUCKET")]
    RankingBucket = 1,

    /// <summary>A report.</summary>
    [CloudflareValue("REPORT")]
    Report = 2,
}

/// <summary>What a dataset download should produce.</summary>
/// <remarks>
/// This is the one place in radar where a <c>format</c> is modelled, and the reason is what comes back: the
/// download answer is still the JSON envelope, carrying a <see cref="RadarDatasetDownload.Url"/> to a file in
/// the format asked for, so choosing CSV changes the file behind a URL rather than the body this transport has
/// to read. On the read endpoints the same parameter would replace the envelope itself with a CSV body, and
/// that would surface as an empty result rather than as an error - so it is not offered there.
/// </remarks>
[JsonConverter(typeof(CloudflareEnumConverter<RadarDatasetFormat>))]
public enum RadarDatasetFormat
{
    /// <summary>A value this version of the library has not met. Sending one is not possible.</summary>
    Unknown = 0,

    /// <summary>JSON.</summary>
    [CloudflareValue("JSON")]
    Json = 1,

    /// <summary>CSV.</summary>
    [CloudflareValue("CSV")]
    Csv = 2,
}

/// <summary>One of the datasets Cloudflare publishes behind radar.</summary>
/// <remarks>
/// <see cref="Alias"/> is not in Cloudflare's published schema for this endpoint, but every answer carries it,
/// and it is the name <c>GET /radar/datasets/{alias}</c> takes - the one radar dataset call this library does
/// not wrap, because it answers a CSV body rather than the JSON envelope. The alias is modelled anyway: it
/// identifies a dataset across dates the way <see cref="Id"/> does not, and a caller reading it out of
/// <see cref="AdditionalData"/> would have to know to look for a field the schema does not mention.
/// </remarks>
public sealed class RadarDataset
{
    /// <summary>The dataset's id, which is what a download asks for.</summary>
    [JsonPropertyName("id")]
    public long? Id { get; set; }

    /// <summary>What the dataset contains.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Whatever Cloudflare attaches to this dataset. It publishes the field as <c>unknown</c> and answers an
    /// object that differs per dataset, so it is read as the JSON it arrives as rather than guessed at.
    /// </summary>
    [JsonPropertyName("meta")]
    public JsonElement? Meta { get; set; }

    /// <summary>The dataset's tags - <c>global</c>, a country code, a period.</summary>
    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    /// <summary>The dataset's title.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>What kind of dataset this is, as Cloudflare spells it.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// The dataset's alias, such as <c>ranking_top_200000</c> - the name that identifies it across dates,
    /// which its numeric id does not. Unpublished, but answered.
    /// </summary>
    [JsonPropertyName("alias")]
    public string? Alias { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"{Id?.ToString(CultureInfo.InvariantCulture) ?? "?"} {Title ?? "?"}" +
        (Type is { Length: > 0 } type ? $" ({type})" : string.Empty);
}

/// <summary>The <c>result</c> of a dataset list: Cloudflare wraps the array in a named property.</summary>
public sealed class RadarDatasetListResult
{
    /// <summary>The datasets.</summary>
    [JsonPropertyName("datasets")]
    public List<RadarDataset>? Datasets { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>
/// Query parameters for listing radar datasets, which page with <c>offset</c> and <c>limit</c> and answer no
/// <c>result_info</c>.
/// </summary>
/// <remarks>
/// <see cref="Date"/> is a date rather than a timestamp: Cloudflare publishes this one parameter with
/// <c>format: date</c>, where the annotation filters beside it are <c>date-time</c>, and it is sent as
/// <c>yyyy-MM-dd</c> accordingly.
/// </remarks>
public sealed class RadarDatasetListParameters : ICloudflareQueryParameters
{
    /// <summary>Filters to one kind of dataset.</summary>
    public RadarDatasetType? DatasetType { get; set; }

    /// <summary>Filters to the datasets Cloudflare published for one date.</summary>
    public DateTimeOffset? Date { get; set; }

    /// <summary>How many datasets to return.</summary>
    public int? Limit { get; set; }

    /// <summary>How many datasets to skip first.</summary>
    public int? Offset { get; set; }

    /// <inheritdoc />
    public void Apply(QueryStringBuilder query)
    {
        if (DatasetType is { } type)
        {
            if (!CloudflareEnumConverter<RadarDatasetType>.TryToWire(type, out var wire))
            {
                throw new CloudflareConfigurationException(
                    $"'{type}' is not a radar dataset type this version of the library knows about, so it " +
                    "cannot be sent as a filter.");
            }

            query.Add("datasetType", wire);
        }

        // Published as a date, not a timestamp, so it is sent without the time part the annotation filters
        // beside it carry.
        if (Date is { } date)
        {
            query.Add("date", date, "yyyy-MM-dd");
        }

        query.Add("limit", Limit);
        query.Add("offset", Offset);
    }

    /// <summary>A copy of these parameters that starts further along the list.</summary>
    /// <param name="offset">How many datasets to skip.</param>
    /// <returns>The new parameters.</returns>
    public RadarDatasetListParameters WithOffset(int offset) => new()
    {
        DatasetType = DatasetType,
        Date = Date,
        Limit = Limit,
        Offset = offset,
    };
}

/// <summary>Where to fetch a dataset Cloudflare has prepared.</summary>
/// <remarks>
/// The URL is Cloudflare's, not this library's: it points at the file and carries whatever signature Cloudflare
/// put on it, so a caller fetches it with an HTTP client of their own. Nothing here reads it, which is also why
/// a CSV download is safe to ask for and a CSV <c>format</c> on a read endpoint would not be.
/// </remarks>
public sealed class RadarDatasetDownload
{
    /// <summary>The address of the prepared file.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => Url ?? "?";
}

/// <summary>The <c>result</c> of a dataset download request.</summary>
public sealed class RadarDatasetDownloadResult
{
    /// <summary>The download.</summary>
    [JsonPropertyName("dataset")]
    public RadarDatasetDownload? Dataset { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>The body of a dataset download request.</summary>
/// <remarks>
/// Only the id travels in the body: Cloudflare takes the format as a query parameter on the same call, which is
/// why <c>IRadarService.RequestDatasetDownloadAsync</c> has both a body and a query string.
/// </remarks>
public sealed class RadarDatasetDownloadRequest
{
    /// <summary>The id of the dataset to prepare.</summary>
    [JsonPropertyName("datasetId")]
    public long DatasetId { get; set; }
}
