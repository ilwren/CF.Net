using System.Runtime.CompilerServices;
using CFNet.Http;
using CFNet.Models.D1;

namespace CFNet.Services;

/// <summary>
/// Operations on D1 databases: <c>/accounts/{account_id}/d1/database</c>, plus queries and backups.
/// </summary>
public interface ID1DatabaseService
{
    /// <summary>Lists D1 databases one page at a time.</summary>
    /// <param name="parameters">Optional filters and paging.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CloudflarePage<D1Database>> ListAsync(
        D1DatabaseListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Enumerates every D1 database, following page-number pagination automatically.</summary>
    /// <param name="parameters">Optional filters; <see cref="D1DatabaseListParameters.Page"/> is ignored.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<D1Database> ListAllAsync(
        D1DatabaseListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Looks a database up by name and returns its id.</summary>
    /// <param name="name">Database name.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The database, or <c>null</c> when no database has that name.</returns>
    Task<D1Database?> GetByNameAsync(string name, string? accountId = null, CancellationToken cancellationToken = default);

    /// <summary>Fetches one database by id.</summary>
    /// <param name="databaseId">Database id (UUID).</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The database, or <c>null</c> when it does not exist.</returns>
    Task<D1Database?> GetAsync(string databaseId, string? accountId = null, CancellationToken cancellationToken = default);

    /// <summary>Creates a database.</summary>
    /// <param name="request">Name and optional location hint.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<D1Database> CreateAsync(CreateD1DatabaseRequest request, string? accountId = null, CancellationToken cancellationToken = default);

    /// <summary>Creates a database with the given name.</summary>
    /// <param name="name">Database name.</param>
    /// <param name="primaryLocationHint">Optional preferred primary location.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<D1Database> CreateAsync(string name, string? primaryLocationHint = null, string? accountId = null, CancellationToken cancellationToken = default);

    /// <summary>Deletes a database and all of its data.</summary>
    /// <param name="databaseId">Database id (UUID).</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(string databaseId, string? accountId = null, CancellationToken cancellationToken = default);

