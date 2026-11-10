using System.Runtime.CompilerServices;
using CFNet.Exceptions;
using CFNet.Http;
using CFNet.Models.Radar;
using CFNet.Serialization;

namespace CFNet.Services;

/// <summary>
/// Cloudflare Radar's reference data and rankings: the locations, autonomous systems and geolocations the rest
/// of the radar API is queried by, the entity behind an IP address, the global search that finds any of them,
/// the annotations Cloudflare attaches to its own data, the datasets it publishes, what it knows about how
/// autonomous systems relate to one another, how it ranks the domains and internet services behind the traffic
/// it sees, and what it measures about the bots in that traffic.
/// </summary>
/// <remarks>
/// Scope. None of these paths carries an account or a zone - they sit at the API root beside <c>/ips</c>, and
/// they answer the same public data for every caller, so no method here takes an account or zone id.
///
/// Pagination. The radar endpoints page with <c>offset</c> and <c>limit</c>, not the <c>page</c> and
/// <c>per_page</c> the rest of the v4 API uses, and they answer no <c>result_info</c> at all - so there is no
/// total to read and no cursor to follow. A page that comes back with a different number of items than the limit
/// asked for is the end of the list, which is the rule <see cref="ListAllLocationsAsync"/> and
/// <see cref="ListAllAsnsAsync"/> walk by.
///
/// Result shape. Every radar answer wraps its payload in a property named for it - <c>{locations: […]}</c>,
/// <c>{asn: {…}}</c>, <c>{search: […]}</c> - rather than putting the array or object straight in <c>result</c>,
/// so each call here unwraps one level before handing anything back.
///
/// Permissions. Cloudflare's reference names <b>User Details Read</b> for every radar endpoint - a group that
/// appears nowhere in its permission catalogue - and says nothing about <b>Radar Read</b>, which the live
/// catalogue does carry. A run read all of these with a token holding neither, so the reference data needs no
/// permission at all; the integration suite keeps <c>Radar Read</c> named against these rows anyway, so that a
/// refusal in future names a group that exists and can be put in a token URL.
/// </remarks>
public interface IRadarService
{
    /// <summary>
    /// Lists one page of the locations radar's data is grouped by.
    /// </summary>
    /// <param name="parameters">Filters and paging, or <see langword="null"/> for Cloudflare's defaults.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The locations on that page.</returns>
    Task<CloudflarePage<RadarLocation>> ListLocationsAsync(
        RadarLocationListParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Walks every location, moving along by <c>offset</c>.
    /// </summary>
    /// <param name="pageSize">How many locations to ask for per page; 100 when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async sequence of every location.</returns>
    IAsyncEnumerable<RadarLocation> ListAllLocationsAsync(
        int? pageSize = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one location by its alpha-2 code.
    /// </summary>
    /// <param name="location">The alpha-2 code, such as <c>GB</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The location, or <see langword="null"/> when Cloudflare has no such location.</returns>
    Task<RadarLocation?> GetLocationAsync(string location, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists one page of autonomous systems.
    /// </summary>
    /// <param name="parameters">Filters and paging, or <see langword="null"/> for Cloudflare's defaults.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ASNs on that page.</returns>
    Task<CloudflarePage<RadarAsn>> ListAsnsAsync(
        RadarAsnListParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Walks every autonomous system, moving along by <c>offset</c>. This is a long list - there are tens of
    /// thousands of ASNs - so give it a page size and a cancellation token.
    /// </summary>
    /// <param name="pageSize">How many ASNs to ask for per page; 100 when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async sequence of every ASN.</returns>
    IAsyncEnumerable<RadarAsn> ListAllAsnsAsync(
        int? pageSize = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one autonomous system, including its related ASNs and the per-location breakdown of its estimated
    /// users - neither of which the list answers.
    /// </summary>
    /// <param name="asn">The ASN number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ASN, or <see langword="null"/> when Cloudflare has no such ASN.</returns>
    Task<RadarAsn?> GetAsnAsync(long asn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the autonomous system behind an IP address, with the same detail as a read by number.
    /// </summary>
    /// <param name="ip">The address to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ASN, or <see langword="null"/> when Cloudflare answers nothing for the address.</returns>
    Task<RadarAsn?> GetAsnForIpAsync(string ip, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads what radar knows about an IP address itself: where it is, and the ASN behind it.
    /// </summary>
    /// <param name="ip">The address to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The entity, or <see langword="null"/> when Cloudflare answers nothing for the address.</returns>
    Task<RadarIpEntity?> GetEntityForIpAsync(string ip, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists one page of radar's geolocation tree - continents, countries and first-level administrative
    /// divisions, each carrying the place above it.
    /// </summary>
    /// <param name="parameters">Filters and paging, or <see langword="null"/> for Cloudflare's defaults.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The geolocations on that page.</returns>
    Task<CloudflarePage<RadarGeolocation>> ListGeolocationsAsync(
        RadarGeolocationListParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one geolocation by its <c>geoId</c>.
    /// </summary>
    /// <remarks>
    /// The id this call wants is <see cref="RadarGeolocation.GeoId"/> - a number such as <c>3175395</c>, which
    /// a live answer carries on every place and every parent even though Cloudflare's published schema does not
    /// list the field. Asking by <see cref="RadarGeolocation.Code"/> instead is answered <c>400 [2001]</c>.
    /// </remarks>
    /// <param name="geoId">The geolocation's <c>geoId</c>, such as <c>3175395</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The geolocation, or <see langword="null"/> when Cloudflare has no such place.</returns>
    Task<RadarGeolocation?> GetGeolocationAsync(string geoId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches every kind of radar entity for one string.
    /// </summary>
    /// <param name="parameters">The query and what to narrow it to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The hits, in the order Cloudflare ranked them.</returns>
    /// <exception cref="CFNet.Exceptions.CloudflareConfigurationException">Thrown when the query is missing or a
    /// category is one this library has not met.</exception>
    Task<IReadOnlyList<RadarSearchHit>> SearchAsync(
        RadarSearchParameters parameters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches every kind of radar entity for one string, with nothing narrowed.
    /// </summary>
    /// <param name="query">The string to search for.</param>
    /// <param name="limit">How many hits to ask for, or <see langword="null"/> for Cloudflare's default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The hits, in the order Cloudflare ranked them.</returns>
    Task<IReadOnlyList<RadarSearchHit>> SearchAsync(
        string query,
        int? limit = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists one page of radar's annotations: the outages, traffic anomalies and pipeline events Cloudflare
    /// notices in its own data, with the ASNs, locations and origins each one is about.
    /// </summary>
    /// <param name="parameters">Filters and paging, or <see langword="null"/> for the latest annotations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The annotations on that page.</returns>
    Task<CloudflarePage<RadarAnnotation>> ListAnnotationsAsync(
        RadarAnnotationListParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists one page of the outage annotations only.
    /// </summary>
    /// <remarks>
    /// This is the annotation list narrowed to outages and it answers the same shape, so it takes the same
    /// parameters - of which Cloudflare publishes neither <c>eventType</c> (the endpoint is already the outage
    /// one) nor <c>tld</c> for this path, and both are ignored here rather than silently sent.
    /// </remarks>
    /// <param name="parameters">Filters and paging, or <see langword="null"/> for the latest outages.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The outage annotations on that page.</returns>
    Task<CloudflarePage<RadarAnnotation>> ListOutagesAsync(
        RadarAnnotationListParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the client countries the outages in a period reached.
    /// </summary>
    /// <remarks>
    /// A different shape from the annotations themselves, answered under the same <c>annotations</c> property:
    /// each item is a country and a measured value rather than a described event.
    /// </remarks>
    /// <param name="parameters">The date range and a limit - this endpoint publishes no offset.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The locations.</returns>
    Task<CloudflarePage<RadarOutageLocation>> ListOutageLocationsAsync(
        RadarOutageLocationsParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists one page of the datasets Cloudflare publishes behind radar.
    /// </summary>
    /// <param name="parameters">Filters and paging, or <see langword="null"/> for every dataset.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The datasets on that page.</returns>
    Task<CloudflarePage<RadarDataset>> ListDatasetsAsync(
        RadarDatasetListParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks Cloudflare to prepare one dataset, and reads back where to fetch it.
    /// </summary>
    /// <remarks>
    /// The answer is a URL rather than the file, and nothing in this library fetches it - which is also why
    /// asking for CSV is safe here and would not be on a read endpoint: the format names what the file behind
    /// the URL holds, while the answer itself stays the JSON envelope. The dataset id travels in the body and
    /// the format in the query string, because that is how Cloudflare publishes this one call.
    /// </remarks>
    /// <param name="datasetId">The id of the dataset, as <see cref="ListDatasetsAsync"/> answers it.</param>
    /// <param name="format">What the prepared file should hold; JSON unless a caller asks otherwise.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The download, or <see langword="null"/> when Cloudflare answered no result.</returns>
    /// <exception cref="CloudflareConfigurationException">Thrown when the format is one this library has not
    /// met.</exception>
    Task<RadarDatasetDownload?> RequestDatasetDownloadAsync(
        long datasetId,
        RadarDatasetFormat format = RadarDatasetFormat.Json,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the AS-SETs one ASN belongs to, and the paths from each of them up to its upstreams.
    /// </summary>
    /// <param name="asn">The ASN to ask about.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The AS-SETs and paths, or <see langword="null"/> when Cloudflare has none for that ASN.</returns>
    Task<RadarAsnAsSet?> GetAsnAsSetAsync(long asn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads how one ASN relates to the others Cloudflare sees it exchanging traffic with.
    /// </summary>
    /// <param name="asn">The ASN to ask about.</param>
    /// <param name="asn2">Narrows the answer to one relationship, or <see langword="null"/> for all of them.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The relationships, with when Cloudflare measured them, or <see langword="null"/> when it has
    /// none.</returns>
    Task<RadarAsnRelationships?> GetAsnRelationshipsAsync(
        long asn,
        long? asn2 = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one page of the ranking of autonomous systems by their presence in Cloudflare's botnet threat
    /// feed.
    /// </summary>
    /// <remarks>
    /// Not a <see cref="CloudflarePage{T}"/> although it pages with <c>offset</c> and <c>limit</c>: Cloudflare
    /// answers a <c>meta</c> object beside the list whose <c>total</c> is the only total this endpoint offers,
    /// and wrapping the list alone would drop it.
    /// </remarks>
    /// <param name="parameters">Filters, ranking and paging, or <see langword="null"/> for the most recent
    /// ranking.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ranking and its metadata, or <see langword="null"/> when Cloudflare answered no
    /// result.</returns>
    Task<RadarBotnetThreatFeed?> ListBotnetThreatFeedAsync(
        RadarBotnetThreatFeedParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads Cloudflare's ranking of the most-used domains: the popular ones, or the ones trending.
    /// </summary>
    /// <remarks>
    /// The answer is one series per date or label asked for, and Cloudflare numbers them - <c>top_0</c>, then
    /// <c>top_1</c> - while publishing only the first. The first is typed; the rest arrive in the result's
    /// <c>AdditionalData</c>, which <see cref="RadarRankingTop.SeriesNames"/> lists.
    /// </remarks>
    /// <param name="parameters">Filters and ranking type, or <see langword="null"/> for Cloudflare's defaults.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ranking, with what it covers.</returns>
    Task<RadarRankingTop?> GetTopDomainsAsync(
        RadarRankingTopParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads how the domain ranking moved over time, as one group of series per period asked for.
    /// </summary>
    /// <param name="parameters">Periods and filters, or <see langword="null"/> for Cloudflare's defaults.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The series, with how they were aggregated and scaled.</returns>
    Task<RadarTimeseriesGroups?> GetTopDomainTimeseriesGroupsAsync(
        RadarRankingTimeseriesGroupsParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads where one domain sits in Cloudflare's ranking, with the categories it is in and - when asked for -
    /// the places it is most used.
    /// </summary>
    /// <param name="domain">The domain to ask about, such as <c>cloudflare.com</c>.</param>
    /// <param name="parameters">Dates and filters, or <see langword="null"/> for Cloudflare's defaults.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What Cloudflare knows about the domain's rank.</returns>
    Task<RadarDomainRanking?> GetDomainRankingAsync(
        string domain,
        RadarDomainRankingParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the internet-service categories Cloudflare ranks by, which is what the two service rankings are
    /// filtered with.
    /// </summary>
    /// <param name="parameters">Dates and paging, or <see langword="null"/> for every category.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The categories.</returns>
    Task<RadarInternetServiceCategories?> ListInternetServiceCategoriesAsync(
        RadarInternetServiceCategoriesParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads Cloudflare's ranking of internet services - the services behind the traffic, rather than the
    /// domains in it.
    /// </summary>
    /// <param name="parameters">Filters, or <see langword="null"/> for Cloudflare's defaults.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ranking, with the category and date each series is for.</returns>
    Task<RadarInternetServiceTop?> GetTopInternetServicesAsync(
        RadarInternetServiceTopParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads how the internet-service ranking moved over time.
    /// </summary>
    /// <param name="parameters">Periods and filters, or <see langword="null"/> for Cloudflare's defaults.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The series, with how they were aggregated and scaled.</returns>
    Task<RadarTimeseriesGroups?> GetInternetServiceTimeseriesGroupsAsync(
        RadarInternetServiceTimeseriesGroupsParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists one page of the bots Cloudflare knows by name, with the operator, category and user-agent patterns
    /// it identifies each of them by.
    /// </summary>
    /// <param name="parameters">Filters and paging, or <see langword="null"/> for every bot.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The bots on that page.</returns>
    Task<CloudflarePage<RadarBot>> ListBotsAsync(
        RadarBotListParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Walks every bot Cloudflare knows, moving along by <c>offset</c>.
    /// </summary>
    /// <param name="pageSize">How many bots to ask for per page; 100 when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async sequence of every bot.</returns>
    IAsyncEnumerable<RadarBot> ListAllBotsAsync(
        int? pageSize = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one bot by the slug the list answers, with the three fields only this read carries: where its
    /// operator is, the user agents themselves, and the agent that signs its requests when Cloudflare has one.
    /// </summary>
    /// <param name="botSlug">The bot's slug, such as <c>googlebot</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The bot, or <see langword="null"/> when Cloudflare has no such bot.</returns>
    Task<RadarBot?> GetBotAsync(string botSlug, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads how the traffic of the bots Cloudflare knows divides over one dimension - per bot, per kind, per
    /// operator or per category.
    /// </summary>
    /// <param name="dimension">What to divide the traffic by. It is part of the path.</param>
    /// <param name="parameters">Filters and periods, or <see langword="null"/> for Cloudflare's defaults.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One entry per value of the dimension.</returns>
    Task<RadarSummary?> GetBotSummaryAsync(
        RadarBotDimension dimension,
        RadarBotSummaryParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads how bot traffic moved over time, as one group per thing the filters named.
    /// </summary>
    /// <remarks>
    /// This is the timeseries with no dimension in its path, and Cloudflare publishes its result as
    /// <c>meta</c> plus an open set of groups named after whatever was filtered by - so the groups are read by
    /// name rather than by position, and <see cref="RadarNamedTimeseries.GroupNames"/> lists what arrived.
    /// </remarks>
    /// <param name="parameters">Filters and periods, or <see langword="null"/> for Cloudflare's defaults.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The groups, by name.</returns>
    Task<RadarNamedTimeseries?> GetBotTimeseriesAsync(
        RadarBotTimeseriesParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads how bot traffic moved over time, divided over one dimension.
    /// </summary>
    /// <param name="dimension">What to divide the traffic by. It is part of the path.</param>
    /// <param name="parameters">Filters and periods, or <see langword="null"/> for Cloudflare's defaults.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The series, with how they were aggregated and scaled.</returns>
    Task<RadarTimeseriesGroups?> GetBotTimeseriesGroupsAsync(
        RadarBotDimension dimension,
        RadarBotTimeseriesGroupsParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads how the traffic of crawlers that identify themselves divides over one dimension - by client type,
    /// user agent, referer, vertical, industry or how the origin answered.
    /// </summary>
    /// <param name="dimension">What to divide the traffic by. It is part of the path.</param>
    /// <param name="parameters">Filters and periods, or <see langword="null"/> for Cloudflare's defaults.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One entry per value of the dimension.</returns>
    Task<RadarSummary?> GetWebCrawlerSummaryAsync(
        RadarWebCrawlerDimension dimension,
        RadarWebCrawlerSummaryParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads how the traffic of crawlers that identify themselves moved over time, divided over one dimension.
    /// </summary>
    /// <param name="dimension">What to divide the traffic by. It is part of the path.</param>
    /// <param name="parameters">Filters, periods and scaling, or <see langword="null"/> for Cloudflare's
    /// defaults.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The series, with how they were aggregated and scaled.</returns>
    Task<RadarTimeseriesGroups?> GetWebCrawlerTimeseriesGroupsAsync(
        RadarWebCrawlerDimension dimension,
        RadarWebCrawlerTimeseriesGroupsParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the verified bots Cloudflare saw most of, with the share of traffic each one accounted for.
    /// </summary>
    /// <param name="parameters">Filters and periods, or <see langword="null"/> for Cloudflare's defaults.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ranking.</returns>
    Task<RadarTopList<RadarVerifiedBot>?> GetTopVerifiedBotsAsync(
        RadarVerifiedBotTopParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the verified-bot categories Cloudflare saw most of, with the share of traffic each accounted for.
    /// </summary>
    /// <param name="parameters">Filters and periods, or <see langword="null"/> for Cloudflare's defaults.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ranking.</returns>
    Task<RadarTopList<RadarVerifiedBotCategory>?> GetTopVerifiedBotCategoriesAsync(
        RadarVerifiedBotTopParameters? parameters = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IRadarService" />
public sealed class RadarService : CloudflareService, IRadarService
{
    private const string Entities = "radar/entities";
    private const string Geolocations = "radar/geolocations";
    private const string Annotations = "radar/annotations";
    private const string Datasets = "radar/datasets";
    private const string Ranking = "radar/ranking";
    private const string Bots = "radar/bots";
    private const string VerifiedBots = "radar/verified_bots";
    private const string InternetServices = "radar/ranking/internet_services";
    private const int DefaultPageSize = 100;
    private const int MaxPages = 10_000;

    /// <summary>Builds the service over a transport.</summary>
    /// <param name="transport">The transport that sends requests and reads Cloudflare's envelope.</param>
    public RadarService(ICloudflareTransport transport)
        : base(transport)
    {
    }

    /// <inheritdoc />
    public async Task<CloudflarePage<RadarLocation>> ListLocationsAsync(
        RadarLocationListParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var request = Apply(CloudflareRequest.Get($"{Entities}/locations"), parameters);
        request.OperationName = "List Radar Locations";

        var response = await Transport.SendAsync<RadarLocationListResult>(request, cancellationToken).ConfigureAwait(false);

        return new CloudflarePage<RadarLocation>(
            response.Result?.Locations ?? new List<RadarLocation>(),
            response.ResultInfo);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RadarLocation> ListAllLocationsAsync(
        int? pageSize = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var size = pageSize ?? DefaultPageSize;
        var offset = 0;
        var guard = 0;

        while (guard++ < MaxPages)
        {
            var page = await ListLocationsAsync(
                new RadarLocationListParameters { Limit = size, Offset = offset },
                cancellationToken).ConfigureAwait(false);

            foreach (var item in page.Items)
            {
                yield return item;
            }

            // Radar answers no total and no cursor, so the page's own length is the only signal there is: a
            // page of any other size than the one asked for is the last one - shorter when the list ran out,
            // longer when Cloudflare ignored the limit and sent everything at once.
            if (page.Count != size)
            {
                yield break;
            }

            offset += size;
        }
    }

    /// <inheritdoc />
    public async Task<RadarLocation?> GetLocationAsync(string location, CancellationToken cancellationToken = default)
    {
        var path = $"{Entities}/locations/{CloudflareUrl.EncodeSegment(Require(location, nameof(location)))}";
        var request = CloudflareRequest.Get(path).Tolerate(System.Net.HttpStatusCode.NotFound);
        request.OperationName = "Get Radar Location";

        var response = await Transport.SendAsync<RadarLocationResult>(request, cancellationToken).ConfigureAwait(false);
        return response.Result?.Location;
    }

    /// <inheritdoc />
    public async Task<CloudflarePage<RadarAsn>> ListAsnsAsync(
        RadarAsnListParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var request = Apply(CloudflareRequest.Get($"{Entities}/asns"), parameters);
        request.OperationName = "List Radar ASNs";

        var response = await Transport.SendAsync<RadarAsnListResult>(request, cancellationToken).ConfigureAwait(false);

        return new CloudflarePage<RadarAsn>(
            response.Result?.Asns ?? new List<RadarAsn>(),
            response.ResultInfo);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RadarAsn> ListAllAsnsAsync(
        int? pageSize = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var size = pageSize ?? DefaultPageSize;
        var offset = 0;
        var guard = 0;

        while (guard++ < MaxPages)
        {
            var page = await ListAsnsAsync(
                new RadarAsnListParameters { Limit = size, Offset = offset },
                cancellationToken).ConfigureAwait(false);

            foreach (var item in page.Items)
            {
                yield return item;
            }

            if (page.Count != size)
            {
                yield break;
            }

            offset += size;
        }
    }

    /// <inheritdoc />
    public async Task<RadarAsn?> GetAsnAsync(long asn, CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get($"{Entities}/asns/{CloudflareUrl.Format(asn)}")
            .Tolerate(System.Net.HttpStatusCode.NotFound);
        request.OperationName = "Get Radar ASN";

        var response = await Transport.SendAsync<RadarAsnResult>(request, cancellationToken).ConfigureAwait(false);
        return response.Result?.Asn;
    }

    /// <inheritdoc />
    public async Task<RadarAsn?> GetAsnForIpAsync(string ip, CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get($"{Entities}/asns/ip");
        request.OperationName = "Get Radar ASN by IP";
        request.Query.Add("ip", Require(ip, nameof(ip)));

        var response = await Transport.SendAsync<RadarAsnResult>(request, cancellationToken).ConfigureAwait(false);
        return response.Result?.Asn;
    }

    /// <inheritdoc />
    public async Task<RadarIpEntity?> GetEntityForIpAsync(string ip, CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get($"{Entities}/ip");
        request.OperationName = "Get Radar Entity by IP";
        request.Query.Add("ip", Require(ip, nameof(ip)));

        var response = await Transport.SendAsync<RadarIpEntityResult>(request, cancellationToken).ConfigureAwait(false);
        return response.Result?.Ip;
    }

    /// <inheritdoc />
    public async Task<CloudflarePage<RadarGeolocation>> ListGeolocationsAsync(
        RadarGeolocationListParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var request = Apply(CloudflareRequest.Get(Geolocations), parameters);
        request.OperationName = "List Radar Geolocations";

        var response = await Transport.SendAsync<RadarGeolocationListResult>(request, cancellationToken).ConfigureAwait(false);

        return new CloudflarePage<RadarGeolocation>(
            response.Result?.Geolocations ?? new List<RadarGeolocation>(),
            response.ResultInfo);
    }

    /// <inheritdoc />
    public async Task<RadarGeolocation?> GetGeolocationAsync(string geoId, CancellationToken cancellationToken = default)
    {
        var path = $"{Geolocations}/{CloudflareUrl.EncodeSegment(Require(geoId, nameof(geoId)))}";
        var request = CloudflareRequest.Get(path).Tolerate(System.Net.HttpStatusCode.NotFound);
        request.OperationName = "Get Radar Geolocation";

        var response = await Transport.SendAsync<RadarGeolocationResult>(request, cancellationToken).ConfigureAwait(false);
        return response.Result?.Geolocation;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RadarSearchHit>> SearchAsync(
        RadarSearchParameters parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.EnsureValid();

        var request = Apply(CloudflareRequest.Get("radar/search/global"), parameters);
        request.OperationName = "Search Radar";

        var response = await Transport.SendAsync<RadarSearchResults>(request, cancellationToken).ConfigureAwait(false);
        return response.Result?.Search ?? new List<RadarSearchHit>();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RadarSearchHit>> SearchAsync(
        string query,
        int? limit = null,
        CancellationToken cancellationToken = default) =>
        SearchAsync(new RadarSearchParameters { Query = query, Limit = limit }, cancellationToken);

    /// <inheritdoc />
    public async Task<CloudflarePage<RadarAnnotation>> ListAnnotationsAsync(
        RadarAnnotationListParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var request = Apply(CloudflareRequest.Get(Annotations), parameters);
        request.OperationName = "List Radar Annotations";

        var response = await Transport.SendAsync<RadarAnnotationListResult>(request, cancellationToken).ConfigureAwait(false);

        return new CloudflarePage<RadarAnnotation>(
            response.Result?.Annotations ?? new List<RadarAnnotation>(),
            response.ResultInfo);
    }

    /// <inheritdoc />
    public async Task<CloudflarePage<RadarAnnotation>> ListOutagesAsync(
        RadarAnnotationListParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var request = Apply(CloudflareRequest.Get($"{Annotations}/outages"), parameters);
        request.OperationName = "List Radar Outages";

        var response = await Transport.SendAsync<RadarAnnotationListResult>(request, cancellationToken).ConfigureAwait(false);

        return new CloudflarePage<RadarAnnotation>(
            response.Result?.Annotations ?? new List<RadarAnnotation>(),
            response.ResultInfo);
    }

    /// <inheritdoc />
    public async Task<CloudflarePage<RadarOutageLocation>> ListOutageLocationsAsync(
        RadarOutageLocationsParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var request = Apply(CloudflareRequest.Get($"{Annotations}/outages/locations"), parameters);
        request.OperationName = "List Radar Outage Locations";

        var response = await Transport.SendAsync<RadarOutageLocationsResult>(request, cancellationToken).ConfigureAwait(false);

        return new CloudflarePage<RadarOutageLocation>(
            response.Result?.Annotations ?? new List<RadarOutageLocation>(),
            response.ResultInfo);
    }

    /// <inheritdoc />
    public async Task<CloudflarePage<RadarDataset>> ListDatasetsAsync(
        RadarDatasetListParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var request = Apply(CloudflareRequest.Get(Datasets), parameters);
        request.OperationName = "List Radar Datasets";

        var response = await Transport.SendAsync<RadarDatasetListResult>(request, cancellationToken).ConfigureAwait(false);

        return new CloudflarePage<RadarDataset>(
            response.Result?.Datasets ?? new List<RadarDataset>(),
            response.ResultInfo);
    }

    /// <inheritdoc />
    public async Task<RadarDatasetDownload?> RequestDatasetDownloadAsync(
        long datasetId,
        RadarDatasetFormat format = RadarDatasetFormat.Json,
        CancellationToken cancellationToken = default)
    {
        if (!CloudflareEnumConverter<RadarDatasetFormat>.TryToWire(format, out var wire))
        {
            throw new CloudflareConfigurationException(
                $"'{format}' is not a dataset format this version of the library knows about, so it cannot be " +
                "asked for.");
        }

        var request = CloudflareRequest.Post(
            $"{Datasets}/download",
            new RadarDatasetDownloadRequest { DatasetId = datasetId });

        // The id travels in the body and the format in the query string, which is how Cloudflare publishes
        // this one call.
        request.Query.Add("format", wire);
        request.OperationName = "Download Radar Dataset";

        var response = await Transport.SendAsync<RadarDatasetDownloadResult>(request, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(response.Result?.Dataset?.Url))
        {
            return response.Result!.Dataset;
        }

        // With format=CSV Cloudflare does not answer the envelope it publishes. It answers one line of text -
        // "Dataset url https://…", an address signed for an hour - which is not JSON at all, so it parses to
        // nothing and a call that worked reads as one that answered no address. The address is in the text,
        // so it is read out of the text, whole: the signature is what makes it usable, and a caller needs all
        // of it. Only a log line needs less than that, and cutting it is the log's business.
        // The line is "Dataset url <address>", but the run that found it did not match that spelling exactly -
        // it printed as though the separator were a space, and an exact match on one still missed, which is what
        // a tab or a byte-order mark looks like in a log. So the address is the first token that is one, rather
        // than a prefix to compare against, and a body that looks like JSON is left alone: hunting for an
        // address inside an envelope that answered none would invent a download Cloudflare did not offer.
        var body = response.RawBody?.Trim();
        if (!string.IsNullOrEmpty(body) && !body.StartsWith("{", StringComparison.Ordinal))
        {
            foreach (var token in body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (Uri.TryCreate(token, UriKind.Absolute, out var address) &&
                    address is not null &&
                    (address.Scheme == Uri.UriSchemeHttp || address.Scheme == Uri.UriSchemeHttps))
                {
                    return new RadarDatasetDownload { Url = token };
                }
            }
        }

        return response.Result?.Dataset;
    }

    /// <inheritdoc />
    public async Task<RadarAsnAsSet?> GetAsnAsSetAsync(long asn, CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get($"{Entities}/asns/{CloudflareUrl.Format(asn)}/as_set")
            .Tolerate(System.Net.HttpStatusCode.NotFound);
        request.OperationName = "Get Radar ASN AS-SET";

        var response = await Transport.SendAsync<RadarAsnAsSet>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<RadarAsnRelationships?> GetAsnRelationshipsAsync(
        long asn,
        long? asn2 = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get($"{Entities}/asns/{CloudflareUrl.Format(asn)}/rel")
            .Tolerate(System.Net.HttpStatusCode.NotFound);
        request.Query.Add("asn2", asn2);
        request.OperationName = "Get Radar ASN Relationships";

        var response = await Transport.SendAsync<RadarAsnRelationships>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<RadarBotnetThreatFeed?> ListBotnetThreatFeedAsync(
        RadarBotnetThreatFeedParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var request = Apply(CloudflareRequest.Get($"{Entities}/asns/botnet_threat_feed"), parameters);
        request.OperationName = "List Radar Botnet Threat Feed";

        var response = await Transport.SendAsync<RadarBotnetThreatFeed>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<RadarRankingTop?> GetTopDomainsAsync(
        RadarRankingTopParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        parameters?.EnsureValid();

        var request = Apply(CloudflareRequest.Get($"{Ranking}/top"), parameters);
        request.OperationName = "Get Radar Top Domains";

        var response = await Transport.SendAsync<RadarRankingTop>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<RadarTimeseriesGroups?> GetTopDomainTimeseriesGroupsAsync(
        RadarRankingTimeseriesGroupsParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        parameters?.EnsureValid();

        var request = Apply(CloudflareRequest.Get($"{Ranking}/timeseries_groups"), parameters);
        request.OperationName = "Get Radar Top Domain Timeseries Groups";

        var response = await Transport.SendAsync<RadarTimeseriesGroups>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<RadarDomainRanking?> GetDomainRankingAsync(
        string domain,
        RadarDomainRankingParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        parameters?.EnsureValid();

        var request = Apply(
            CloudflareRequest.Get($"{Ranking}/domain/{CloudflareUrl.EncodeSegment(Require(domain, nameof(domain)))}"),
            parameters);
        request.OperationName = "Get Radar Domain Ranking";

        var response = await Transport.SendAsync<RadarDomainRanking>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<RadarInternetServiceCategories?> ListInternetServiceCategoriesAsync(
        RadarInternetServiceCategoriesParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        parameters?.EnsureValid();

        var request = Apply(CloudflareRequest.Get($"{InternetServices}/categories"), parameters);
        request.OperationName = "List Radar Internet Service Categories";

        var response = await Transport.SendAsync<RadarInternetServiceCategories>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<RadarInternetServiceTop?> GetTopInternetServicesAsync(
        RadarInternetServiceTopParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        parameters?.EnsureValid();

        var request = Apply(CloudflareRequest.Get($"{InternetServices}/top"), parameters);
        request.OperationName = "Get Radar Top Internet Services";

        var response = await Transport.SendAsync<RadarInternetServiceTop>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<RadarTimeseriesGroups?> GetInternetServiceTimeseriesGroupsAsync(
        RadarInternetServiceTimeseriesGroupsParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        parameters?.EnsureValid();

        var request = Apply(CloudflareRequest.Get($"{InternetServices}/timeseries_groups"), parameters);
        request.OperationName = "Get Radar Internet Service Timeseries Groups";

        var response = await Transport.SendAsync<RadarTimeseriesGroups>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<CloudflarePage<RadarBot>> ListBotsAsync(
        RadarBotListParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var request = Apply(CloudflareRequest.Get(Bots), parameters);
        request.OperationName = "List Radar Bots";

        var response = await Transport.SendAsync<RadarBotListResult>(request, cancellationToken).ConfigureAwait(false);

        return new CloudflarePage<RadarBot>(
            response.Result?.Bots ?? new List<RadarBot>(),
            response.ResultInfo);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RadarBot> ListAllBotsAsync(
        int? pageSize = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var size = pageSize ?? DefaultPageSize;
        var offset = 0;
        var guard = 0;

        while (guard++ < MaxPages)
        {
            var page = await ListBotsAsync(
                new RadarBotListParameters { Limit = size, Offset = offset },
                cancellationToken).ConfigureAwait(false);

            foreach (var bot in page.Items)
            {
                yield return bot;
            }

            // Radar answers no total and no cursor, so the page's own length is the only signal there is.
            if (page.Count != size)
            {
                yield break;
            }

            offset += size;
        }
    }

    /// <inheritdoc />
    public async Task<RadarBot?> GetBotAsync(string botSlug, CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get($"{Bots}/{CloudflareUrl.EncodeSegment(Require(botSlug, nameof(botSlug)))}")
            .Tolerate(System.Net.HttpStatusCode.NotFound);
        request.OperationName = "Get Radar Bot";

        var response = await Transport.SendAsync<RadarBotResult>(request, cancellationToken).ConfigureAwait(false);
        return response.Result?.Bot;
    }

    /// <inheritdoc />
    public async Task<RadarSummary?> GetBotSummaryAsync(
        RadarBotDimension dimension,
        RadarBotSummaryParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        parameters?.EnsureValid();

        var request = Apply(CloudflareRequest.Get($"{Bots}/summary/{Spell(dimension)}"), parameters);
        request.OperationName = "Get Radar Bot Summary";

        var response = await Transport.SendAsync<RadarSummary>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<RadarNamedTimeseries?> GetBotTimeseriesAsync(
        RadarBotTimeseriesParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        parameters?.EnsureValid();

        var request = Apply(CloudflareRequest.Get($"{Bots}/timeseries"), parameters);
        request.OperationName = "Get Radar Bot Timeseries";

        var response = await Transport.SendAsync<RadarNamedTimeseries>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<RadarTimeseriesGroups?> GetBotTimeseriesGroupsAsync(
        RadarBotDimension dimension,
        RadarBotTimeseriesGroupsParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        parameters?.EnsureValid();

        var request = Apply(
            CloudflareRequest.Get($"{Bots}/timeseries_groups/{Spell(dimension)}"),
            parameters);
        request.OperationName = "Get Radar Bot Timeseries Groups";

        var response = await Transport.SendAsync<RadarTimeseriesGroups>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<RadarSummary?> GetWebCrawlerSummaryAsync(
        RadarWebCrawlerDimension dimension,
        RadarWebCrawlerSummaryParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        parameters?.EnsureValid();

        var request = Apply(CloudflareRequest.Get($"{Bots}/crawlers/summary/{Spell(dimension)}"), parameters);
        request.OperationName = "Get Radar Web Crawler Summary";

        var response = await Transport.SendAsync<RadarSummary>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<RadarTimeseriesGroups?> GetWebCrawlerTimeseriesGroupsAsync(
        RadarWebCrawlerDimension dimension,
        RadarWebCrawlerTimeseriesGroupsParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        parameters?.EnsureValid();

        var request = Apply(
            CloudflareRequest.Get($"{Bots}/crawlers/timeseries_groups/{Spell(dimension)}"),
            parameters);
        request.OperationName = "Get Radar Web Crawler Timeseries Groups";

        var response = await Transport.SendAsync<RadarTimeseriesGroups>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<RadarTopList<RadarVerifiedBot>?> GetTopVerifiedBotsAsync(
        RadarVerifiedBotTopParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        parameters?.EnsureValid();

        var request = Apply(CloudflareRequest.Get($"{VerifiedBots}/top/bots"), parameters);
        request.OperationName = "Get Radar Top Verified Bots";

        var response = await Transport.SendAsync<RadarTopList<RadarVerifiedBot>>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<RadarTopList<RadarVerifiedBotCategory>?> GetTopVerifiedBotCategoriesAsync(
        RadarVerifiedBotTopParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        parameters?.EnsureValid();

        var request = Apply(CloudflareRequest.Get($"{VerifiedBots}/top/categories"), parameters);
        request.OperationName = "Get Radar Top Verified Bot Categories";

        var response = await Transport.SendAsync<RadarTopList<RadarVerifiedBotCategory>>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    // A dimension is part of the path, so one this library cannot spell has nowhere to be put - which is worth
    // saying as a configuration problem rather than leaving the converter's own exception to explain it.
    private static string Spell(RadarBotDimension dimension)
    {
        // TryToWire only answers true with a spelling in hand, so the null-forgiving operator is not a
        // guess here: the branch with nothing to say is the one that throws.
        return CloudflareEnumConverter<RadarBotDimension>.TryToWire(dimension, out var wire)
            ? wire!
            : throw new CloudflareConfigurationException(
                $"'{dimension}' is not a radar bot dimension this version of the library knows about, so " +
                "there is no path to put it in.");
    }

    private static string Spell(RadarWebCrawlerDimension dimension)
    {
        return CloudflareEnumConverter<RadarWebCrawlerDimension>.TryToWire(dimension, out var wire)
            ? wire!
            : throw new CloudflareConfigurationException(
                $"'{dimension}' is not a radar web-crawler dimension this version of the library knows " +
                "about, so there is no path to put it in.");
    }
}
