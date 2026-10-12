using CFNet.Models.Ssl;

namespace CFNet.Gallery.ViewModels;

/// <summary>
/// SSL: certificate packs, what it takes to get one issued, Universal SSL, the enrolment settings Cloudflare
/// files under SSL but serves from the zone settings path, the origin TLS compliance modes that constrain
/// what Auto-Origin TLS KEX may pick, and the SSL for SaaS custom hostnames that sit beside them - including
/// the zone-wide fallback origin they share.
/// </summary>
public sealed class SslViewModel : ModuleViewModelBase
{
    private string _zoneOverride = string.Empty;

    private string _hostnameId = string.Empty;
    private string _hostname = string.Empty;
    private string _hostnameDcvMethod = CustomHostnameDcvMethods.Http;
    private bool _hostnameWildcard;
    private string _hostnameContains = string.Empty;
    private string _hostnameStatus = string.Empty;
    private string _hostnamePerPage = "20";
    private string _hostnameOrigin = string.Empty;
    private string _complianceModes = OriginTlsComplianceModeValues.Fips;
    private string _fallbackOrigin = string.Empty;

    private string _listDeploy = string.Empty;
    private string _listStatus = string.Empty;
    private string _listPerPage = "20";
    private string _listPage = "1";

    private string _packId = string.Empty;

    private string _orderHosts = string.Empty;
    private string _orderValidationMethod = SslValidationMethods.Txt;
    private string _orderValidityDays = "365";
    private string _orderAuthority = SslCertificateAuthorities.LetsEncrypt;
    private string _orderBranding = string.Empty;

    private string _newValidationMethod = SslValidationMethods.Txt;
    private bool _verificationRetry;

    private string _analyzePem = string.Empty;
    private string _analyzeBundleMethod = string.Empty;

    private string _automaticMode = SslAutomaticModes.Auto;

    /// <summary>Creates the page.</summary>
    /// <param name="gallery">Owning shell.</param>
    public SslViewModel(GalleryViewModel gallery)
        : base(gallery)
    {
        ListCommand = new RelayCommand(ListAsync);
        ListAllCommand = new RelayCommand(ListAllAsync);
        GetPackCommand = new RelayCommand(GetPackAsync);
        DeletePackCommand = new RelayCommand(DeletePackAsync);

        OrderCommand = new RelayCommand(OrderAsync);
        BrandingOnCommand = new RelayCommand(() => SetBrandingAsync(true));
        BrandingOffCommand = new RelayCommand(() => SetBrandingAsync(false));
        RestartValidationCommand = new RelayCommand(RestartValidationAsync);

        GetVerificationCommand = new RelayCommand(GetVerificationAsync);
        UpdateValidationMethodCommand = new RelayCommand(UpdateValidationMethodAsync);
        RevalidateCommand = new RelayCommand(RevalidateAsync);

        GetQuotaCommand = new RelayCommand(GetQuotaAsync);
        GetRecommendationCommand = new RelayCommand(GetRecommendationAsync);
        AnalyzeCommand = new RelayCommand(AnalyzeAsync);

        GetUniversalCommand = new RelayCommand(GetUniversalAsync);
        UniversalOnCommand = new RelayCommand(() => SetUniversalAsync(true));
        UniversalOffCommand = new RelayCommand(() => SetUniversalAsync(false));

        GetKexCommand = new RelayCommand(GetKexAsync);
        KexOnCommand = new RelayCommand(() => SetKexAsync(true));
        KexOffCommand = new RelayCommand(() => SetKexAsync(false));

        GetAutomaticCommand = new RelayCommand(GetAutomaticAsync);
        SetAutomaticCommand = new RelayCommand(SetAutomaticAsync);
        AutomaticAutoCommand = new RelayCommand(() => EnableAutomaticAsync());
        AutomaticCustomCommand = new RelayCommand(() => DisableAutomaticAsync());
        ListHostnamesCommand = new RelayCommand(ListHostnamesAsync);
        ListAllHostnamesCommand = new RelayCommand(ListAllHostnamesAsync);
        GetHostnameCommand = new RelayCommand(GetHostnameAsync);
        CreateHostnameCommand = new RelayCommand(CreateHostnameAsync);
        RetryHostnameValidationCommand = new RelayCommand(RetryHostnameValidationAsync);
        SetHostnameOriginCommand = new RelayCommand(SetHostnameOriginAsync);
        DeleteHostnameCommand = new RelayCommand(DeleteHostnameAsync);

        GetComplianceModesCommand = new RelayCommand(GetComplianceModesAsync);
        ReplaceComplianceModesCommand = new RelayCommand(ReplaceComplianceModesAsync);
        ChangeComplianceModesCommand = new RelayCommand(ChangeComplianceModesAsync);
        ClearComplianceModesCommand = new RelayCommand(ClearComplianceModesAsync);

        GetFallbackOriginCommand = new RelayCommand(GetFallbackOriginAsync);
        SetFallbackOriginCommand = new RelayCommand(SetFallbackOriginAsync);
        DeleteFallbackOriginCommand = new RelayCommand(DeleteFallbackOriginAsync);
    }

