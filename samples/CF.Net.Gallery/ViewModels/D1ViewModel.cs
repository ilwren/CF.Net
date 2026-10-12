using CFNet.Models.D1;
using CFNet.Serialization;
using System.Text.Json;

namespace CFNet.Gallery.ViewModels;

/// <summary>
/// D1 databases, SQL execution and backups.
/// </summary>
public sealed class D1ViewModel : ModuleViewModelBase
{
    private string _listPerPage = "20";
    private string _listName = string.Empty;
    private string _databaseId = string.Empty;
    private string _databaseName = string.Empty;
    private string _locationHint = string.Empty;
    private string _sql = "SELECT 1 AS answer";
    private string _parametersJson = string.Empty;
    private string _batchJson = string.Empty;
    private string _backupId = string.Empty;
    private string _rawJson = string.Empty;
    private string _dumpTables = string.Empty;
    private bool _dumpNoData;
    private bool _dumpNoSchema;
    private string _exportBookmark = string.Empty;
    private string _etag = string.Empty;
    private string _importFilename = "dump.sql";
    private string _importBookmark = string.Empty;
    private string _readReplicationMode = D1ReadReplicationModes.Auto;

    /// <summary>Creates the page.</summary>
    /// <param name="gallery">Owning shell.</param>
    public D1ViewModel(GalleryViewModel gallery)
        : base(gallery)
    {
        ListCommand = new RelayCommand(ListAsync);
        GetCommand = new RelayCommand(GetAsync);
        GetByNameCommand = new RelayCommand(GetByNameAsync);
        CreateCommand = new RelayCommand(CreateAsync);
        DeleteCommand = new RelayCommand(DeleteAsync);
        QueryCommand = new RelayCommand(QueryAsync);
        BatchCommand = new RelayCommand(BatchAsync);
        ListBackupsCommand = new RelayCommand(ListBackupsAsync);
        GetBackupCommand = new RelayCommand(GetBackupAsync);
        RestoreBackupCommand = new RelayCommand(RestoreBackupAsync);
        QueryRawCommand = new RelayCommand(QueryRawAsync);
        BatchRawCommand = new RelayCommand(BatchRawAsync);
        ExecuteCommand = new RelayCommand(ExecuteAsync);
        ExecuteRawCommand = new RelayCommand(ExecuteRawAsync);
        ExportCommand = new RelayCommand(ExportAsync);
        ExportAndWaitCommand = new RelayCommand(ExportAndWaitAsync);
        ComputeEtagCommand = new RelayCommand(ComputeEtagAsync);
        ImportInitCommand = new RelayCommand(ImportInitAsync);
        ImportIngestCommand = new RelayCommand(ImportIngestAsync);
        ImportPollCommand = new RelayCommand(ImportPollAsync);
        ImportWaitCommand = new RelayCommand(ImportWaitAsync);
        ReadReplicationCommand = new RelayCommand(SetReadReplicationAsync);
        UndeleteCommand = new RelayCommand(UndeleteAsync);
    }

    /// <inheritdoc />
    public override string Title => "D1";

    /// <inheritdoc />
    public override string Description =>
        "SQLite databases: metadata, single statements, batches and point-in-time backups.";

    /// <summary>Items per page.</summary>
    public string ListPerPage { get => _listPerPage; set => SetProperty(ref _listPerPage, value); }

    /// <summary>Name filter (substring).</summary>
    public string ListName { get => _listName; set => SetProperty(ref _listName, value); }

    /// <summary>Database uuid every single-database call operates on.</summary>
    public string DatabaseId { get => _databaseId; set => SetProperty(ref _databaseId, value); }

    /// <summary>Database name for the create and get-by-name calls.</summary>
    public string DatabaseName { get => _databaseName; set => SetProperty(ref _databaseName, value); }

    /// <summary>Optional primary location hint, e.g. <c>weur</c>.</summary>
    public string LocationHint { get => _locationHint; set => SetProperty(ref _locationHint, value); }

