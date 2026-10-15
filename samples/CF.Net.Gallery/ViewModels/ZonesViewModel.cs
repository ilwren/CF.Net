using CFNet.Models.Common;
using CFNet.Models.Dns;
using CFNet.Models.Zones;
using CFNet.Serialization;
using System.Text.Json;

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
    private string _settingZoneId = string.Empty;
    private string _settingId = "always_online";
    private string _settingValueJson = string.Empty;
    private bool _settingOn = true;
    private string _settingBodyJson = string.Empty;
    private string _bulkSettingsJson = string.Empty;
    private string _zoneFile = string.Empty;
    private string _importProxied = string.Empty;
    private string _dnssecZoneId = string.Empty;
    private string _dnssecMultiSigner = string.Empty;
    private string _dnssecPresigned = string.Empty;
    private string _dnssecUseNsec3 = string.Empty;

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
        DnsListAllCommand = new RelayCommand(DnsListAllAsync);
        DnsFindCommand = new RelayCommand(DnsFindAsync);
        DnsGetCommand = new RelayCommand(DnsGetAsync);
        DnsCreateCommand = new RelayCommand(DnsCreateAsync);
        DnsUpdateCommand = new RelayCommand(DnsUpdateAsync);
        DnsDeleteCommand = new RelayCommand(DnsDeleteAsync);
        ListSettingsCommand = new RelayCommand(ListSettingsAsync);
        GetSettingCommand = new RelayCommand(GetSettingAsync);
        SetSettingValueCommand = new RelayCommand(SetSettingValueAsync);
        SetSettingOnOffCommand = new RelayCommand(SetSettingOnOffAsync);
        UpdateSettingCommand = new RelayCommand(UpdateSettingAsync);
        BulkUpdateSettingsCommand = new RelayCommand(BulkUpdateSettingsAsync);
        ExportZoneFileCommand = new RelayCommand(ExportZoneFileAsync);
        ImportZoneFileCommand = new RelayCommand(ImportZoneFileAsync);
        DnsReplaceCommand = new RelayCommand(DnsReplaceAsync);
        DnsCreateAddressCommand = new RelayCommand(DnsCreateAddressAsync);
        DnsCreateCnameCommand = new RelayCommand(DnsCreateCnameAsync);
        DnsCreateTxtCommand = new RelayCommand(DnsCreateTxtAsync);
        GetDnssecCommand = new RelayCommand(GetDnssecAsync);
        EnableDnssecCommand = new RelayCommand(EnableDnssecAsync);
        DisableDnssecCommand = new RelayCommand(DisableDnssecAsync);
        UpdateDnssecSigningCommand = new RelayCommand(UpdateDnssecSigningAsync);
        DeleteDnssecCommand = new RelayCommand(DeleteDnssecAsync);
        ListDnssecZsksCommand = new RelayCommand(ListDnssecZsksAsync);
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
    /// <summary>Walks every page of DNS records, keeping the filters.</summary>
    public RelayCommand DnsListAllCommand { get; }

    public RelayCommand DnsFindCommand { get; }

    /// <summary>Gets one DNS record.</summary>
    public RelayCommand DnsGetCommand { get; }

    /// <summary>Creates a DNS record.</summary>
    public RelayCommand DnsCreateCommand { get; }

    /// <summary>Patches a DNS record.</summary>
    public RelayCommand DnsUpdateCommand { get; }

    /// <summary>Deletes a DNS record.</summary>
    public RelayCommand DnsDeleteCommand { get; }

    /// <summary>Lists every setting on the zone.</summary>
    public RelayCommand ListSettingsCommand { get; }

    /// <summary>Reads one setting.</summary>
    public RelayCommand GetSettingCommand { get; }

    /// <summary>Sends the value box as one setting's value.</summary>
    public RelayCommand SetSettingValueCommand { get; }

    /// <summary>Sends <c>"on"</c> or <c>"off"</c> for one setting.</summary>
    public RelayCommand SetSettingOnOffCommand { get; }

    /// <summary>Patches one setting with a hand-written body.</summary>
    public RelayCommand UpdateSettingCommand { get; }

    /// <summary>Patches several settings in one call.</summary>
    public RelayCommand BulkUpdateSettingsCommand { get; }

    /// <summary>Downloads the zone file.</summary>
    public RelayCommand ExportZoneFileCommand { get; }

    /// <summary>Uploads a BIND zone file.</summary>
    public RelayCommand ImportZoneFileCommand { get; }

    /// <summary>Replaces a record wholesale with a PUT.</summary>
    public RelayCommand DnsReplaceCommand { get; }

    /// <summary>Creates an A or AAAA record without naming the type.</summary>
    public RelayCommand DnsCreateAddressCommand { get; }

    /// <summary>Creates a CNAME record without naming the type.</summary>
    public RelayCommand DnsCreateCnameCommand { get; }

    /// <summary>Creates a TXT record, which is never proxied.</summary>
    public RelayCommand DnsCreateTxtCommand { get; }

    /// <summary>Reads DNSSEC, including the DS record a registrar needs.</summary>
    public RelayCommand GetDnssecCommand { get; }

    /// <summary>Turns DNSSEC on; the zone then waits on the registrar.</summary>
    public RelayCommand EnableDnssecCommand { get; }

    /// <summary>Turns DNSSEC off.</summary>
    public RelayCommand DisableDnssecCommand { get; }

    /// <summary>Changes the three signing switches without touching the enabled state.</summary>
    public RelayCommand UpdateDnssecSigningCommand { get; }

    /// <summary>Deletes the zone's DNSSEC records.</summary>
    public RelayCommand DeleteDnssecCommand { get; }

    /// <summary>Lists the Zone Signing Keys.</summary>
    public RelayCommand ListDnssecZsksCommand { get; }

    /// <summary>Zone the DNSSEC calls run against; blank uses the configured default.</summary>
    public string DnssecZoneId { get => _dnssecZoneId; set => SetProperty(ref _dnssecZoneId, value); }

    /// <summary>Multi-signer DNSSEC: <c>true</c>, <c>false</c>, or blank to leave it alone.</summary>
    public string DnssecMultiSigner { get => _dnssecMultiSigner; set => SetProperty(ref _dnssecMultiSigner, value); }

    /// <summary>Take an externally signed zone as secondary: <c>true</c>, <c>false</c>, or blank.</summary>
    public string DnssecPresigned { get => _dnssecPresigned; set => SetProperty(ref _dnssecPresigned, value); }

    /// <summary>Use NSEC3 rather than NSEC: <c>true</c>, <c>false</c>, or blank.</summary>
    public string DnssecUseNsec3 { get => _dnssecUseNsec3; set => SetProperty(ref _dnssecUseNsec3, value); }

    /// <summary>Zone the settings and zone-file calls run against; blank uses the configured default.</summary>
    public string SettingZoneId { get => _settingZoneId; set => SetProperty(ref _settingZoneId, value); }

    /// <summary>Setting id, for example <c>always_online</c>.</summary>
    public string SettingId { get => _settingId; set => SetProperty(ref _settingId, value); }

    /// <summary>The value to send, as JSON - a string, a number or an object, depending on the setting.</summary>
    public string SettingValueJson { get => _settingValueJson; set => SetProperty(ref _settingValueJson, value); }

    /// <summary>What the on/off switch sends.</summary>
    public bool SettingOn { get => _settingOn; set => SetProperty(ref _settingOn, value); }

    /// <summary>Hand-written body for the single-setting PATCH.</summary>
    public string SettingBodyJson { get => _settingBodyJson; set => SetProperty(ref _settingBodyJson, value); }

    /// <summary>Hand-written array of changes for the bulk PATCH.</summary>
    public string BulkSettingsJson { get => _bulkSettingsJson; set => SetProperty(ref _bulkSettingsJson, value); }

    /// <summary>BIND zone file to import.</summary>
    public string ZoneFile { get => _zoneFile; set => SetProperty(ref _zoneFile, value); }

    /// <summary>Import <c>proxied</c> field; blank lets Cloudflare decide.</summary>
    public string ImportProxied { get => _importProxied; set => SetProperty(ref _importProxied, value); }

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

    private Task ListSettingsAsync() => RunAsync("List zone settings", async () =>
        await Client.Zones.ListSettingsAsync(SettingsZone()));

    private Task GetSettingAsync() => RunAsync("Get zone setting", async () =>
        await Client.Zones.GetSettingAsync(Setting(), SettingsZone()));

    private Task SetSettingValueAsync() => RunAsync($"Set {SettingId}", async () =>
        await Client.Zones.SetSettingValueAsync(Setting(), SettingValue(), SettingsZone()));

    private Task SetSettingOnOffAsync() => RunAsync($"Switch {SettingId} {(SettingOn ? "on" : "off")}", async () =>
        await Client.Zones.SetSettingOnOffAsync(Setting(), SettingOn, SettingsZone()));

    private Task UpdateSettingAsync() => RunAsync("Patch zone setting (hand-written body)", async () =>
        await Client.Zones.UpdateSettingAsync(
            Setting(),
            Parse<UpdateZoneSettingRequest>(SettingBodyJson, "setting body"),
            SettingsZone()));

    private Task BulkUpdateSettingsAsync() => RunAsync("Patch several zone settings", async () =>
        await Client.Zones.UpdateSettingsAsync(
            ParseList<ZoneSettingChange>(BulkSettingsJson, "array of changes"),
            SettingsZone()));

    private Task ExportZoneFileAsync() => RunAsync("Export the zone file", async () =>
        await Client.DnsRecords.ExportAsync(SettingsZone()));

    private Task ImportZoneFileAsync() => RunAsync("Import a zone file", async () =>
        await Client.DnsRecords.ImportAsync(ZoneFileText(), OptionalBool(ImportProxied), SettingsZone()));

    private string? SettingsZone() => Blank(SettingZoneId);

    private string Setting() =>
        Blank(SettingId) ?? throw new ArgumentException("Enter a setting id, for example always_online.");

    private object? SettingValue()
    {
        var text = Blank(SettingValueJson);
        if (text is null)
        {
            return null;
        }

        try
        {
            // Settings take strings, numbers, booleans and objects alike, so send whatever the JSON says.
            return JsonSerializer.Deserialize<JsonElement>(text, CloudflareJson.Default);
        }
        catch (JsonException)
        {
            return text;
        }
    }

    private string ZoneFileText() =>
        Blank(ZoneFile) ?? throw new ArgumentException("Paste a BIND zone file, or export one first.");

    private static T Parse<T>(string? json, string what)
        where T : class
    {
        var text = Blank(json) ?? throw new ArgumentException($"Enter the {what} as JSON.");

        try
        {
            return JsonSerializer.Deserialize<T>(text, CloudflareJson.Default)
                ?? throw new ArgumentException($"The {what} deserialized to nothing.");
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"The {what} is not valid JSON: {exception.Message}", exception);
        }
    }

    private static List<T> ParseList<T>(string? json, string what)
    {
        var text = Blank(json) ?? throw new ArgumentException($"Enter the {what} as JSON.");

        try
        {
            return JsonSerializer.Deserialize<List<T>>(text, CloudflareJson.Default) ?? new List<T>();
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"The {what} is not valid JSON: {exception.Message}", exception);
        }
    }

    private Task DnsReplaceAsync() => RunAsync("Replace DNS record (PUT)", () =>
        Client.DnsRecords.ReplaceAsync(
            RecordId(),
            new DnsRecord
            {
                Type = DnsCreateType,
                Name = RecordName(),
                Content = RecordContent(),
                Ttl = OptionalInt(DnsCreateTtl) ?? 1,
                Priority = OptionalInt(DnsCreatePriority),
                Comment = Blank(DnsCreateComment),
                Proxied = DnsCreateProxied,
            },
            ZoneIdFor(DnsCreateZoneId)));

    private Task DnsCreateAddressAsync() => RunAsync("Create an address record", async () =>
        await Client.DnsRecords.CreateAddressRecordAsync(
            RecordName(),
            RecordContent(),
            DnsCreateProxied,
            OptionalInt(DnsCreateTtl) ?? 1,
            ZoneIdFor(DnsCreateZoneId)));

    private Task DnsCreateCnameAsync() => RunAsync("Create a CNAME record", async () =>
        await Client.DnsRecords.CreateCnameRecordAsync(
            RecordName(),
            RecordContent(),
            DnsCreateProxied,
            OptionalInt(DnsCreateTtl) ?? 1,
            ZoneIdFor(DnsCreateZoneId)));

    private Task DnsCreateTxtAsync() => RunAsync("Create a TXT record", async () =>
        await Client.DnsRecords.CreateTxtRecordAsync(
            RecordName(),
            RecordContent(),
            OptionalInt(DnsCreateTtl) ?? 1,
            ZoneIdFor(DnsCreateZoneId)));

    private string RecordName() =>
        Blank(DnsCreateName) ?? throw new ArgumentException("Enter a record name.");

    private string RecordContent() =>
        Blank(DnsCreateContent) ?? throw new ArgumentException("Enter the record's content.");

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

    private Task DnsListAllAsync() => RunAsync("List every DNS record", async () =>
    {
        var records = new List<DnsRecord>();
        await foreach (var record in Client.DnsRecords.ListAllAsync(new DnsRecordListParameters
        {
            PerPage = OptionalInt(DnsListPerPage),
            Type = Blank(DnsListType),
            Name = Blank(DnsListName),
        }, ZoneIdFor(DnsListZoneId)))
        {
            records.Add(record);
        }

        return records;
    });

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

    private Task GetDnssecAsync() => RunAsync("Get DNSSEC", async () =>
        await Client.Dnssec.GetAsync(ZoneIdFor(DnssecZoneId)));

    private Task EnableDnssecAsync() => RunAsync("Enable DNSSEC", async () =>
        await Client.Dnssec.EnableAsync(ZoneIdFor(DnssecZoneId)));

    private Task DisableDnssecAsync() => RunAsync("Disable DNSSEC", async () =>
        await Client.Dnssec.DisableAsync(ZoneIdFor(DnssecZoneId)));

    private Task UpdateDnssecSigningAsync() => RunAsync("Change the DNSSEC signing switches", async () =>
        await Client.Dnssec.UpdateAsync(
            UpdateDnssecRequest.SigningOptions(
                OptionalBool(DnssecMultiSigner),
                OptionalBool(DnssecPresigned),
                OptionalBool(DnssecUseNsec3)),
            ZoneIdFor(DnssecZoneId)));

    private Task DeleteDnssecAsync() => RunAsync("Delete DNSSEC records", async () =>
        await Client.Dnssec.DeleteAsync(ZoneIdFor(DnssecZoneId)));

    private Task ListDnssecZsksAsync() => RunAsync("List DNSSEC ZSKs", async () =>
        await Client.Dnssec.ListZoneSigningKeysAsync(ZoneIdFor(DnssecZoneId)));

    private string? ZoneIdFor(string? explicitZoneId) => Blank(explicitZoneId) ?? Blank(ZoneId);
}
