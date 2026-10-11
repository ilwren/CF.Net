using CFNet.Models.Kv;
using CFNet.Serialization;
using System.Text;
using System.Text.Json;

namespace CFNet.Gallery.ViewModels;

/// <summary>
/// Workers KV: namespaces, key listings and values.
/// </summary>
public sealed class KvViewModel : ModuleViewModelBase
{
    private string _listPerPage = "20";
    private string _listSearch = string.Empty;
    private string _namespaceId = string.Empty;
    private string _namespaceTitle = string.Empty;
    private string _keyPrefix = string.Empty;
    private string _keyLimit = "100";
    private string _key = string.Empty;
    private string _value = string.Empty;
    private string _valueContentType = "text/plain";
    private string _valueTtl = string.Empty;
    private string _bulkJson = string.Empty;

    /// <summary>Creates the page.</summary>
    /// <param name="gallery">Owning shell.</param>
    public KvViewModel(GalleryViewModel gallery)
        : base(gallery)
    {
        ListNamespacesCommand = new RelayCommand(ListNamespacesAsync);
        GetNamespaceByTitleCommand = new RelayCommand(GetNamespaceByTitleAsync);
        GetNamespaceCommand = new RelayCommand(GetNamespaceAsync);
        CreateNamespaceCommand = new RelayCommand(CreateNamespaceAsync);
        RenameNamespaceCommand = new RelayCommand(RenameNamespaceAsync);
        DeleteNamespaceCommand = new RelayCommand(DeleteNamespaceAsync);
        ListKeysCommand = new RelayCommand(ListKeysAsync);
        ListAllKeysCommand = new RelayCommand(ListAllKeysAsync);
        GetValueCommand = new RelayCommand(GetValueAsync);
        PutValueCommand = new RelayCommand(PutValueAsync);
        DeleteValueCommand = new RelayCommand(DeleteValueAsync);
        BulkWriteCommand = new RelayCommand(BulkWriteAsync);
    }

    /// <inheritdoc />
    public override string Title => "KV";

    /// <inheritdoc />
    public override string Description =>
        "Key-value namespaces. Keys are percent-encoded in the URL, so slashes inside a key are safe.";

    /// <summary>Items per page.</summary>
    public string ListPerPage { get => _listPerPage; set => SetProperty(ref _listPerPage, value); }

    /// <summary>Namespace title filter (substring).</summary>
    public string ListSearch { get => _listSearch; set => SetProperty(ref _listSearch, value); }

    /// <summary>Namespace id every key call operates on.</summary>
    public string NamespaceId { get => _namespaceId; set => SetProperty(ref _namespaceId, value); }

    /// <summary>Title for the create / rename / lookup calls.</summary>
    public string NamespaceTitle { get => _namespaceTitle; set => SetProperty(ref _namespaceTitle, value); }

    /// <summary>Key prefix filter.</summary>
    public string KeyPrefix { get => _keyPrefix; set => SetProperty(ref _keyPrefix, value); }

    /// <summary>Page size for the key listing (10 to 1000).</summary>
    public string KeyLimit { get => _keyLimit; set => SetProperty(ref _keyLimit, value); }

    /// <summary>Key for the read / write / delete calls.</summary>
    public string Key { get => _key; set => SetProperty(ref _key, value); }

    /// <summary>Value to store.</summary>
    public string Value { get => _value; set => SetProperty(ref _value, value); }

    /// <summary>Content type stored alongside the value.</summary>
    public string ValueContentType { get => _valueContentType; set => SetProperty(ref _valueContentType, value); }

    /// <summary>Time to live in seconds; blank means no expiry. Minimum is 60.</summary>
    public string ValueTtl { get => _valueTtl; set => SetProperty(ref _valueTtl, value); }

    /// <summary>
    /// Bulk write as a JSON array, e.g.
    /// <c>[{"key":"a","value":"1"},{"key":"b","value":"Mg==","base64":true},{"key":"c","delete":true}]</c>.
    /// </summary>
    public string BulkJson { get => _bulkJson; set => SetProperty(ref _bulkJson, value); }

    /// <summary>Lists namespaces.</summary>
    public RelayCommand ListNamespacesCommand { get; }

    /// <summary>Finds a namespace by exact title.</summary>
    public RelayCommand GetNamespaceByTitleCommand { get; }

    /// <summary>Gets one namespace.</summary>
    public RelayCommand GetNamespaceCommand { get; }

