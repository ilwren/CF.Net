using CFNet.Models.Ssl;

namespace CFNet.Gallery.ViewModels;

/// <summary>
/// Origin TLS client auth: the client certificate Cloudflare presents to a zone's origin, either for the
/// whole zone or per hostname, and the switches that decide whether it is actually sent.
/// </summary>
/// <remarks>
/// Three things have to agree before an origin sees a client certificate: the zone-level setting, a hostname
/// association that is enabled, and a certificate that reached <c>active</c>. This page exposes all three
/// separately so the one that is missing is visible. The last card drives the four deprecated zone-level
/// endpoints, which are the only ones that hand a private key back.
/// </remarks>
public sealed class OriginTlsViewModel : ModuleViewModelBase
{
    private string _zoneOverride = string.Empty;
    private string _hostname = string.Empty;
    private string _hostnames = string.Empty;
    private string _associationState = EnableState;
    private string _certificateId = string.Empty;
    private string _certificatePem = string.Empty;
    private string _privateKeyPem = string.Empty;

    private const string EnableState = "enable";

    private const string DisableState = "disable";

    private const string VoidState = "void";

    /// <summary>Creates the page.</summary>
    /// <param name="gallery">Owning shell.</param>
    public OriginTlsViewModel(GalleryViewModel gallery)
        : base(gallery)
    {
        GetSettingsCommand = new RelayCommand(GetSettingsAsync);
        EnableZoneCommand = new RelayCommand(() => SetSettingsAsync(true));
        DisableZoneCommand = new RelayCommand(() => SetSettingsAsync(false));

        GetHostnameCommand = new RelayCommand(GetHostnameAsync);
        SetHostnamesCommand = new RelayCommand(SetHostnamesAsync);
        EnableHostnameCommand = new RelayCommand(() => ChangeHostnameAsync(EnableState));
        DisableHostnameCommand = new RelayCommand(() => ChangeHostnameAsync(DisableState));
        VoidHostnameCommand = new RelayCommand(() => ChangeHostnameAsync(VoidState));

        ListCertificatesCommand = new RelayCommand(ListCertificatesAsync);
        GetCertificateCommand = new RelayCommand(GetCertificateAsync);
        UploadCertificateCommand = new RelayCommand(UploadCertificateAsync);
        DeleteCertificateCommand = new RelayCommand(DeleteCertificateAsync);

        ListZoneCertificatesCommand = new RelayCommand(ListZoneCertificatesAsync);
        GetZoneCertificateCommand = new RelayCommand(GetZoneCertificateAsync);
        UploadZoneCertificateCommand = new RelayCommand(UploadZoneCertificateAsync);
        DeleteZoneCertificateCommand = new RelayCommand(DeleteZoneCertificateAsync);
    }

    /// <inheritdoc />
    public override string Title => "Origin TLS";

    /// <inheritdoc />
    public override string Description =>
        "Mutual TLS towards the origin: the zone-level switch, per-hostname certificate associations, the uploaded client certificates, and the deprecated zone-level certificates that return their private key.";

    /// <summary>The three things an association can be set to; <c>void</c> sends <c>enabled: null</c>.</summary>
    public IReadOnlyList<string> AssociationStates { get; } = new[] { EnableState, DisableState, VoidState };

    /// <summary>Zone to talk to; empty uses the configured default.</summary>
    public string ZoneOverride { get => _zoneOverride; set => SetProperty(ref _zoneOverride, value); }

    /// <summary>Hostname on the origin to associate, enable, disable or void.</summary>
    public string Hostname { get => _hostname; set => SetProperty(ref _hostname, value); }

    /// <summary>One hostname per line, for the batch call.</summary>
    public string Hostnames { get => _hostnames; set => SetProperty(ref _hostnames, value); }

    /// <summary>Which of the three states the batch call asks for.</summary>
    public string AssociationState { get => _associationState; set => SetProperty(ref _associationState, value); }