    /// <inheritdoc />
    public override string Title => "SSL";

    /// <inheritdoc />
    public override string Description =>
        "Certificate packs and how they get issued, Universal SSL, validation details, the enrolment settings Cloudflare serves from /zones/{zone_id}/settings, the origin TLS compliance modes that bound them, and the SSL for SaaS custom hostnames with their zone-wide fallback origin.";

    /// <summary><c>deploy</c> values offered in the list filter; empty sends none.</summary>
    public IReadOnlyList<string> Deploys { get; } = new[]
    {
        string.Empty,
        SslCertificatePackDeployments.Staging,
        SslCertificatePackDeployments.Production,
    };

    /// <summary><c>status</c> values offered in the list filter; empty sends none.</summary>
    public IReadOnlyList<string> Statuses { get; } = new[] { string.Empty, "all" };

    /// <summary>Validation methods an order accepts. Note the absence of <c>cname</c>.</summary>
    public IReadOnlyList<string> OrderValidationMethods { get; } = SslValidationMethods.All;

    /// <summary>
    /// Validation methods the verification PATCH accepts - one more than an order does, because
    /// <c>cname</c> (DCV delegation) can only be switched to, never ordered with.
    /// </summary>
    public IReadOnlyList<string> ValidationMethods { get; } = new[]
    {
        SslValidationMethods.Txt,
        SslValidationMethods.Http,
        SslValidationMethods.Email,
        SslValidationMethods.Cname,
    };

    /// <summary>Certificate authorities an order accepts.</summary>
    public IReadOnlyList<string> Authorities { get; } = SslCertificateAuthorities.All;

    /// <summary>Validity periods an order accepts, as text for the picker.</summary>
    public IReadOnlyList<string> ValidityDays { get; } = new[] { "14", "30", "90", "365" };

    /// <summary>Cloudflare branding choices; empty leaves the field out of the body entirely.</summary>
    public IReadOnlyList<string> BrandingChoices { get; } = new[] { string.Empty, "true", "false" };

    /// <summary>Bundle methods for certificate analysis; empty leaves the field out.</summary>
    public IReadOnlyList<string> BundleMethods { get; } = new[]
    {
        string.Empty,
        SslBundleMethods.Ubiquitous,
        SslBundleMethods.Optimal,
        SslBundleMethods.Force,
    };

    /// <summary>Automatic SSL/TLS enrolment values.</summary>
    public IReadOnlyList<string> AutomaticModes { get; } = SslAutomaticModes.All;

    /// <summary>Lists the SSL for SaaS custom hostnames of the zone.</summary>
    public RelayCommand ListHostnamesCommand { get; }