    /// <summary>SQL statement to run.</summary>
    public string Sql { get => _sql; set => SetProperty(ref _sql, value); }

    /// <summary>Bound parameters as a JSON array, e.g. <c>["alpha", 2]</c>.</summary>
    public string ParametersJson { get => _parametersJson; set => SetProperty(ref _parametersJson, value); }

    /// <summary>
    /// Batch as a JSON array, e.g.
    /// <c>[{"sql":"CREATE TABLE t (id INTEGER)"},{"sql":"INSERT INTO t VALUES (?)","params":[1]}]</c>.
    /// </summary>
    public string BatchJson { get => _batchJson; set => SetProperty(ref _batchJson, value); }

    /// <summary>Backup id for the get and restore calls.</summary>
    public string BackupId { get => _backupId; set => SetProperty(ref _backupId, value); }

    /// <summary>Lists databases.</summary>
    public RelayCommand ListCommand { get; }

    /// <summary>Gets one database by uuid.</summary>
    public RelayCommand GetCommand { get; }

    /// <summary>Looks a database up by name.</summary>
    public RelayCommand GetByNameCommand { get; }

    /// <summary>Creates a database.</summary>
    public RelayCommand CreateCommand { get; }

    /// <summary>Deletes a database.</summary>
    public RelayCommand DeleteCommand { get; }

    /// <summary>Runs one statement.</summary>
    public RelayCommand QueryCommand { get; }

    /// <summary>Runs a batch transactionally.</summary>
    public RelayCommand BatchCommand { get; }

    /// <summary>Lists backups.</summary>
    public RelayCommand ListBackupsCommand { get; }

    /// <summary>Gets one backup.</summary>
    public RelayCommand GetBackupCommand { get; }

    /// <summary>Restores a backup.</summary>
    public RelayCommand RestoreBackupCommand { get; }

    /// <summary>Runs the SQL box against the <c>/raw</c> endpoint.</summary>
    public RelayCommand QueryRawCommand { get; }

    /// <summary>Runs the batch box against the <c>/raw</c> endpoint.</summary>
    public RelayCommand BatchRawCommand { get; }

    /// <summary>Sends the request-body box to <c>/query</c>.</summary>
    public RelayCommand ExecuteCommand { get; }

    /// <summary>Sends the request-body box to <c>/raw</c>.</summary>
    public RelayCommand ExecuteRawCommand { get; }

    /// <summary>Starts an export, or polls the one the bookmark names.</summary>
    public RelayCommand ExportCommand { get; }

    /// <summary>Starts an export and polls it to completion.</summary>
    public RelayCommand ExportAndWaitCommand { get; }

    /// <summary>Fills the etag box from the SQL box.</summary>
    public RelayCommand ComputeEtagCommand { get; }

    /// <summary>Sends the import <c>init</c> action.</summary>
    public RelayCommand ImportInitCommand { get; }

    /// <summary>Sends the import <c>ingest</c> action.</summary>
    public RelayCommand ImportIngestCommand { get; }

    /// <summary>Sends the import <c>poll</c> action once.</summary>
    public RelayCommand ImportPollCommand { get; }

    /// <summary>Polls the import until it stops.</summary>
    public RelayCommand ImportWaitCommand { get; }

    /// <summary>Sets the read-replication mode.</summary>
    public RelayCommand ReadReplicationCommand { get; }

    /// <summary>Undeletes a database.</summary>
    public RelayCommand UndeleteCommand { get; }

    /// <summary>Request body for the two <c>Execute</c> calls, as JSON.</summary>
    public string RawJson { get => _rawJson; set => SetProperty(ref _rawJson, value); }

    /// <summary>Tables to restrict a dump to, one per line; blank means every table.</summary>
    public string DumpTables { get => _dumpTables; set => SetProperty(ref _dumpTables, value); }

    /// <summary>Leave the row contents out of the dump.</summary>
    public bool DumpNoData { get => _dumpNoData; set => SetProperty(ref _dumpNoData, value); }