    /// <summary>Identifier of an uploaded hostname certificate.</summary>
    public string CertificateId { get => _certificateId; set => SetProperty(ref _certificateId, value); }

    /// <summary>Client certificate in PEM.</summary>
    public string CertificatePem { get => _certificatePem; set => SetProperty(ref _certificatePem, value); }

    /// <summary>Private key matching that certificate, in PEM.</summary>
    public string PrivateKeyPem { get => _privateKeyPem; set => SetProperty(ref _privateKeyPem, value); }

    /// <summary>Reads the zone-level switch.</summary>
    public RelayCommand GetSettingsCommand { get; }

    /// <summary>Turns zone-level authenticated origin pulls on.</summary>
    public RelayCommand EnableZoneCommand { get; }

    /// <summary>Turns them off.</summary>
    public RelayCommand DisableZoneCommand { get; }

    /// <summary>Reads one hostname's association and both of its statuses.</summary>
    public RelayCommand GetHostnameCommand { get; }

    /// <summary>Sends one <c>config</c> array covering every hostname in the list.</summary>
    public RelayCommand SetHostnamesCommand { get; }

    /// <summary>Associates one hostname with the certificate and enables it.</summary>
    public RelayCommand EnableHostnameCommand { get; }

    /// <summary>Keeps the association but stops the certificate being sent.</summary>
    public RelayCommand DisableHostnameCommand { get; }

    /// <summary>Voids the association, which is what a certificate deletion needs first.</summary>
    public RelayCommand VoidHostnameCommand { get; }

    /// <summary>Lists the zone's hostname client certificates.</summary>
    public RelayCommand ListCertificatesCommand { get; }

    /// <summary>Reads one hostname client certificate.</summary>
    public RelayCommand GetCertificateCommand { get; }

    /// <summary>Uploads a certificate and its key.</summary>
    public RelayCommand UploadCertificateCommand { get; }

    /// <summary>Deletes a hostname client certificate.</summary>
    public RelayCommand DeleteCertificateCommand { get; }

    /// <summary>Deprecated: lists the zone-level certificates.</summary>
    public RelayCommand ListZoneCertificatesCommand { get; }

    /// <summary>Deprecated: reads one zone-level certificate.</summary>
    public RelayCommand GetZoneCertificateCommand { get; }

    /// <summary>Deprecated: uploads a certificate for the whole zone.</summary>
    public RelayCommand UploadZoneCertificateCommand { get; }

    /// <summary>Deprecated: removes a zone-level certificate.</summary>
    public RelayCommand DeleteZoneCertificateCommand { get; }

    /// <summary>The zone every call on this page uses, or <c>null</c> for the configured default.</summary>
    private string? Zone => Blank(ZoneOverride);

    private Task GetSettingsAsync() => RunAsync("Read the origin TLS client auth setting", async () =>
        await Client.OriginTlsClientAuth.GetSettingsAsync(Zone));

    private Task SetSettingsAsync(bool enabled) => RunAsync(
        enabled ? "Enable zone-level authenticated origin pulls" : "Disable zone-level authenticated origin pulls",
        async () => await Client.OriginTlsClientAuth.SetSettingsAsync(enabled, Zone));

    private Task GetHostnameAsync() => RunAsync("Read the hostname association", async () =>
        await Client.OriginTlsClientAuth.GetHostnameAsync(RequireHostname(), Zone));

    private Task SetHostnamesAsync() => RunAsync($"Apply '{AssociationState}' to the listed hostnames", async () =>
        await Client.OriginTlsClientAuth.SetHostnamesAsync(
            UpdateOriginTlsHostnameAssociationsRequest.For(Changes()),
            Zone));