    /// <summary>Walks every page of them.</summary>
    public RelayCommand ListAllHostnamesCommand { get; }

    /// <summary>Reads one, with its challenge and certificate state.</summary>
    public RelayCommand GetHostnameCommand { get; }

    /// <summary>Adds one and asks for a certificate.</summary>
    public RelayCommand CreateHostnameCommand { get; }

    /// <summary>Asks Cloudflare to try domain control validation again.</summary>
    public RelayCommand RetryHostnameValidationCommand { get; }

    /// <summary>Points one at a different origin.</summary>
    public RelayCommand SetHostnameOriginCommand { get; }

    /// <summary>Removes one, and its certificate.</summary>
    public RelayCommand DeleteHostnameCommand { get; }

    /// <summary>Reads the origin TLS compliance modes.</summary>
    public RelayCommand GetComplianceModesCommand { get; }

    /// <summary>Replaces them with <c>PUT</c>.</summary>
    public RelayCommand ReplaceComplianceModesCommand { get; }

    /// <summary>Replaces them with <c>PATCH</c>, which is a full replace too.</summary>
    public RelayCommand ChangeComplianceModesCommand { get; }

    /// <summary>Clears the constraint with <c>DELETE</c>.</summary>
    public RelayCommand ClearComplianceModesCommand { get; }

    /// <summary>Reads the zone-wide fallback origin for custom hostnames.</summary>
    public RelayCommand GetFallbackOriginCommand { get; }

    /// <summary>Sets it with <c>PUT</c>.</summary>
    public RelayCommand SetFallbackOriginCommand { get; }

    /// <summary>Removes it.</summary>
    public RelayCommand DeleteFallbackOriginCommand { get; }

    /// <summary>Id of the custom hostname the single-hostname calls operate on.</summary>
    public string HostnameId { get => _hostnameId; set => SetProperty(ref _hostnameId, value); }

    /// <summary>Hostname to add, on a domain the customer controls.</summary>
    public string Hostname { get => _hostname; set => SetProperty(ref _hostname, value); }

    /// <summary>Validation method to ask for.</summary>
    public string HostnameDcvMethod { get => _hostnameDcvMethod; set => SetProperty(ref _hostnameDcvMethod, value); }

    /// <summary>Whether the certificate should cover a wildcard.</summary>
    public bool HostnameWildcard { get => _hostnameWildcard; set => SetProperty(ref _hostnameWildcard, value); }

    /// <summary>Substring filter, sent as <c>hostname.contain</c>.</summary>
    public string HostnameContains { get => _hostnameContains; set => SetProperty(ref _hostnameContains, value); }

    /// <summary>Activation status filter; blank sends none.</summary>
    public string HostnameStatus { get => _hostnameStatus; set => SetProperty(ref _hostnameStatus, value); }

    /// <summary>Items per page, 5 to 1000 on this endpoint.</summary>
    public string HostnamePerPage { get => _hostnamePerPage; set => SetProperty(ref _hostnamePerPage, value); }

    /// <summary>Origin to route the hostname to.</summary>
    public string HostnameOrigin { get => _hostnameOrigin; set => SetProperty(ref _hostnameOrigin, value); }

    /// <summary>
    /// Compliance modes to put in force, comma separated: <c>fips</c> and <c>pqh</c> today, and Cloudflare
    /// says to treat anything it adds later as an opaque string. Empty sends an empty list, which clears the
    /// constraint.
    /// </summary>
    public string ComplianceModes { get => _complianceModes; set => SetProperty(ref _complianceModes, value); }

    /// <summary>Origin hostname that custom hostnames without one of their own fall back to.</summary>
    public string FallbackOrigin { get => _fallbackOrigin; set => SetProperty(ref _fallbackOrigin, value); }

    /// <summary>The validation methods a custom hostname can use - no <c>cname</c> among them.</summary>
    public IReadOnlyList<string> DcvMethods { get; } = CustomHostnameDcvMethods.All;

