using CFNet.Models.Common;
using CFNet.Models.Dns;
using CFNet.Models.Zones;

namespace CFNet.Gallery.ViewModels;

/// <summary>
/// Domains: zones plus the DNS records that live inside them.
/// </summary>
public sealed class ZonesViewModel : ModuleViewModelBase
{
    private string _listPerPage = "20";
    private string _listPage = "1";
    private string _listStatus = string.Empty;
    private string _listName = string.Empty;
    private string _getZoneId = string.Empty;
    private string _getZoneName = string.Empty;
    private string _createZoneName = string.Empty;
    private string _createZoneType = ZoneTypeValues.Full;
    private string _createZoneAccount = string.Empty;
    private bool _paused;
    private bool _purgeEverything = true;
    private string _purgeUrls = string.Empty;
    private string _purgeTags = string.Empty;
    private string _purgeZoneId = string.Empty;
    private string _dnsListType = string.Empty;
    private string _dnsListName = string.Empty;
    private string _dnsListPerPage = "50";
    private string _dnsListZoneId = string.Empty;
    private string _dnsCreateType = DnsRecordTypes.A;
    private string _dnsCreateName = string.Empty;
    private string _dnsCreateContent = string.Empty;
    private string _dnsCreateTtl = "1";
    private string _dnsCreatePriority = string.Empty;
    private string _dnsCreateComment = string.Empty;
    private bool _dnsCreateProxied = true;
    private string _dnsCreateZoneId = string.Empty;
    private string _dnsRecordId = string.Empty;
    private string _dnsUpdateContent = string.Empty;
    private string _dnsFindName = string.Empty;

    /// <summary>Creates the page.</summary>
    /// <param name="gallery">Owning shell.</param>
    public ZonesViewModel(GalleryViewModel gallery)
        : base(gallery)
    {
        ListCommand = new RelayCommand(ListAsync);
        ListAllCommand = new RelayCommand(ListAllAsync);
        GetCommand = new RelayCommand(GetAsync);
        GetByNameCommand = new RelayCommand(GetByNameAsync);
        CreateCommand = new RelayCommand(CreateAsync);
        SetPausedCommand = new RelayCommand(SetPausedAsync);
        ActivationCheckCommand = new RelayCommand(ActivationCheckAsync);
        PurgeCacheCommand = new RelayCommand(PurgeCacheAsync);
        DeleteZoneCommand = new RelayCommand(DeleteZoneAsync);

        DnsListCommand = new RelayCommand(DnsListAsync);
        DnsFindCommand = new RelayCommand(DnsFindAsync);
        DnsGetCommand = new RelayCommand(DnsGetAsync);
        DnsCreateCommand = new RelayCommand(DnsCreateAsync);
        DnsUpdateCommand = new RelayCommand(DnsUpdateAsync);
        DnsDeleteCommand = new RelayCommand(DnsDeleteAsync);
    }

    /// <inheritdoc />
    public override string Title => "Domains";

    /// <inheritdoc />
    public override string Description =>
        "Zones (the domains on the account) and the DNS records inside them.";

    /// <summary>Record types offered in the DNS picker.</summary>
    public IReadOnlyList<string> RecordTypes { get; } = new[]
    {
        DnsRecordTypes.A,
        DnsRecordTypes.Aaaa,
        DnsRecordTypes.Cname,
        DnsRecordTypes.Txt,
        DnsRecordTypes.Mx,
        DnsRecordTypes.Ns,
        DnsRecordTypes.Ptr,
        DnsRecordTypes.Srv,
        DnsRecordTypes.Caa,
        DnsRecordTypes.Loc,
    };

    /// <summary>Zone types offered in the create picker.</summary>
    public IReadOnlyList<string> ZoneTypes { get; } = new[]
    {
        ZoneTypeValues.Full,
        ZoneTypeValues.Partial,
        ZoneTypeValues.Secondary,
        ZoneTypeValues.Internal,
    };

    /// <summary>Zone statuses offered in the list filter.</summary>
    public IReadOnlyList<string> ZoneStatuses { get; } = new[]
    {
        string.Empty,
        ZoneStatusValues.Active,
        ZoneStatusValues.Pending,
        ZoneStatusValues.Initializing,
        ZoneStatusValues.Moved,
        ZoneStatusValues.Deleted,
        ZoneStatusValues.Deactivated,
    };

    /// <summary>Items per page for the zone list.</summary>
    public string ListPerPage { get => _listPerPage; set => SetProperty(ref _listPerPage, value); }

    /// <summary>Page number for the zone list.</summary>
    public string ListPage { get => _listPage; set => SetProperty(ref _listPage, value); }

    /// <summary>Status filter for the zone list.</summary>
    public string ListStatus { get => _listStatus; set => SetProperty(ref _listStatus, value); }

    /// <summary>Name filter for the zone list.</summary>
    public string ListName { get => _listName; set => SetProperty(ref _listName, value); }

    /// <summary>Zone id for the get call.</summary>
    public string GetZoneId { get => _getZoneId; set => SetProperty(ref _getZoneId, value); }

