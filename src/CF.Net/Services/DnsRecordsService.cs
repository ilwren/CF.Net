using System.Runtime.CompilerServices;
using System.Text.Json;
using CFNet.Http;
using CFNet.Models.Dns;

namespace CFNet.Services;

/// <summary>
/// Operations on DNS records inside a zone: <c>/zones/{zone_id}/dns_records</c>.
/// </summary>
public interface IDnsRecordsService
{
    /// <summary>Lists records one page at a time.</summary>
    /// <param name="parameters">Optional filters and paging.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CloudflarePage<DnsRecord>> ListAsync(
        DnsRecordListParameters? parameters = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Enumerates every record in a zone, following page-number pagination automatically.</summary>
    /// <param name="parameters">Optional filters; <c>Page</c> is ignored.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<DnsRecord> ListAllAsync(
        DnsRecordListParameters? parameters = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Finds records matching a name.</summary>
    /// <param name="name">The record name, e.g. <c>www.example.com</c>.</param>
    /// <param name="type">Optional record type filter.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<DnsRecord>> FindAsync(
        string name,
        string? type = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches one record.</summary>
    /// <param name="recordId">Record id.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The record, or <c>null</c> when it does not exist or the zone cannot be read with these
    /// credentials - Cloudflare answers 403 for both.
    /// </returns>
    Task<DnsRecord?> GetAsync(string recordId, string? zoneId = null, CancellationToken cancellationToken = default);

    /// <summary>Creates a record.</summary>
    /// <param name="record">The record to create.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DnsRecord> CreateAsync(DnsRecord record, string? zoneId = null, CancellationToken cancellationToken = default);