    /// <summary>The activation statuses the list can be filtered by, plus blank for "any".</summary>
    public IReadOnlyList<string> HostnameStatuses { get; } =
        new[] { string.Empty }.Concat(CustomHostnameStatuses.All).ToList();

    /// <summary>Zone id used by every call on this page; blank means the one from the credentials page.</summary>
    public string ZoneOverride { get => _zoneOverride; set => SetProperty(ref _zoneOverride, value); }

    /// <summary><c>deploy</c> filter for the pack list.</summary>
    public string ListDeploy { get => _listDeploy; set => SetProperty(ref _listDeploy, value); }

    /// <summary><c>status</c> filter for the pack list.</summary>
    public string ListStatus { get => _listStatus; set => SetProperty(ref _listStatus, value); }

    /// <summary>Items per page for the pack list (5-50).</summary>
    public string ListPerPage { get => _listPerPage; set => SetProperty(ref _listPerPage, value); }

    /// <summary>Page number for the pack list.</summary>
    public string ListPage { get => _listPage; set => SetProperty(ref _listPage, value); }

    /// <summary>Certificate pack UUID used by the get, update, delete and verification calls.</summary>
    public string PackId { get => _packId; set => SetProperty(ref _packId, value); }

    /// <summary>Hostnames to cover, one per line.</summary>
    public string OrderHosts { get => _orderHosts; set => SetProperty(ref _orderHosts, value); }

    /// <summary>Validation method for a new order.</summary>
    public string OrderValidationMethod { get => _orderValidationMethod; set => SetProperty(ref _orderValidationMethod, value); }

    /// <summary>Validity period for a new order.</summary>
    public string OrderValidityDays { get => _orderValidityDays; set => SetProperty(ref _orderValidityDays, value); }

    /// <summary>Certificate authority for a new order.</summary>
    public string OrderAuthority { get => _orderAuthority; set => SetProperty(ref _orderAuthority, value); }

    /// <summary>Cloudflare branding for a new order; empty omits the field.</summary>
    public string OrderBranding { get => _orderBranding; set => SetProperty(ref _orderBranding, value); }

    /// <summary>Method to switch a pack to, which also validates with it immediately.</summary>
    public string NewValidationMethod { get => _newValidationMethod; set => SetProperty(ref _newValidationMethod, value); }

    /// <summary>Whether reading verification details should make Cloudflare re-run the check now.</summary>
    public bool VerificationRetry { get => _verificationRetry; set => SetProperty(ref _verificationRetry, value); }

    /// <summary>PEM to analyze, intermediates included.</summary>
    public string AnalyzePem { get => _analyzePem; set => SetProperty(ref _analyzePem, value); }

    /// <summary>Bundle method for the analysis; empty omits the field.</summary>
    public string AnalyzeBundleMethod { get => _analyzeBundleMethod; set => SetProperty(ref _analyzeBundleMethod, value); }

    /// <summary>Enrolment value to set for Automatic SSL/TLS.</summary>
    public string AutomaticMode { get => _automaticMode; set => SetProperty(ref _automaticMode, value); }

    /// <summary>Lists one page of certificate packs.</summary>
    public RelayCommand ListCommand { get; }

    /// <summary>Walks every page of certificate packs.</summary>
    public RelayCommand ListAllCommand { get; }

    /// <summary>Reads one certificate pack.</summary>
    public RelayCommand GetPackCommand { get; }

    /// <summary>Deletes an Advanced Certificate Manager pack.</summary>
    public RelayCommand DeletePackCommand { get; }

    /// <summary>Orders an Advanced Certificate Manager pack.</summary>
    public RelayCommand OrderCommand { get; }

    /// <summary>Turns Cloudflare branding on for a pack.</summary>
    public RelayCommand BrandingOnCommand { get; }

    /// <summary>Turns Cloudflare branding off for a pack.</summary>
    public RelayCommand BrandingOffCommand { get; }