    /// <summary>Creates a namespace.</summary>
    public RelayCommand CreateNamespaceCommand { get; }

    /// <summary>Renames a namespace.</summary>
    public RelayCommand RenameNamespaceCommand { get; }

    /// <summary>Deletes a namespace and everything in it.</summary>
    public RelayCommand DeleteNamespaceCommand { get; }

    /// <summary>Lists one page of keys.</summary>
    public RelayCommand ListKeysCommand { get; }

    /// <summary>Walks every key page.</summary>
    public RelayCommand ListAllKeysCommand { get; }

    /// <summary>Reads a value.</summary>
    public RelayCommand GetValueCommand { get; }

    /// <summary>Writes a value.</summary>
    public RelayCommand PutValueCommand { get; }

    /// <summary>Deletes a value.</summary>
    public RelayCommand DeleteValueCommand { get; }

    /// <summary>Writes many keys in one call.</summary>
    public RelayCommand BulkWriteCommand { get; }

    private Task ListNamespacesAsync() => RunAsync("List KV namespaces", async () =>
        await Client.Kv.ListNamespacesAsync(new KvNamespaceListParameters
        {
            PerPage = OptionalInt(ListPerPage),
            Search = Blank(ListSearch),
        }));

    private Task GetNamespaceByTitleAsync() => RunAsync("Find KV namespace", async () =>
        await Client.Kv.GetNamespaceByTitleAsync(Title()));

    private Task GetNamespaceAsync() => RunAsync("Get KV namespace", async () =>
        await Client.Kv.GetNamespaceAsync(Namespace()));

    private Task CreateNamespaceAsync() => RunAsync("Create KV namespace", async () =>
        await Client.Kv.CreateNamespaceAsync(Title()));

    private Task RenameNamespaceAsync() => RunAsync("Rename KV namespace", async () =>
        await Client.Kv.RenameNamespaceAsync(Namespace(), Title()));

    private Task DeleteNamespaceAsync() => RunVoidAsync("Delete KV namespace", () =>
        Client.Kv.DeleteNamespaceAsync(Namespace()));

    private Task ListKeysAsync() => RunAsync("List KV keys", async () =>
        await Client.Kv.ListKeysAsync(Namespace(), new KvKeyListParameters
        {
            Prefix = Blank(KeyPrefix),
            Limit = OptionalInt(KeyLimit),
        }));

    private Task ListAllKeysAsync() => RunAsync("List every KV key", async () =>
    {
        var keys = new List<KvKey>();
        await foreach (var key in Client.Kv.ListAllKeysAsync(Namespace(), Blank(KeyPrefix), OptionalInt(KeyLimit)))
        {
            keys.Add(key);

            if (keys.Count >= 5000)
            {
                break;
            }
        }

        return keys;
    });

    private Task GetValueAsync() => RunAsync("Get KV value", async () =>
        await Client.Kv.GetValueAsync(Namespace(), KeyName()));

    private Task PutValueAsync() => RunVoidAsync("Put KV value", () =>
        Client.Kv.PutValueAsync(Namespace(), KeyName(), new KvPutValueRequest
        {
            Content = Encoding.UTF8.GetBytes(Value ?? string.Empty),
            ContentType = Blank(ValueContentType) ?? "text/plain",
            ExpirationTtl = OptionalInt(ValueTtl),
        }));

    private Task DeleteValueAsync() => RunVoidAsync("Delete KV value", () =>
        Client.Kv.DeleteValueAsync(Namespace(), KeyName()));

    private Task BulkWriteAsync() => RunVoidAsync("Bulk write KV", () =>
    {
        var json = Blank(BulkJson) ?? throw new ArgumentException("Enter a JSON array of operations.");

        List<KvBulkWriteOperation>? operations;
        try
        {
            operations = JsonSerializer.Deserialize<List<KvBulkWriteOperation>>(json, CloudflareJson.Default);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"The batch is not valid JSON: {exception.Message}", exception);
        }

        return Client.Kv.BulkWriteAsync(Namespace(), operations ?? new List<KvBulkWriteOperation>());
    });

    private string Namespace() =>
        Blank(NamespaceId) ?? throw new ArgumentException("Enter a namespace id - list the namespaces first.");

    private string Title() =>
        Blank(NamespaceTitle) ?? throw new ArgumentException("Enter a namespace title.");

    private string KeyName() =>
        Blank(Key) ?? throw new ArgumentException("Enter a key.");
}
