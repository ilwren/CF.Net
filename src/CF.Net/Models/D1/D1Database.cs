using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Http;

namespace CFNet.Models.D1;

/// <summary>
/// A D1 database, from <c>GET /accounts/{account_id}/d1/database</c> and
/// <c>GET /accounts/{account_id}/d1/database/{database_id}</c>.
/// </summary>
/// <remarks>
/// The list endpoint returns a slimmed down projection (name, uuid, created_at, jurisdiction);
/// the single-database endpoint additionally reports size and table counts. Every member is nullable
/// so one type covers both.
/// </remarks>
public sealed class D1Database
{
    /// <summary>
    /// Database identifier (a UUID). This is the <c>database_id</c> the query and backup endpoints need.
    /// </summary>
    [JsonPropertyName("uuid")]
    public string? Uuid { get; set; }

    /// <summary>Database name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Schema version.</summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>Number of tables.</summary>
    [JsonPropertyName("num_tables")]
    public int? NumTables { get; set; }

    /// <summary>Total size of the database in bytes.</summary>
    [JsonPropertyName("file_size")]
    public long? FileSize { get; set; }

    /// <summary>Creation timestamp.</summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>
    /// Data residency restriction. When present it overrides the location hint: <c>eu</c>, <c>us</c>
    /// or <c>fedramp</c>.
    /// </summary>
    [JsonPropertyName("jurisdiction")]
    public string? Jurisdiction { get; set; }

    /// <summary>Preferred primary location, e.g. <c>enam</c>.</summary>
    [JsonPropertyName("primary_location_hint")]
    public string? PrimaryLocationHint { get; set; }

    /// <summary>Where the database primary currently lives.</summary>
    [JsonPropertyName("database_location")]
    public string? DatabaseLocation { get; set; }

    /// <summary>Storage engine version.</summary>
    [JsonPropertyName("storage_version")]
    public string? StorageVersion { get; set; }

    /// <summary>Read replication configuration, when the database reports one.</summary>
    [JsonPropertyName("read_replication")]
    public D1ReadReplication? ReadReplication { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"{Name} ({Uuid})";
}

/// <summary>Request body of <c>POST /accounts/{account_id}/d1/database</c>.</summary>
public sealed class CreateD1DatabaseRequest
{
    /// <summary>Creates a request.</summary>
    /// <param name="name">Database name.</param>
    /// <param name="primaryLocationHint">Optional preferred primary location.</param>
    public CreateD1DatabaseRequest(string name, string? primaryLocationHint = null)
    {
        Name = name;
        PrimaryLocationHint = primaryLocationHint;
    }

    /// <summary>Parameterless constructor for object initialisers and deserialisation.</summary>
    [System.Text.Json.Serialization.JsonConstructor]
    public CreateD1DatabaseRequest()
    {
    }

    /// <summary>Database name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Preferred primary location: <c>wnam</c>, <c>enam</c>, <c>weur</c>, <c>eeur</c>, <c>apac</c> or <c>oc</c>.
    /// Best effort, not a guarantee.
    /// </summary>
    [JsonPropertyName("primary_location_hint")]
    public string? PrimaryLocationHint { get; set; }
}

/// <summary>Query filters for <c>GET /accounts/{account_id}/d1/database</c>.</summary>
public sealed class D1DatabaseListParameters : CloudflarePagedParameters
{
    /// <summary>Database name to search for.</summary>
    public string? Name { get; set; }

    /// <inheritdoc />
    public override void Apply(QueryStringBuilder query)
    {
        base.Apply(query);
        query.AddIfNotEmpty("name", Name);
    }
}

/// <summary>One SQL statement plus its bound parameters.</summary>
public sealed class D1Query
{
    /// <summary>Creates a query.</summary>
    /// <param name="sql">The SQL statement. Multiple statements separated by semicolons run as a batch.</param>
    /// <param name="parameters">Values bound to the <c>?</c> placeholders, in order.</param>
    public D1Query(string sql, IReadOnlyList<object?>? parameters = null)
    {
        Sql = sql;
        Params = parameters;
    }