    /// <summary>Restarts validation of a pack by PATCHing it with an empty body.</summary>
    public RelayCommand RestartValidationCommand { get; }

    /// <summary>Reads the zone's SSL verification details.</summary>
    public RelayCommand GetVerificationCommand { get; }

    /// <summary>Switches a pack's validation method and validates with it now.</summary>
    public RelayCommand UpdateValidationMethodCommand { get; }

    /// <summary>Asks for an immediate re-check of the method already in use.</summary>
    public RelayCommand RevalidateCommand { get; }

    /// <summary>Reads the certificate pack quota.</summary>
    public RelayCommand GetQuotaCommand { get; }

    /// <summary>Reads the deprecated SSL/TLS recommendation.</summary>
    public RelayCommand GetRecommendationCommand { get; }

    /// <summary>Inspects a certificate without uploading it.</summary>
    public RelayCommand AnalyzeCommand { get; }

    /// <summary>Reads the Universal SSL switch.</summary>
    public RelayCommand GetUniversalCommand { get; }

    /// <summary>Turns Universal SSL on.</summary>
    public RelayCommand UniversalOnCommand { get; }

    /// <summary>Turns Universal SSL off.</summary>
    public RelayCommand UniversalOffCommand { get; }

    /// <summary>Reads Auto-Origin TLS KEX enrolment.</summary>
    public RelayCommand GetKexCommand { get; }

    /// <summary>Enables Auto-Origin TLS KEX.</summary>
    public RelayCommand KexOnCommand { get; }

    /// <summary>Disables Auto-Origin TLS KEX.</summary>
    public RelayCommand KexOffCommand { get; }

    /// <summary>Reads Automatic SSL/TLS enrolment.</summary>
    public RelayCommand GetAutomaticCommand { get; }

    /// <summary>Sends whatever the picker says, through <c>SetAutomaticSslAsync</c>.</summary>
    public RelayCommand SetAutomaticCommand { get; }

    /// <summary>Enrols the zone through the convenience method, which sends <c>auto</c>.</summary>
    public RelayCommand AutomaticAutoCommand { get; }

    /// <summary>Opts the zone out through the convenience method, which sends <c>custom</c>.</summary>
    public RelayCommand AutomaticCustomCommand { get; }

    /// <summary>The zone every call on this page uses, or <c>null</c> for the configured default.</summary>
    private string? Zone => Blank(ZoneOverride);

    private Task ListAsync() => RunAsync("List certificate packs", async () =>
        await Client.Ssl.ListAsync(new SslCertificatePackListParameters
        {
            Deploy = Blank(ListDeploy),
            Status = Blank(ListStatus),
            Page = OptionalInt(ListPage),
            PerPage = OptionalInt(ListPerPage),
        }, Zone));

    private Task ListAllAsync() => RunAsync("List every certificate pack", async () =>
    {
        var packs = new List<SslCertificatePack>();
        await foreach (var pack in Client.Ssl.ListAllAsync(new SslCertificatePackListParameters { PerPage = 50 }, Zone))
        {
            packs.Add(pack);
        }

        return packs;
    });

    private Task GetPackAsync() => RunAsync("Get certificate pack", async () =>
        await Client.Ssl.GetAsync(RequirePackId(), Zone));

    private Task DeletePackAsync() => RunAsync("Delete certificate pack", async () =>
        await Client.Ssl.DeleteAsync(RequirePackId(), Zone));

    private Task OrderAsync() => RunAsync("Order certificate pack", async () =>
    {
        var hosts = Lines(OrderHosts)
            ?? throw new ArgumentException("Enter at least one hostname, and include the zone apex.");

        return await Client.Ssl.OrderAsync(new OrderSslCertificatePackRequest(
            hosts,
            OrderValidationMethod,
            OptionalInt(OrderValidityDays) ?? throw new ArgumentException("Pick a validity period."),
            OrderAuthority,
            OptionalBool(OrderBranding)), Zone);
    });