    /// <summary>Leave the table definitions out of the dump.</summary>
    public bool DumpNoSchema { get => _dumpNoSchema; set => SetProperty(ref _dumpNoSchema, value); }

    /// <summary>The <c>at_bookmark</c> of the export being polled; blank starts a new one.</summary>
    public string ExportBookmark { get => _exportBookmark; set => SetProperty(ref _exportBookmark, value); }

    /// <summary>MD5 of the SQL file, which is how D1 recognises an upload.</summary>
    public string Etag { get => _etag; set => SetProperty(ref _etag, value); }

    /// <summary>File name reported with the <c>ingest</c> action.</summary>
    public string ImportFilename { get => _importFilename; set => SetProperty(ref _importFilename, value); }

    /// <summary>The <c>final_bookmark</c> the import is being polled with.</summary>
    public string ImportBookmark { get => _importBookmark; set => SetProperty(ref _importBookmark, value); }

    /// <summary>Read-replication mode to send.</summary>
    public string ReadReplicationMode { get => _readReplicationMode; set => SetProperty(ref _readReplicationMode, value); }

    /// <summary>The read-replication modes D1 accepts.</summary>
    public IReadOnlyList<string> ReadReplicationModes { get; } = new[]
    {
        D1ReadReplicationModes.Auto,
        D1ReadReplicationModes.Disabled,
    };

    private Task ListAsync() => RunAsync("List D1 databases", async () =>
        await Client.D1.ListAsync(new D1DatabaseListParameters
        {
            PerPage = OptionalInt(ListPerPage),
            Name = Blank(ListName),
        }));

    private Task GetAsync() => RunAsync("Get D1 database", async () =>
        await Client.D1.GetAsync(Database()));

    private Task GetByNameAsync() => RunAsync("Get D1 database by name", async () =>
        await Client.D1.GetByNameAsync(Name()));

    private Task CreateAsync() => RunAsync("Create D1 database", async () =>
        await Client.D1.CreateAsync(Name(), Blank(LocationHint)));

    private Task DeleteAsync() => RunVoidAsync("Delete D1 database", () =>
        Client.D1.DeleteAsync(Database()));

    private Task QueryAsync() => RunAsync("Query D1", async () =>
        await Client.D1.QueryAsync(Database(), Sql ?? string.Empty, ParseParameters()));

    private Task BatchAsync() => RunAsync("Batch D1", () =>
        Client.D1.BatchAsync(Database(), Queries()));

    private Task ListBackupsAsync() => RunAsync("List D1 backups", async () =>
        await Client.D1.ListBackupsAsync(Database(), new D1BackupListParameters
        {
            PerPage = OptionalInt(ListPerPage),
        }));

    private Task GetBackupAsync() => RunAsync("Get D1 backup", async () =>
        await Client.D1.GetBackupAsync(Database(), Backup()));

    private Task RestoreBackupAsync() => RunVoidAsync("Restore D1 backup", () =>
        Client.D1.RestoreBackupAsync(Database(), Backup()));