    /// <summary>
    /// Drives the three single-hostname convenience methods, one per state, so each of them is a button
    /// rather than something only the batch call reaches.
    /// </summary>
    private Task ChangeHostnameAsync(string state) => RunAsync($"{state} {Hostname}", () => state switch
    {
        DisableState => Client.OriginTlsClientAuth.DisableHostnameAsync(
            RequireHostname(), RequireCertificateId(), Zone),
        VoidState => Client.OriginTlsClientAuth.VoidHostnameAsync(
            RequireHostname(), RequireCertificateId(), Zone),
        _ => Client.OriginTlsClientAuth.EnableHostnameAsync(
            RequireHostname(), RequireCertificateId(), Zone),
    });

    private Task ListCertificatesAsync() => RunAsync("List hostname client certificates", async () =>
        await Client.OriginTlsClientAuth.ListCertificatesAsync(Zone));

    private Task GetCertificateAsync() => RunAsync("Read a hostname client certificate", async () =>
        await Client.OriginTlsClientAuth.GetCertificateAsync(RequireCertificateId(), Zone));

    private Task UploadCertificateAsync() => RunAsync("Upload a hostname client certificate", async () =>
        await Client.OriginTlsClientAuth.UploadCertificateAsync(CertificateUpload(), Zone));

    private Task DeleteCertificateAsync() => RunAsync("Delete a hostname client certificate", async () =>
        await Client.OriginTlsClientAuth.DeleteCertificateAsync(RequireCertificateId(), Zone));

    private Task ListZoneCertificatesAsync() => RunAsync("List zone-level certificates (deprecated)", async () =>
        await Client.OriginTlsClientAuth.ListZoneCertificatesAsync(Zone));

    private Task GetZoneCertificateAsync() => RunAsync("Read a zone-level certificate (deprecated)", async () =>
        await Client.OriginTlsClientAuth.GetZoneCertificateAsync(RequireCertificateId(), Zone));

    private Task UploadZoneCertificateAsync() => RunAsync("Upload a zone-level certificate (deprecated)", async () =>
        await Client.OriginTlsClientAuth.UploadZoneCertificateAsync(CertificateUpload(), Zone));

    private Task DeleteZoneCertificateAsync() => RunAsync("Delete a zone-level certificate (deprecated)", async () =>
        await Client.OriginTlsClientAuth.DeleteZoneCertificateAsync(RequireCertificateId(), Zone));

    /// <summary>Turns the list box and the state picker into one <c>config</c> array.</summary>
    private OriginTlsHostnameAssociationChange[] Changes()
    {
        var names = Lines(Hostnames)
            ?? throw new ArgumentException("Enter one hostname per line, or use the single-hostname buttons.");

        return names.Select(name => Change(name, AssociationState)).ToArray();
    }

    /// <summary>One entry of the array, picked by the state the caller asked for.</summary>
    private OriginTlsHostnameAssociationChange Change(string hostname, string state)
    {
        var certificateId = RequireCertificateId();
        return state switch
        {
            DisableState => OriginTlsHostnameAssociationChange.Disable(hostname, certificateId),
            VoidState => OriginTlsHostnameAssociationChange.Void(hostname, certificateId),
            _ => OriginTlsHostnameAssociationChange.Enable(hostname, certificateId),
        };
    }

    /// <summary>Both halves of an upload, checked locally before anything is sent.</summary>
    private UploadOriginTlsCertificateRequest CertificateUpload() =>
        UploadOriginTlsCertificateRequest.Of(RequireCertificatePem(), RequirePrivateKeyPem());

    private string RequireHostname() =>
        Blank(Hostname) ?? throw new ArgumentException("Enter the hostname on the origin, for example app.example.com.");

    private string RequireCertificateId() =>
        Blank(CertificateId) ?? throw new ArgumentException("Enter a certificate id - list the certificates first.");

    private string RequireCertificatePem() =>
        Blank(CertificatePem) ?? throw new ArgumentException("Paste the client certificate in PEM.");

    private string RequirePrivateKeyPem() =>
        Blank(PrivateKeyPem) ?? throw new ArgumentException("Paste the private key that matches the certificate.");
}