    /// <summary>Runs a single SQL statement with bound parameters.</summary>
    /// <param name="databaseId">Database id (UUID).</param>
    /// <param name="sql">The SQL statement.</param>
    /// <param name="parameters">Values bound to the <c>?</c> placeholders, in order.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The single result element Cloudflare returns for a one-statement query.</returns>
    Task<D1QueryResult> QueryAsync(
        string databaseId,
        string sql,
        IReadOnlyList<object?>? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Runs several SQL statements in one call.</summary>
    /// <param name="databaseId">Database id (UUID).</param>
    /// <param name="queries">The statements, in order.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One result element per statement.</returns>
    Task<IReadOnlyList<D1QueryResult>> BatchAsync(
        string databaseId,
        IEnumerable<D1Query> queries,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Sends a pre-built query request.</summary>
    /// <param name="databaseId">Database id (UUID).</param>
    /// <param name="request">The request; validated locally first.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<D1QueryResult>> ExecuteAsync(
        string databaseId,
        D1QueryRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists available backups.</summary>
    /// <param name="databaseId">Database id (UUID).</param>
    /// <param name="parameters">Optional paging.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CloudflarePage<D1Backup>> ListBackupsAsync(
        string databaseId,
        D1BackupListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches one backup.</summary>
    /// <param name="databaseId">Database id (UUID).</param>
    /// <param name="backupId">Backup id.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<D1Backup?> GetBackupAsync(
        string databaseId,
        string backupId,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Restores a backup over the live database. This is destructive.</summary>
    /// <param name="databaseId">Database id (UUID).</param>
    /// <param name="backupId">Backup id.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RestoreBackupAsync(string databaseId, string backupId, string? accountId = null, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class D1DatabaseService : CloudflareService, ID1DatabaseService
{
    /// <summary>Creates the service.</summary>
    /// <param name="transport">The transport to execute requests through.</param>
    public D1DatabaseService(ICloudflareTransport transport)
        : base(transport)
    {
    }

    /// <inheritdoc />
    public async Task<CloudflarePage<D1Database>> ListAsync(
        D1DatabaseListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var account = ResolveAccountId(accountId);
        var request = Apply(CloudflareRequest.Get($"{AccountPath(account)}/d1/database"), parameters);
        request.OperationName = "List D1 Databases";

        var response = await Transport.SendAsync<List<D1Database>?>(request, cancellationToken).ConfigureAwait(false);
        return response.ToPage();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<D1Database> ListAllAsync(
        D1DatabaseListParameters? parameters = null,
        string? accountId = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var account = ResolveAccountId(accountId);
        var page = 1;

        while (true)
        {
            var effective = new D1DatabaseListParameters
            {
                Name = parameters?.Name,
                PerPage = parameters?.PerPage,
                Page = page,
            };

            var result = await ListAsync(effective, account, cancellationToken).ConfigureAwait(false);
            foreach (var database in result.Items)
            {
                yield return database;
            }

            // D1's list response does not always carry result_info, so also stop on a short page.
            if (result.Items.Count == 0 || !result.HasMore)
            {
                yield break;
            }

            page++;
        }
    }

    /// <inheritdoc />
    public async Task<D1Database?> GetByNameAsync(string name, string? accountId = null, CancellationToken cancellationToken = default)
    {
        Require(name, nameof(name));

        var page = await ListAsync(
            new D1DatabaseListParameters { Name = name.Trim(), PerPage = 100 },
            accountId,
            cancellationToken).ConfigureAwait(false);

        return page.Items.FirstOrDefault(d => string.Equals(d.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task<D1Database?> GetAsync(string databaseId, string? accountId = null, CancellationToken cancellationToken = default)
    {
        var path = DatabasePath(ResolveAccountId(accountId), databaseId);
        var request = CloudflareRequest.Get(path).Tolerate(System.Net.HttpStatusCode.NotFound);
        request.OperationName = "Get D1 Database";

        var response = await Transport.SendAsync<D1Database?>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<D1Database> CreateAsync(
        CreateD1DatabaseRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new Exceptions.CloudflareConfigurationException("A D1 database name is required.");
        }

        var account = ResolveAccountId(accountId);
        var httpRequest = CloudflareRequest.Post($"{AccountPath(account)}/d1/database", request);
        httpRequest.OperationName = "Create D1 Database";

        var response = await Transport.SendAsync<D1Database?>(httpRequest, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public Task<D1Database> CreateAsync(
        string name,
        string? primaryLocationHint = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        Require(name, nameof(name));
        return CreateAsync(new CreateD1DatabaseRequest(name.Trim(), primaryLocationHint), accountId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string databaseId, string? accountId = null, CancellationToken cancellationToken = default)
    {
        var path = DatabasePath(ResolveAccountId(accountId), databaseId);
        var request = CloudflareRequest.Delete(path);
        request.OperationName = "Delete D1 Database";

        await SendVoidAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<D1QueryResult> QueryAsync(
        string databaseId,
        string sql,
        IReadOnlyList<object?>? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        Require(sql, nameof(sql));

        var results = await ExecuteAsync(
            databaseId,
            D1QueryRequest.Single(sql, parameters),
            accountId,
            cancellationToken).ConfigureAwait(false);

        return results.FirstOrDefault() ?? new D1QueryResult { Success = false };
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<D1QueryResult>> BatchAsync(
        string databaseId,
        IEnumerable<D1Query> queries,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queries);

        var list = queries.ToList();
        if (list.Count == 0)
        {
            throw new Exceptions.CloudflareConfigurationException("A D1 batch needs at least one query.");
        }

        foreach (var query in list)
        {
            if (string.IsNullOrWhiteSpace(query.Sql))
            {
                throw new Exceptions.CloudflareConfigurationException("Every D1 batch entry needs a SQL statement.");
            }
        }

        return ExecuteAsync(databaseId, D1QueryRequest.Batch(list), accountId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<D1QueryResult>> ExecuteAsync(
        string databaseId,
        D1QueryRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        var path = DatabasePath(ResolveAccountId(accountId), databaseId);
        var httpRequest = CloudflareRequest.Post($"{path}/query", request);
        httpRequest.OperationName = "Query D1 Database";

        var response = await Transport.SendAsync<List<D1QueryResult>?>(httpRequest, cancellationToken).ConfigureAwait(false);
        return response.Result ?? Array.Empty<D1QueryResult>();
    }

    /// <inheritdoc />
    public async Task<CloudflarePage<D1Backup>> ListBackupsAsync(
        string databaseId,
        D1BackupListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var path = DatabasePath(ResolveAccountId(accountId), databaseId);
        var request = Apply(CloudflareRequest.Get($"{path}/backup"), parameters);
        request.OperationName = "List D1 Backups";

        var response = await Transport.SendAsync<List<D1Backup>?>(request, cancellationToken).ConfigureAwait(false);
        return response.ToPage();
    }

    /// <inheritdoc />
    public async Task<D1Backup?> GetBackupAsync(
        string databaseId,
        string backupId,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var path = DatabasePath(ResolveAccountId(accountId), databaseId);
        var id = Require(backupId, nameof(backupId));

        var request = CloudflareRequest
            .Get($"{path}/backup/{CloudflareUrl.EncodeSegment(id)}")
            .Tolerate(System.Net.HttpStatusCode.NotFound);
        request.OperationName = "Get D1 Backup";

        var response = await Transport.SendAsync<D1Backup?>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task RestoreBackupAsync(
        string databaseId,
        string backupId,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var path = DatabasePath(ResolveAccountId(accountId), databaseId);
        var id = Require(backupId, nameof(backupId));

        var request = CloudflareRequest.Post($"{path}/backup/{CloudflareUrl.EncodeSegment(id)}/restore");
        request.OperationName = "Restore D1 Backup";

        await SendVoidAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static string DatabasePath(string accountId, string databaseId) =>
        $"{AccountPath(accountId)}/d1/database/{CloudflareUrl.EncodeSegment(Require(databaseId, nameof(databaseId)))}";
}