    private IReadOnlyList<object?>? ParseParameters()
    {
        var json = Blank(ParametersJson);
        if (json is null)
        {
            return null;
        }

        try
        {
            // Deserializing to JsonElement keeps every SQL type intact, and the library re-serializes
            // each element verbatim into the "params" array.
            return JsonSerializer.Deserialize<List<JsonElement>>(json, CloudflareJson.Default)?
                .Cast<object?>()
                .ToList();
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"The parameters are not a valid JSON array: {exception.Message}", exception);
        }
    }

    private Task QueryRawAsync() => RunAsync("Query D1 (array rows)", async () =>
        await Client.D1.QueryRawAsync(Database(), Sql ?? string.Empty, ParseParameters()));

    private Task BatchRawAsync() => RunAsync("Batch D1 (array rows)", () =>
        Client.D1.BatchRawAsync(Database(), Queries()));

    private Task ExecuteAsync() => RunAsync("Execute a D1 request body", () =>
        Client.D1.ExecuteAsync(Database(), QueryRequest()));

    private Task ExecuteRawAsync() => RunAsync("Execute a D1 request body (array rows)", () =>
        Client.D1.ExecuteRawAsync(Database(), QueryRequest()));

    private Task ExportAsync() => RunAsync("Export D1 database", async () =>
        await Client.D1.ExportAsync(Database(), ExportRequest()));

    private Task ExportAndWaitAsync() => RunAsync("Export D1 database and wait", async () =>
        await Client.D1.ExportAndWaitAsync(Database(), DumpOptions()));

    private Task ComputeEtagAsync() => RunVoidAsync("Compute the etag from the SQL box", () =>
    {
        Etag = D1ImportRequest.ComputeEtag(Sql ?? string.Empty);
        return Task.CompletedTask;
    });

    private Task ImportInitAsync() => RunAsync("Ask D1 for an upload URL", async () =>
        await Client.D1.InitialiseImportAsync(Database(), EtagValue()));

    private Task ImportIngestAsync() => RunAsync("Ingest the uploaded D1 dump", async () =>
        await Client.D1.IngestImportAsync(Database(), EtagValue(), Filename()));

    private Task ImportPollAsync() => RunAsync("Poll the D1 import once", async () =>
        await Client.D1.PollImportAsync(Database(), Bookmark()));

    private Task ImportWaitAsync() => RunAsync("Wait for the D1 import", async () =>
        await Client.D1.WaitForImportAsync(Database(), Bookmark()));

    private Task SetReadReplicationAsync() => RunAsync($"Set D1 read replication to {ReadReplicationMode}", async () =>
        await Client.D1.SetReadReplicationAsync(Database(), ReadReplicationMode));

    private Task UndeleteAsync() => RunAsync("Undelete D1 database", async () =>
        await Client.D1.UndeleteAsync(Database()));

    private D1QueryRequest QueryRequest()
    {
        var json = Blank(RawJson) ?? throw new ArgumentException("Enter the request body as JSON.");

        try
        {
            return JsonSerializer.Deserialize<D1QueryRequest>(json, CloudflareJson.Default)
                ?? throw new ArgumentException("The request body deserialized to nothing.");
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"The request body is not valid JSON: {exception.Message}", exception);
        }
    }

    private List<D1Query> Queries()
    {
        var json = Blank(BatchJson) ?? throw new ArgumentException("Enter a JSON array of statements.");

        try
        {
            return JsonSerializer.Deserialize<List<D1Query>>(json, CloudflareJson.Default) ?? new List<D1Query>();
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"The batch is not valid JSON: {exception.Message}", exception);
        }
    }

    private D1ExportRequest ExportRequest() =>
        Blank(ExportBookmark) is string bookmark
            ? D1ExportRequest.Continue(bookmark, DumpOptions())
            : D1ExportRequest.Start(DumpOptions());

    private D1DumpOptions? DumpOptions()
    {
        var tables = Lines(DumpTables);
        if (!DumpNoData && !DumpNoSchema && tables is null)
        {
            return null;
        }

        return new D1DumpOptions
        {
            // Only send the flags that are actually on: a false would claim a restriction that is not there.
            NoData = DumpNoData ? true : null,
            NoSchema = DumpNoSchema ? true : null,
            Tables = tables,
        };
    }

    private string EtagValue() =>
        Blank(Etag) ?? throw new ArgumentException("Enter the file's MD5, or compute it from the SQL box.");

    private string Filename() =>
        Blank(ImportFilename) ?? throw new ArgumentException("Enter the dump's file name.");

    private string Bookmark() =>
        Blank(ImportBookmark) ?? throw new ArgumentException("Enter the final_bookmark from the ingest response.");

    private string Database() =>
        Blank(DatabaseId) ?? throw new ArgumentException("Enter a database uuid - list the databases first.");

    private string Name() =>
        Blank(DatabaseName) ?? throw new ArgumentException("Enter a database name.");

    private string Backup() =>
        Blank(BackupId) ?? throw new ArgumentException("Enter a backup id - list the backups first.");
}