    /// <summary>Parameterless constructor for deserialisation.</summary>
    [System.Text.Json.Serialization.JsonConstructor]
    public D1Query()
    {
    }

    /// <summary>The SQL statement.</summary>
    [JsonPropertyName("sql")]
    public string? Sql { get; set; }

    /// <summary>Bound parameters. Cloudflare documents these as strings but accepts JSON scalars.</summary>
    [JsonPropertyName("params")]
    public IReadOnlyList<object?>? Params { get; set; }
}

/// <summary>Request body of <c>POST .../d1/database/{database_id}/query</c>.</summary>
/// <remarks>
/// Cloudflare accepts exactly one of two shapes: a single query (<c>{ sql, params }</c>) or a batch
/// (<c>{ batch: [ { sql, params }, ... ] }</c>). <see cref="Single"/> and <see cref="BatchOf"/> build the
/// right one; setting both is rejected by <see cref="Validate"/>.
/// </remarks>
public sealed class D1QueryRequest
{
    /// <summary>Creates a single statement request.</summary>
    /// <param name="sql">The SQL statement.</param>
    /// <param name="parameters">Values bound to the <c>?</c> placeholders.</param>
    public static D1QueryRequest Single(string sql, IReadOnlyList<object?>? parameters = null) =>
        new() { Sql = sql, Params = parameters };

    /// <summary>Creates a batch request.</summary>
    /// <param name="queries">The statements to run, in order.</param>
    public static D1QueryRequest BatchOf(IEnumerable<D1Query> queries) =>
        new() { Batch = queries?.ToList() ?? throw new ArgumentNullException(nameof(queries)) };

    /// <summary>The SQL statement, for the single query shape.</summary>
    [JsonPropertyName("sql")]
    public string? Sql { get; set; }

    /// <summary>Bound parameters, for the single query shape.</summary>
    [JsonPropertyName("params")]
    public IReadOnlyList<object?>? Params { get; set; }

    /// <summary>Statements to run in order, for the batch shape.</summary>
    [JsonPropertyName("batch")]
    public List<D1Query>? Batch { get; set; }

    /// <summary>
    /// Ensures exactly one of the two accepted shapes is populated.
    /// </summary>
    /// <exception cref="Exceptions.CloudflareConfigurationException">Both or neither shape is set.</exception>
    public void Validate()
    {
        var hasSingle = !string.IsNullOrWhiteSpace(Sql);
        var hasBatch = Batch is { Count: > 0 };

        if (hasSingle && hasBatch)
        {
            throw new Exceptions.CloudflareConfigurationException(
                "A D1 query request must use either { sql, params } or { batch: [...] }, not both.");
        }

        if (!hasSingle && !hasBatch)
        {
            throw new Exceptions.CloudflareConfigurationException(
                "A D1 query request needs either Sql or at least one Batch entry.");
        }
    }
}

/// <summary>
/// One element of the <c>result</c> array returned by the D1 query endpoint. A batch produces one
/// element per statement.
/// </summary>
public sealed class D1QueryResult
{
    /// <summary>Rows returned by the statement, each as a JSON object.</summary>
    [JsonPropertyName("results")]
    public List<JsonElement>? Results { get; set; }

    /// <summary>Whether the statement succeeded.</summary>
    [JsonPropertyName("success")]
    public bool? Success { get; set; }

