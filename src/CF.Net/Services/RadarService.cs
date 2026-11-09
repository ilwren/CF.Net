using System.Runtime.CompilerServices;
using CFNet.Http;
using CFNet.Models.Radar;

namespace CFNet.Services;

/// <summary>
/// Cloudflare Radar's reference data: the locations, autonomous systems and geolocations the rest of the radar
/// API is queried by, the entity behind an IP address, and the global search that finds any of them.
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
/// Permissions. Cloudflare's reference names <b>User Details Read</b> for these endpoints, which is unlike
/// anything else it publishes for public data; a live run is what settles whether a token needs it, and the
/// integration suite reports the answer either way.
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
}

/// <inheritdoc cref="IRadarService" />
public sealed class RadarService : CloudflareService, IRadarService
{
    private const string Entities = "radar/entities";
    private const string Geolocations = "radar/geolocations";
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
}