    private Task SetBrandingAsync(bool enabled) => RunAsync(
        enabled ? "Turn Cloudflare branding on" : "Turn Cloudflare branding off",
        async () => await Client.Ssl.SetCloudflareBrandingAsync(RequirePackId(), enabled, Zone));

    private Task RestartValidationAsync() => RunAsync("Restart pack validation", async () =>
        await Client.Ssl.RestartValidationAsync(RequirePackId(), Zone));

    private Task GetVerificationAsync() => RunAsync("Read verification details", async () =>
        await Client.Ssl.GetVerificationAsync(VerificationRetry, Zone));

    private Task UpdateValidationMethodAsync() => RunAsync("Update validation method", async () =>
        await Client.Ssl.UpdateValidationMethodAsync(RequirePackId(), NewValidationMethod, Zone));

    private Task RevalidateAsync() => RunAsync("Revalidate now", async () =>
        await Client.Ssl.RevalidateAsync(RequirePackId(), Zone));

    private Task GetQuotaAsync() => RunAsync("Read certificate pack quota", async () =>
        await Client.Ssl.GetQuotaAsync(Zone));

    private Task GetRecommendationAsync() => RunAsync("Read SSL/TLS recommendation", async () =>
        await Client.Ssl.GetRecommendationAsync(Zone));

    private Task AnalyzeAsync() => RunAsync("Analyze certificate", async () =>
        await Client.Ssl.AnalyzeCertificateAsync(AnalyzeSslCertificateRequest.ForCertificate(
            AnalyzePem,
            Blank(AnalyzeBundleMethod)), Zone));

    private Task GetUniversalAsync() => RunAsync("Read Universal SSL", async () =>
        await Client.Ssl.GetUniversalSettingsAsync(Zone));

    private Task SetUniversalAsync(bool enabled) => RunAsync(
        enabled ? "Turn Universal SSL on" : "Turn Universal SSL off",
        async () => await Client.Ssl.SetUniversalSslAsync(enabled, Zone));

    private Task GetKexAsync() => RunAsync("Read Auto-Origin TLS KEX", async () =>
        await Client.Ssl.GetAutoOriginTlsKexAsync(Zone));

    private Task SetKexAsync(bool enabled) => RunAsync(
        enabled ? "Enable Auto-Origin TLS KEX" : "Disable Auto-Origin TLS KEX",
        async () => await Client.Ssl.SetAutoOriginTlsKexAsync(enabled, Zone));

    private Task GetAutomaticAsync() => RunAsync("Read Automatic SSL/TLS", async () =>
        await Client.Ssl.GetAutomaticSslAsync(Zone));

    private Task SetAutomaticAsync() => RunAsync($"Set Automatic SSL/TLS to {AutomaticMode}", async () =>
        await Client.Ssl.SetAutomaticSslAsync(AutomaticMode, Zone));

    private Task EnableAutomaticAsync() => RunAsync("Enrol in Automatic SSL/TLS", async () =>
        await Client.Ssl.EnableAutomaticSslAsync(Zone));

    private Task DisableAutomaticAsync() => RunAsync("Opt out of Automatic SSL/TLS", async () =>
        await Client.Ssl.DisableAutomaticSslAsync(Zone));

    private Task ListHostnamesAsync() => RunAsync("List custom hostnames", async () =>
        await Client.CustomHostnames.ListAsync(new CustomHostnameListParameters
        {
            PerPage = OptionalInt(HostnamePerPage),
            HostnameContains = Blank(HostnameContains),
            HostnameStatus = Blank(HostnameStatus),
        }, Zone));