    /// <summary>Zone name for the get-by-name call.</summary>
    public string GetZoneName { get => _getZoneName; set => SetProperty(ref _getZoneName, value); }

    /// <summary>Domain to add.</summary>
    public string CreateZoneName { get => _createZoneName; set => SetProperty(ref _createZoneName, value); }

    /// <summary>Zone type to add.</summary>
    public string CreateZoneType { get => _createZoneType; set => SetProperty(ref _createZoneType, value); }

    /// <summary>Account to add the zone to.</summary>
    public string CreateZoneAccount { get => _createZoneAccount; set => SetProperty(ref _createZoneAccount, value); }

    /// <summary>New paused flag.</summary>
    public bool Paused { get => _paused; set => SetProperty(ref _paused, value); }

    /// <summary>Purge the whole cache instead of specific URLs or tags.</summary>
    public bool PurgeEverything { get => _purgeEverything; set => SetProperty(ref _purgeEverything, value); }

    /// <summary>One absolute URL per line.</summary>
    public string PurgeUrls { get => _purgeUrls; set => SetProperty(ref _purgeUrls, value); }

    /// <summary>One cache tag per line.</summary>
    public string PurgeTags { get => _purgeTags; set => SetProperty(ref _purgeTags, value); }

    /// <summary>Zone to purge; blank means the default zone id.</summary>
    public string PurgeZoneId { get => _purgeZoneId; set => SetProperty(ref _purgeZoneId, value); }

    /// <summary>DNS record type filter.</summary>
    public string DnsListType { get => _dnsListType; set => SetProperty(ref _dnsListType, value); }

    /// <summary>DNS record name filter.</summary>
    public string DnsListName { get => _dnsListName; set => SetProperty(ref _dnsListName, value); }

    /// <summary>DNS items per page.</summary>
    public string DnsListPerPage { get => _dnsListPerPage; set => SetProperty(ref _dnsListPerPage, value); }

    /// <summary>Zone holding the records; blank means the default zone id.</summary>
    public string DnsListZoneId { get => _dnsListZoneId; set => SetProperty(ref _dnsListZoneId, value); }

    /// <summary>Record type to create.</summary>
    public string DnsCreateType { get => _dnsCreateType; set => SetProperty(ref _dnsCreateType, value); }

    /// <summary>Record name to create.</summary>
    public string DnsCreateName { get => _dnsCreateName; set => SetProperty(ref _dnsCreateName, value); }

    /// <summary>Record content to create.</summary>
    public string DnsCreateContent { get => _dnsCreateContent; set => SetProperty(ref _dnsCreateContent, value); }

    /// <summary>Time to live; 1 means automatic.</summary>
    public string DnsCreateTtl { get => _dnsCreateTtl; set => SetProperty(ref _dnsCreateTtl, value); }

    /// <summary>MX/SRV priority.</summary>
    public string DnsCreatePriority { get => _dnsCreatePriority; set => SetProperty(ref _dnsCreatePriority, value); }

    /// <summary>Record comment.</summary>
    public string DnsCreateComment { get => _dnsCreateComment; set => SetProperty(ref _dnsCreateComment, value); }

    /// <summary>Whether the record is proxied through Cloudflare.</summary>
    public bool DnsCreateProxied { get => _dnsCreateProxied; set => SetProperty(ref _dnsCreateProxied, value); }

    /// <summary>Zone for the create call; blank means the default zone id.</summary>
    public string DnsCreateZoneId { get => _dnsCreateZoneId; set => SetProperty(ref _dnsCreateZoneId, value); }

    /// <summary>Record id for get / update / delete.</summary>
    public string DnsRecordId { get => _dnsRecordId; set => SetProperty(ref _dnsRecordId, value); }

    /// <summary>New content for the update call.</summary>
    public string DnsUpdateContent { get => _dnsUpdateContent; set => SetProperty(ref _dnsUpdateContent, value); }

    /// <summary>Name to search for.</summary>
    public string DnsFindName { get => _dnsFindName; set => SetProperty(ref _dnsFindName, value); }

    /// <summary>Lists zones.</summary>
    public RelayCommand ListCommand { get; }

    /// <summary>Walks every zone page.</summary>
    public RelayCommand ListAllCommand { get; }

    /// <summary>Gets one zone.</summary>
    public RelayCommand GetCommand { get; }

    /// <summary>Looks a zone up by domain name.</summary>
    public RelayCommand GetByNameCommand { get; }

    /// <summary>Adds a domain to the account.</summary>
    public RelayCommand CreateCommand { get; }

    /// <summary>Pauses or resumes a zone.</summary>
    public RelayCommand SetPausedCommand { get; }

    /// <summary>Triggers an activation check.</summary>
    public RelayCommand ActivationCheckCommand { get; }

    /// <summary>Purges the cache.</summary>
    public RelayCommand PurgeCacheCommand { get; }

    /// <summary>Removes a zone from the account.</summary>
    public RelayCommand DeleteZoneCommand { get; }

    /// <summary>Lists DNS records.</summary>
    public RelayCommand DnsListCommand { get; }

    /// <summary>Finds records by name.</summary>
    public RelayCommand DnsFindCommand { get; }