    /// <summary>Creates an A/AAAA record pointing at an address.</summary>
    /// <param name="name">Record name.</param>
    /// <param name="content">IP address.</param>
    /// <param name="proxied">Whether traffic goes through the Cloudflare proxy.</param>
    /// <param name="ttl">TTL in seconds; <c>1</c> means automatic.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DnsRecord> CreateAddressRecordAsync(
        string name,
        string content,
        bool proxied = true,
        int ttl = 1,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a CNAME record.</summary>
    /// <param name="name">Record name.</param>
    /// <param name="target">Target hostname.</param>
    /// <param name="proxied">Whether traffic goes through the Cloudflare proxy.</param>
    /// <param name="ttl">TTL in seconds; <c>1</c> means automatic.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DnsRecord> CreateCnameRecordAsync(
        string name,
        string target,
        bool proxied = true,
        int ttl = 1,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a TXT record.</summary>
    /// <param name="name">Record name.</param>
    /// <param name="content">Record text.</param>
    /// <param name="ttl">TTL in seconds.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DnsRecord> CreateTxtRecordAsync(
        string name,
        string content,
        int ttl = 1,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Partially updates a record (<c>PATCH</c>). Only the members set on <paramref name="record"/> are
    /// sent; everything else keeps its current value.
    /// </summary>
    /// <param name="recordId">Record id.</param>
    /// <param name="record">The fields to change.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DnsRecord> UpdateAsync(
        string recordId,
        DnsRecord record,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a record wholesale (<c>PUT</c>). Members left <c>null</c> are reset to Cloudflare's
    /// defaults, which is not the same as <see cref="UpdateAsync"/>.
    /// </summary>
    /// <param name="recordId">Record id.</param>
    /// <param name="record">The complete new record.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DnsRecord> ReplaceAsync(
        string recordId,
        DnsRecord record,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a record.</summary>
    /// <param name="recordId">Record id.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(string recordId, string? zoneId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports every record in a zone as a BIND config file.
    /// </summary>
    /// <remarks>
    /// <c>GET /zones/{zone_id}/dns_records/export</c> answers with the zone file itself rather than a
    /// JSON envelope, so this returns the text verbatim - empty when the zone has no records.
    /// </remarks>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string> ExportAsync(string? zoneId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports records from a BIND config file.
    /// </summary>
    /// <param name="bindContents">The zone file text, as <see cref="ExportAsync"/> produces.</param>
    /// <param name="proxied">
    /// Whether proxiable records should be imported as proxied. <c>null</c> lets Cloudflare decide,
    /// which keeps whatever each record's own comment says.
    /// </param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many records were parsed and added, or <c>null</c> when Cloudflare returned none.</returns>
    Task<DnsRecordsImportResult?> ImportAsync(
        string bindContents,
        bool? proxied = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a batch of record changes - deletes, patches, puts and posts - as a single call.
    /// </summary>
    /// <remarks>
    /// Cloudflare executes the four groups in its own order (deletes, then patches, puts and posts) inside
    /// one database transaction, though propagation is not atomic because each record change is its own
    /// key-value pair in the store behind it. The published reference documents the request body but not
    /// what a successful answer's <c>result</c> looks like, so this returns it raw rather than modelling a
    /// guess; the integration suite logs what Cloudflare sends, which is how that shape gets pinned down.
    /// </remarks>
    /// <param name="batch">The changes to make. Checked by <see cref="DnsRecordBatch.EnsureValid"/> first.</param>
    /// <param name="includeShadowMetadata">
    /// Whether each returned record carries shadow metadata in its <c>meta</c> field. <c>null</c> leaves the
    /// query parameter out and Cloudflare's default stands.
    /// </param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<JsonElement?> BatchAsync(
        DnsRecordBatch batch,
        bool? includeShadowMetadata = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the records found by a registrar scan - the ones a scan review accepts or rejects.
    /// </summary>
    /// <remarks>
    /// <c>GET /zones/{zone_id}/dns_records/scan/review</c>. A zone that has never been scanned answers 200
    /// with an empty list rather than an error, observed live, so an empty page is a normal answer here and
    /// not a refusal or a missing permission.
    /// </remarks>
    /// <param name="perPage">Page size; <c>null</c> leaves Cloudflare's default in place.</param>
    /// <param name="zoneId">Zone id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CloudflarePage<DnsRecord>> ListScannedRecordsAsync(
        int? perPage = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class DnsRecordsService : CloudflareService, IDnsRecordsService
{
    /// <summary>Creates the service.</summary>
    /// <param name="transport">The transport to execute requests through.</param>
    public DnsRecordsService(ICloudflareTransport transport)
        : base(transport)
    {
    }

    /// <inheritdoc />
    public async Task<CloudflarePage<DnsRecord>> ListAsync(
        DnsRecordListParameters? parameters = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var zone = ResolveZoneId(zoneId);
        var request = Apply(CloudflareRequest.Get($"{ZonePath(zone)}/dns_records"), parameters);
        request.OperationName = "List DNS Records";

        var response = await Transport.SendAsync<List<DnsRecord>>(request, cancellationToken).ConfigureAwait(false);
        return response.ToPage();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<DnsRecord> ListAllAsync(
        DnsRecordListParameters? parameters = null,
        string? zoneId = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var zone = ResolveZoneId(zoneId);
        var page = 1;

        while (true)
        {
            var effective = new DnsRecordListParameters
            {
                Type = parameters?.Type,
                Name = parameters?.Name,
                Content = parameters?.Content,
                Order = parameters?.Order,
                Direction = parameters?.Direction,
                Proxied = parameters?.Proxied,
                Tag = parameters?.Tag,
                Tags = parameters?.Tags,
                TagMatch = parameters?.TagMatch,
                Match = parameters?.Match,
                Search = parameters?.Search,
                PerPage = parameters?.PerPage,
                Page = page,
            };

            var result = await ListAsync(effective, zone, cancellationToken).ConfigureAwait(false);
            foreach (var record in result.Items)
            {
                yield return record;
            }

            if (!result.HasMore || result.Items.Count == 0)
            {
                yield break;
            }

            page++;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsRecord>> FindAsync(
        string name,
        string? type = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        Require(name, nameof(name));

        var parameters = new DnsRecordListParameters
        {
            Name = name.Trim(),
            Type = type,
            PerPage = 100,
        };

        var page = await ListAsync(parameters, zoneId, cancellationToken).ConfigureAwait(false);
        return page.Items;
    }

    /// <inheritdoc />
    public async Task<DnsRecord?> GetAsync(string recordId, string? zoneId = null, CancellationToken cancellationToken = default)
    {
        var zone = ResolveZoneId(zoneId);
        var id = Require(recordId, nameof(recordId));

        // 403 as well as 404: Cloudflare reports both an unknown record id and a zone the token cannot
        // read as a 403, and a lookup answers null for either.
        var request = CloudflareRequest.Get($"{ZonePath(zone)}/dns_records/{CloudflareUrl.EncodeSegment(id)}")
            .Tolerate(System.Net.HttpStatusCode.NotFound, System.Net.HttpStatusCode.Forbidden);
        request.OperationName = "Get DNS Record";

        var response = await Transport.SendAsync<DnsRecord>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<DnsRecord> CreateAsync(DnsRecord record, string? zoneId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidateRecord(record, requireTypeAndName: true);

        var zone = ResolveZoneId(zoneId);
        var request = CloudflareRequest.Post($"{ZonePath(zone)}/dns_records", record);
        request.OperationName = "Create DNS Record";

        return await SendForRecordAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<DnsRecord> CreateAddressRecordAsync(
        string name,
        string content,
        bool proxied = true,
        int ttl = 1,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        Require(name, nameof(name));
        Require(content, nameof(content));

        var type = content.Contains(':') ? DnsRecordTypes.Aaaa : DnsRecordTypes.A;
        var record = new DnsRecord
        {
            Type = type,
            Name = name.Trim(),
            Content = content.Trim(),
            Proxied = proxied,
            Ttl = ttl,
        };

        return CreateAsync(record, zoneId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<DnsRecord> CreateCnameRecordAsync(
        string name,
        string target,
        bool proxied = true,
        int ttl = 1,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        Require(name, nameof(name));
        Require(target, nameof(target));

        var record = new DnsRecord
        {
            Type = DnsRecordTypes.Cname,
            Name = name.Trim(),
            Content = target.Trim(),
            Proxied = proxied,
            Ttl = ttl,
        };

        return CreateAsync(record, zoneId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<DnsRecord> CreateTxtRecordAsync(
        string name,
        string content,
        int ttl = 1,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        Require(name, nameof(name));
        Require(content, nameof(content));

        var record = new DnsRecord
        {
            Type = DnsRecordTypes.Txt,
            Name = name.Trim(),
            Content = content,
            Ttl = ttl,
            // TXT records cannot be proxied; sending proxied = true makes Cloudflare reject the call.
            Proxied = false,
        };

        return CreateAsync(record, zoneId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DnsRecord> UpdateAsync(
        string recordId,
        DnsRecord record,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var zone = ResolveZoneId(zoneId);
        var id = Require(recordId, nameof(recordId));

        // Identifiers in the path win; sending them in a PATCH body makes Cloudflare complain.
        record.Id = null;
        record.ZoneId = null;
        record.ZoneName = null;

        var request = CloudflareRequest.Patch($"{ZonePath(zone)}/dns_records/{CloudflareUrl.EncodeSegment(id)}", record);
        request.OperationName = "Update DNS Record";

        return await SendForRecordAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DnsRecord> ReplaceAsync(
        string recordId,
        DnsRecord record,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidateRecord(record, requireTypeAndName: true);

        var zone = ResolveZoneId(zoneId);
        var id = Require(recordId, nameof(recordId));

        // Identifiers in the path win; sending them in a PUT body makes Cloudflare complain.
        record.Id = null;
        record.ZoneId = null;
        record.ZoneName = null;

        var request = CloudflareRequest.Put($"{ZonePath(zone)}/dns_records/{CloudflareUrl.EncodeSegment(id)}", record);
        request.OperationName = "Replace DNS Record";

        return await SendForRecordAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string recordId, string? zoneId = null, CancellationToken cancellationToken = default)
    {
        var zone = ResolveZoneId(zoneId);
        var id = Require(recordId, nameof(recordId));

        var request = CloudflareRequest.Delete($"{ZonePath(zone)}/dns_records/{CloudflareUrl.EncodeSegment(id)}");
        request.OperationName = "Delete DNS Record";

        await SendVoidAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateRecord(DnsRecord record, bool requireTypeAndName)
    {
        var problems = new List<string>();

        if (requireTypeAndName)
        {
            if (string.IsNullOrWhiteSpace(record.Type))
            {
                problems.Add("A DNS record needs a Type (for example DnsRecordTypes.A).");
            }

            if (string.IsNullOrWhiteSpace(record.Name))
            {
                problems.Add("A DNS record needs a Name.");
            }
        }

        var hasContent = !string.IsNullOrWhiteSpace(record.Content);
        var hasData = record.Data is not null;

        if (requireTypeAndName && !hasContent && !hasData)
        {
            problems.Add("A DNS record needs either Content or Data. Structured types (SRV, LOC, CAA) use Data.");
        }

        if (hasContent && hasData)
        {
            problems.Add("Cloudflare rejects a DNS record that sets both Content and Data.");
        }

        if (problems.Count > 0)
        {
            throw new Exceptions.CloudflareConfigurationException(problems);
        }
    }

    /// <inheritdoc />
    public async Task<string> ExportAsync(string? zoneId = null, CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get($"{ZonePath(ResolveZoneId(zoneId))}/dns_records/export");
        request.OperationName = "Export DNS Records";

        // Not an envelope: the endpoint streams the BIND file back as text/plain.
        var raw = await Transport.SendRawAsync(request, cancellationToken).ConfigureAwait(false);
        return raw.Body;
    }

    /// <inheritdoc />
    public async Task<DnsRecordsImportResult?> ImportAsync(
        string bindContents,
        bool? proxied = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(bindContents);

        var multipart = new MultipartRequestBody()
            .AddFile("file", System.Text.Encoding.UTF8.GetBytes(bindContents), "text/plain", "zone.txt");

        if (proxied.HasValue)
        {
            // The API documents this as the strings "true"/"false", not a JSON boolean.
            multipart.AddField("proxied", proxied.Value ? "true" : "false");
        }

        var request = new CloudflareRequest(HttpMethod.Post, $"{ZonePath(ResolveZoneId(zoneId))}/dns_records/import")
        {
            Body = multipart,
            OperationName = "Import DNS Records",
        };

        var response = await Transport.SendAsync<DnsRecordsImportResult>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<JsonElement?> BatchAsync(
        DnsRecordBatch batch,
        bool? includeShadowMetadata = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        batch.EnsureValid();

        var request = CloudflareRequest.Post(
            $"{ZonePath(ResolveZoneId(zoneId))}/dns_records/batch", batch);
        request.OperationName = "Batch DNS Records";
        request.Query.Add("include_shadow_metadata", includeShadowMetadata);

        var response = await Transport.SendAsync<JsonElement?>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<CloudflarePage<DnsRecord>> ListScannedRecordsAsync(
        int? perPage = null,
        string? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get($"{ZonePath(ResolveZoneId(zoneId))}/dns_records/scan/review");
        request.OperationName = "List Scanned DNS Records";
        request.Query.Add("per_page", perPage);

        var response = await Transport.SendAsync<List<DnsRecord>>(request, cancellationToken).ConfigureAwait(false);
        return response.ToPage();
    }

    private async Task<DnsRecord> SendForRecordAsync(CloudflareRequest request, CancellationToken cancellationToken)
    {
        var response = await Transport.SendAsync<DnsRecord>(request, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }
}
