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
    {
        var json = Blank(BatchJson)
            ?? throw new ArgumentException("Enter a JSON array of statements.");

        List<D1Query>? queries;
        try
        {
            queries = JsonSerializer.Deserialize<List<D1Query>>(json, CloudflareJson.Default);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"The batch is not valid JSON: {exception.Message}", exception);
        }

        return Client.D1.BatchAsync(Database(), queries ?? new List<D1Query>());
    });

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

    private string Database() =>
        Blank(DatabaseId) ?? throw new ArgumentException("Enter a database uuid - list the databases first.");

    private string Name() =>
        Blank(DatabaseName) ?? throw new ArgumentException("Enter a database name.");

    private string Backup() =>
        Blank(BackupId) ?? throw new ArgumentException("Enter a backup id - list the backups first.");
}