    /// <summary>Gets one DNS record.</summary>
    public RelayCommand DnsGetCommand { get; }

    /// <summary>Creates a DNS record.</summary>
    public RelayCommand DnsCreateCommand { get; }

    /// <summary>Patches a DNS record.</summary>
    public RelayCommand DnsUpdateCommand { get; }

    /// <summary>Deletes a DNS record.</summary>
    public RelayCommand DnsDeleteCommand { get; }

    private Task ListAsync() => RunAsync("List zones", async () =>
        await Client.Zones.ListAsync(new ZoneListParameters
        {
            Page = OptionalInt(ListPage),
            PerPage = OptionalInt(ListPerPage),
            Status = Blank(ListStatus),
            Name = Blank(ListName),
        }));

    private Task ListAllAsync() => RunAsync("List every zone", async () =>
    {
        var zones = new List<Zone>();
        await foreach (var zone in Client.Zones.ListAllAsync(new ZoneListParameters { PerPage = 50 }))
        {
            zones.Add(zone);
        }

        return zones;
    });

    private Task GetAsync() => RunAsync("Get zone", async () =>
        await Client.Zones.GetAsync(ZoneIdFor(GetZoneId)));

    private Task GetByNameAsync() => RunAsync("Get zone by name", async () =>
        await Client.Zones.GetByNameAsync(Blank(GetZoneName) ?? throw new ArgumentException("Enter a domain name.")));

    private Task CreateAsync() => RunAsync("Create zone", async () =>
        await Client.Zones.CreateAsync(new CreateZoneRequest(
            Blank(CreateZoneName) ?? throw new ArgumentException("Enter a domain name."),
            Blank(CreateZoneAccount) ?? AccountId,
            string.IsNullOrWhiteSpace(CreateZoneType) ? ZoneTypeValues.Full : CreateZoneType)));

    private Task SetPausedAsync() => RunVoidAsync($"Set paused = {Paused}", () =>
        Client.Zones.SetPausedAsync(Paused, ZoneIdFor(GetZoneId)));

    private Task ActivationCheckAsync() => RunAsync("Activation check", () =>
        Client.Zones.RunActivationCheckAsync(ZoneIdFor(GetZoneId)));

    private Task PurgeCacheAsync() => RunAsync("Purge cache", () =>
    {
        var request = PurgeEverything
            ? PurgeCacheRequest.Everything()
            : new PurgeCacheRequest
            {
                Files = Lines(PurgeUrls),
                Tags = Lines(PurgeTags),
            };

        return Client.Zones.PurgeCacheAsync(request, ZoneIdFor(PurgeZoneId));
    });

    private Task DeleteZoneAsync() => RunVoidAsync("Delete zone", () =>
        Client.Zones.DeleteAsync(ZoneIdFor(GetZoneId) ?? throw new ArgumentException("Enter a zone id.")));

    private Task DnsListAsync() => RunAsync("List DNS records", async () =>
        await Client.DnsRecords.ListAsync(new DnsRecordListParameters
        {
            PerPage = OptionalInt(DnsListPerPage),
            Type = Blank(DnsListType),
            Name = Blank(DnsListName),
        }, ZoneIdFor(DnsListZoneId)));

    private Task DnsFindAsync() => RunAsync("Find DNS records", async () =>
        await Client.DnsRecords.FindAsync(
            Blank(DnsFindName) ?? throw new ArgumentException("Enter a record name."),
            Blank(DnsListType),
            ZoneIdFor(DnsListZoneId)));

    private Task DnsGetAsync() => RunAsync("Get DNS record", async () =>
        await Client.DnsRecords.GetAsync(RecordId(), ZoneIdFor(DnsListZoneId)));

    private Task DnsCreateAsync() => RunAsync("Create DNS record", async () =>
        await Client.DnsRecords.CreateAsync(new DnsRecord
        {
            Type = DnsCreateType,
            Name = Blank(DnsCreateName) ?? throw new ArgumentException("Enter a record name."),
            Content = Blank(DnsCreateContent),
            Ttl = OptionalInt(DnsCreateTtl) ?? 1,
            Priority = OptionalInt(DnsCreatePriority),
            Comment = Blank(DnsCreateComment),
            Proxied = DnsCreateProxied,
        }, ZoneIdFor(DnsCreateZoneId)));

    private Task DnsUpdateAsync() => RunAsync("Update DNS record", async () =>
        await Client.DnsRecords.UpdateAsync(
            RecordId(),
            new DnsRecord { Content = Blank(DnsUpdateContent) },
            ZoneIdFor(DnsListZoneId)));

    private Task DnsDeleteAsync() => RunVoidAsync("Delete DNS record", () =>
        Client.DnsRecords.DeleteAsync(RecordId(), ZoneIdFor(DnsListZoneId)));

    private string RecordId() =>
        Blank(DnsRecordId) ?? throw new ArgumentException("Enter a record id - list the records first and copy it from the result.");

    private string? ZoneIdFor(string? explicitZoneId) => Blank(explicitZoneId) ?? Blank(ZoneId);
}