    private Task ListAllHostnamesAsync() => RunAsync("List every custom hostname", async () =>
    {
        var hostnames = new List<CustomHostname>();
        await foreach (var hostname in Client.CustomHostnames.ListAllAsync(new CustomHostnameListParameters
        {
            PerPage = OptionalInt(HostnamePerPage),
            HostnameContains = Blank(HostnameContains),
            HostnameStatus = Blank(HostnameStatus),
        }, Zone))
        {
            hostnames.Add(hostname);
        }

        return hostnames;
    });

    private Task GetHostnameAsync() => RunAsync("Get custom hostname", async () =>
        await Client.CustomHostnames.GetAsync(RequireHostnameId(), Zone));

    private Task CreateHostnameAsync() => RunAsync("Create custom hostname", async () =>
        await Client.CustomHostnames.CreateAsync(
            RequireHostname(),
            HostnameDcvMethod,
            HostnameWildcard,
            Zone));

    private Task RetryHostnameValidationAsync() => RunAsync("Retry custom hostname validation", async () =>
        await Client.CustomHostnames.RetryValidationAsync(RequireHostnameId(), HostnameDcvMethod, Zone));

    private Task SetHostnameOriginAsync() => RunAsync("Set the custom hostname's origin", async () =>
        await Client.CustomHostnames.UpdateAsync(
            RequireHostnameId(),
            new UpdateCustomHostnameRequest
            {
                CustomOriginServer = Blank(HostnameOrigin)
                    ?? throw new ArgumentException("Enter the origin to route this hostname to."),
            },
            Zone));

    private Task DeleteHostnameAsync() => RunVoidAsync("Delete custom hostname", () =>
        Client.CustomHostnames.DeleteAsync(RequireHostnameId(), Zone));

    private Task GetComplianceModesAsync() => RunAsync("Read origin TLS compliance modes", async () =>
        await Client.Ssl.GetOriginTlsComplianceModesAsync(Zone));

    private Task ReplaceComplianceModesAsync() => RunAsync(
        $"Replace origin TLS compliance modes with {ComplianceModes}",
        async () => await Client.Ssl.ReplaceOriginTlsComplianceModesAsync(Modes(), Zone));

    private Task ChangeComplianceModesAsync() => RunAsync(
        $"Change origin TLS compliance modes to {ComplianceModes}",
        async () => await Client.Ssl.ChangeOriginTlsComplianceModesAsync(Modes(), Zone));

    private Task ClearComplianceModesAsync() => RunAsync("Clear the origin TLS compliance modes", async () =>
        await Client.Ssl.ClearOriginTlsComplianceModesAsync(Zone));

    private Task GetFallbackOriginAsync() => RunAsync("Read the fallback origin", async () =>
        await Client.CustomHostnames.GetFallbackOriginAsync(Zone));

    private Task SetFallbackOriginAsync() => RunAsync($"Set the fallback origin to {FallbackOrigin}", async () =>
        await Client.CustomHostnames.SetFallbackOriginAsync(
            UpdateCustomHostnameFallbackOriginRequest.For(RequireFallbackOrigin()),
            Zone));

    private Task DeleteFallbackOriginAsync() => RunVoidAsync("Delete the fallback origin", () =>
        Client.CustomHostnames.DeleteFallbackOriginAsync(Zone));

    /// <summary>Turns the picker text into the complete replacement list both write verbs expect.</summary>
    private UpdateOriginTlsComplianceModesRequest Modes() =>
        UpdateOriginTlsComplianceModesRequest.With(
            ComplianceModes
                .Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private string RequireHostnameId() =>
        Blank(HostnameId) ?? throw new ArgumentException("Enter a custom hostname id - list them first.");

    private string RequireHostname() =>
        Blank(Hostname) ?? throw new ArgumentException("Enter the hostname to add, for example shop.customer.com.");

    private string RequireFallbackOrigin() =>
        Blank(FallbackOrigin) ?? throw new ArgumentException("Enter the origin hostname to fall back to.");

    private string RequirePackId() =>
        Blank(PackId) ?? throw new ArgumentException("Enter a certificate pack UUID.");
}