    /// <summary>Execution statistics.</summary>
    [JsonPropertyName("meta")]
    public D1QueryMeta? Meta { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Number of rows in <see cref="Results"/>.</summary>
    [JsonIgnore]
    public int RowCount => Results?.Count ?? 0;

    /// <summary>
    /// Projects every row onto a typed model. Column names are matched using the same snake_case
    /// policy as the rest of the library, and matching is case insensitive.
    /// </summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="options">Optional serializer options; defaults to the shared library options.</param>
    public IReadOnlyList<T> Rows<T>(JsonSerializerOptions? options = null)
    {
        if (Results is not { Count: > 0 } rows)
        {
            return Array.Empty<T>();
        }

        options ??= Serialization.CloudflareJson.Default;
        var result = new List<T>(rows.Count);
        foreach (var row in rows)
        {
            var item = row.Deserialize<T>(options);
            if (item is not null)
            {
                result.Add(item);
            }
        }

        return result;
    }

    /// <summary>Reads one column of the first row, or <c>default</c> when there is no row.</summary>
    /// <typeparam name="T">The column type.</typeparam>
    /// <param name="columnName">Column name.</param>
    public T? FirstColumn<T>(string columnName)
    {
        if (Results is not { Count: > 0 } rows)
        {
            return default;
        }

        var row = rows[0];
        if (row.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        foreach (var property in row.EnumerateObject())
        {
            if (string.Equals(property.Name, columnName, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.Deserialize<T>(Serialization.CloudflareJson.Default);
            }
        }

        return default;
    }
}

/// <summary>Execution statistics reported by D1 alongside query results.</summary>
public sealed class D1QueryMeta
{
    /// <summary>Whether the statement changed the database.</summary>
    [JsonPropertyName("changed_db")]
    public bool? ChangedDb { get; set; }

    /// <summary>Rough number of rows modified.</summary>
    [JsonPropertyName("changes")]
    public int? Changes { get; set; }

    /// <summary>SQL execution time inside the database, in milliseconds.</summary>
    [JsonPropertyName("duration")]
    public double? Duration { get; set; }

    /// <summary>Row id of the last inserted row.</summary>
    [JsonPropertyName("last_row_id")]
    public long? LastRowId { get; set; }

    /// <summary>Number of rows read, including index lookups.</summary>
    [JsonPropertyName("rows_read")]
    public int? RowsRead { get; set; }

    /// <summary>Number of rows written.</summary>
    [JsonPropertyName("rows_written")]
    public int? RowsWritten { get; set; }

    /// <summary>Three letter code of the colo that served the query.</summary>
    [JsonPropertyName("served_by_colo")]
    public string? ServedByColo { get; set; }

    /// <summary>Whether the primary instance served the query.</summary>
    [JsonPropertyName("served_by_primary")]
    public bool? ServedByPrimary { get; set; }

    /// <summary>Region that served the query: <c>WNAM</c>, <c>ENAM</c>, <c>WEUR</c>, <c>EEUR</c>, <c>APAC</c>, <c>OC</c>.</summary>
    [JsonPropertyName("served_by_region")]
    public string? ServedByRegion { get; set; }

    /// <summary>Database size in bytes after the statement committed.</summary>
    [JsonPropertyName("size_after")]
    public long? SizeAfter { get; set; }

    /// <summary>Detailed timings.</summary>
    [JsonPropertyName("timings")]
    public D1QueryTimings? Timings { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>Detailed query timings.</summary>
public sealed class D1QueryTimings
{
    /// <summary>SQL execution duration in milliseconds.</summary>
    [JsonPropertyName("sql_duration_ms")]
    public double? SqlDurationMs { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>A point in time backup of a D1 database.</summary>
public sealed class D1Backup
{
    /// <summary>Backup identifier.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Database the backup belongs to.</summary>
    [JsonPropertyName("database_id")]
    public string? DatabaseId { get; set; }

    /// <summary>Database name.</summary>
    [JsonPropertyName("database_name")]
    public string? DatabaseName { get; set; }

    /// <summary>Creation timestamp.</summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Backup state, e.g. <c>complete</c>, <c>pending</c>, <c>failed</c>.</summary>
    [JsonPropertyName("state")]
    public string? State { get; set; }

    /// <summary>Sequential backup number.</summary>
    [JsonPropertyName("number")]
    public int? Number { get; set; }

    /// <summary>Size of the backup in bytes.</summary>
    [JsonPropertyName("size")]
    public long? Size { get; set; }

    /// <summary>Short lived download URL.</summary>
    [JsonPropertyName("download_url")]
    public string? DownloadUrl { get; set; }

    /// <summary>Storage backend details.</summary>
    [JsonPropertyName("storage")]
    public JsonElement? Storage { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>Query filters for the D1 backups list endpoint.</summary>
public sealed class D1BackupListParameters : CloudflarePagedParameters
{
}


/// <summary>
/// Read replication for a D1 database: <c>auto</c> lets Cloudflare place replicas around the world,
/// <c>disabled</c> removes them, which can take a few hours.
/// </summary>
public sealed class D1ReadReplication
{
    /// <summary>The replication mode. See <see cref="D1ReadReplicationModes"/>.</summary>
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    /// <summary>Builds a replication configuration.</summary>
    /// <param name="mode">One of <see cref="D1ReadReplicationModes"/>.</param>
    public static D1ReadReplication WithMode(string mode) => new() { Mode = mode };

    /// <summary>True when the mode is <c>auto</c>.</summary>
    public bool IsAuto => string.Equals(Mode, D1ReadReplicationModes.Auto, StringComparison.OrdinalIgnoreCase);
}

/// <summary>The modes <see cref="D1ReadReplication.Mode"/> accepts.</summary>
public static class D1ReadReplicationModes
{
    /// <summary>Create replicas and let D1 place them around the world.</summary>
    public const string Auto = "auto";

    /// <summary>Use no replicas. Removing existing ones can take a few hours.</summary>
    public const string Disabled = "disabled";
}

/// <summary>
/// Body of both <c>PUT</c> and <c>PATCH /accounts/{account_id}/d1/database/{database_id}</c>, which is
/// the only thing either of them accepts.
/// </summary>
/// <remarks>
/// Renaming a database is not part of this endpoint: the published schema for the PUT and the PATCH both
/// take <c>read_replication</c> and nothing else, so <see cref="D1Database.Name"/> cannot be changed
/// through them.
/// </remarks>
public sealed class UpdateD1DatabaseRequest
{
    /// <summary>The read replication configuration to apply.</summary>
    [JsonPropertyName("read_replication")]
    public D1ReadReplication? ReadReplication { get; set; }

    /// <summary>Builds the body that sets a replication mode.</summary>
    /// <param name="mode">One of <see cref="D1ReadReplicationModes"/>.</param>
    public static UpdateD1DatabaseRequest ForReadReplication(string mode) =>
        new() { ReadReplication = D1ReadReplication.WithMode(mode) };

    /// <summary>
    /// Rejects a body Cloudflare would refuse: the PUT requires a replication configuration, and the mode
    /// has to be one it recognises.
    /// </summary>
    /// <exception cref="Exceptions.CloudflareConfigurationException">The request cannot be sent as it is.</exception>
    public void EnsureValid()
    {
        var mode = ReadReplication?.Mode;
        if (string.IsNullOrWhiteSpace(mode))
        {
            throw new Exceptions.CloudflareConfigurationException(
                $"'{nameof(ReadReplication)}.{nameof(D1ReadReplication.Mode)}' is required: it is the only thing this endpoint accepts.");
        }

        if (!string.Equals(mode, D1ReadReplicationModes.Auto, StringComparison.Ordinal) &&
            !string.Equals(mode, D1ReadReplicationModes.Disabled, StringComparison.Ordinal))
        {
            throw new Exceptions.CloudflareConfigurationException(
                $"'{nameof(D1ReadReplication.Mode)}' must be '{D1ReadReplicationModes.Auto}' or '{D1ReadReplicationModes.Disabled}', not '{mode}'.");
        }
    }
}
