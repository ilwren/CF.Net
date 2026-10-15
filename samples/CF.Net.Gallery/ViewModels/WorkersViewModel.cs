using CFNet.Models.Workers;
using CFNet.Serialization;
using System.Text.Json;

namespace CFNet.Gallery.ViewModels;

/// <summary>
/// Workers scripts, their content and settings, deployments and the <c>workers.dev</c> subdomain.
/// </summary>
public sealed class WorkersViewModel : ModuleViewModelBase
{
    /// <summary>Starter source pre-filled so the upload button does something sensible.</summary>
    private const string DefaultSource = """
        export default {
          async fetch(request, env) {
            return new Response(`hello from CF.Net at ${new Date().toISOString()}`);
          },
        };
        """;

    private string _listPerPage = "20";
    private string _listName = string.Empty;
    private string _searchName = string.Empty;
    private string _searchId = string.Empty;
    private string _searchOrderBy = string.Empty;
    private string _scriptName = string.Empty;
    private string _uploadSource = DefaultSource;
    private string _compatibilityDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
    private string _bindingsJson = string.Empty;
    private string _versionId = string.Empty;
    private string _versionMessage = string.Empty;
    private string _cronExpressions = "*/30 * * * *";
    private string _percentage = "100";
    private string _subdomain = string.Empty;
    private bool _subdomainEnabled = true;
    private bool _scriptSubdomainEnabled = true;
    private string _domainId = string.Empty;
    private string _domainHostname = string.Empty;
    private string _domainZoneId = string.Empty;
    private bool _domainEnabled = true;
    private bool _domainPreviewsEnabled;

    /// <summary>Creates the page.</summary>
    /// <param name="gallery">Owning shell.</param>
    public WorkersViewModel(GalleryViewModel gallery)
        : base(gallery)
    {
        ListCommand = new RelayCommand(ListAsync);
        ListAllCommand = new RelayCommand(ListAllAsync);
        SearchCommand = new RelayCommand(SearchAsync);
        GetCommand = new RelayCommand(GetAsync);
        GetContentCommand = new RelayCommand(GetContentAsync);
        GetSettingsCommand = new RelayCommand(GetSettingsAsync);
        UploadCommand = new RelayCommand(UploadAsync);
        DeleteCommand = new RelayCommand(DeleteAsync);
        ListVersionsCommand = new RelayCommand(ListVersionsAsync);
        GetVersionCommand = new RelayCommand(GetVersionAsync);
        CreateVersionCommand = new RelayCommand(CreateVersionAsync);
        ListDeploymentsCommand = new RelayCommand(ListDeploymentsAsync);
        ListSchedulesCommand = new RelayCommand(ListSchedulesAsync);
        UpdateSchedulesCommand = new RelayCommand(UpdateSchedulesAsync);
        ClearSchedulesCommand = new RelayCommand(ClearSchedulesAsync);
        CreateDeploymentCommand = new RelayCommand(CreateDeploymentAsync);
        GetSubdomainCommand = new RelayCommand(GetSubdomainAsync);
        UpdateSubdomainCommand = new RelayCommand(UpdateSubdomainAsync);
        GetScriptSubdomainCommand = new RelayCommand(GetScriptSubdomainAsync);
        UpdateScriptSubdomainCommand = new RelayCommand(UpdateScriptSubdomainAsync);
        ListDomainsCommand = new RelayCommand(ListDomainsAsync);
        ListDomainsForWorkerCommand = new RelayCommand(ListDomainsForWorkerAsync);
        GetDomainCommand = new RelayCommand(GetDomainAsync);
        AttachDomainCommand = new RelayCommand(AttachDomainAsync);
        AttachDomainWithFlagsCommand = new RelayCommand(AttachDomainWithFlagsAsync);
        DeleteDomainCommand = new RelayCommand(DeleteDomainAsync);
    }

    /// <inheritdoc />
    public override string Title => "Workers";

    /// <inheritdoc />
    public override string Description =>
        "Scripts, module content, script settings, gradual deployments and the workers.dev subdomain.";

    /// <summary>Items per page.</summary>
    public string ListPerPage { get => _listPerPage; set => SetProperty(ref _listPerPage, value); }

    /// <summary>Name filter (substring).</summary>
    public string ListName { get => _listName; set => SetProperty(ref _listName, value); }

    /// <summary>Name to search for; exact and partial matches both come back.</summary>
    public string SearchName { get => _searchName; set => SetProperty(ref _searchName, value); }

    /// <summary>Worker id, also called a tag; only exact matches come back.</summary>
    public string SearchId { get => _searchId; set => SetProperty(ref _searchId, value); }

    /// <summary>Sort field: created_on, modified_on or name. Always ascending.</summary>
    public string SearchOrderBy { get => _searchOrderBy; set => SetProperty(ref _searchOrderBy, value); }

    /// <summary>Script every single-script call operates on.</summary>
    public string ScriptName { get => _scriptName; set => SetProperty(ref _scriptName, value); }

    /// <summary>JavaScript source for the upload.</summary>
    public string UploadSource { get => _uploadSource; set => SetProperty(ref _uploadSource, value); }

    /// <summary>Compatibility date, <c>yyyy-MM-dd</c>.</summary>
    public string CompatibilityDate { get => _compatibilityDate; set => SetProperty(ref _compatibilityDate, value); }

    /// <summary>
    /// Bindings as a JSON array, e.g.
    /// <c>[{"name":"GREETING","type":"plain_text","text":"hi"}]</c>.
    /// </summary>
    public string BindingsJson { get => _bindingsJson; set => SetProperty(ref _bindingsJson, value); }

    /// <summary>Version id to read or deploy.</summary>
    public string VersionId { get => _versionId; set => SetProperty(ref _versionId, value); }

    /// <summary>Cron expressions to set, separated by commas or new lines.</summary>
    public string CronExpressions { get => _cronExpressions; set => SetProperty(ref _cronExpressions, value); }

    /// <summary>Sent as the workers/message annotation on a version upload.</summary>
    public string VersionMessage { get => _versionMessage; set => SetProperty(ref _versionMessage, value); }

    /// <summary>Traffic percentage for the version.</summary>
    public string Percentage { get => _percentage; set => SetProperty(ref _percentage, value); }

    /// <summary>New <c>workers.dev</c> subdomain prefix.</summary>
    public string Subdomain { get => _subdomain; set => SetProperty(ref _subdomain, value); }

    /// <summary>Whether the <c>workers.dev</c> subdomain is enabled.</summary>
    public bool SubdomainEnabled { get => _subdomainEnabled; set => SetProperty(ref _subdomainEnabled, value); }

    /// <summary>Whether this script is published on the subdomain.</summary>
    public bool ScriptSubdomainEnabled { get => _scriptSubdomainEnabled; set => SetProperty(ref _scriptSubdomainEnabled, value); }

    /// <summary>Lists every custom domain attachment in the account.</summary>
    public RelayCommand ListDomainsCommand { get; }

    /// <summary>Lists the attachments that point at the script named above.</summary>
    public RelayCommand ListDomainsForWorkerCommand { get; }

    /// <summary>Reads one attachment by id.</summary>
    public RelayCommand GetDomainCommand { get; }

    /// <summary>Attaches the hostname to the script, the short form.</summary>
    public RelayCommand AttachDomainCommand { get; }

    /// <summary>Attaches it with the routing flags spelled out.</summary>
    public RelayCommand AttachDomainWithFlagsCommand { get; }

    /// <summary>Detaches a hostname.</summary>
    public RelayCommand DeleteDomainCommand { get; }

    /// <summary>Attachment id, as the list reports it.</summary>
    public string DomainId { get => _domainId; set => SetProperty(ref _domainId, value); }

    /// <summary>Hostname to attach, for example <c>app.example.com</c>.</summary>
    public string DomainHostname { get => _domainHostname; set => SetProperty(ref _domainHostname, value); }

    /// <summary>Zone the hostname lives in; blank uses the configured default.</summary>
    public string DomainZoneId { get => _domainZoneId; set => SetProperty(ref _domainZoneId, value); }

    /// <summary>Whether the attachment routes traffic.</summary>
    public bool DomainEnabled { get => _domainEnabled; set => SetProperty(ref _domainEnabled, value); }

    /// <summary>Whether preview deployments answer on this hostname.</summary>
    public bool DomainPreviewsEnabled { get => _domainPreviewsEnabled; set => SetProperty(ref _domainPreviewsEnabled, value); }

    /// <summary>Lists scripts.</summary>
    public RelayCommand ListCommand { get; }

    /// <summary>Walks every script page.</summary>
    public RelayCommand ListAllCommand { get; }

    /// <summary>Searches the account's scripts by id or name.</summary>
    public RelayCommand SearchCommand { get; }

    /// <summary>Gets one script.</summary>
    public RelayCommand GetCommand { get; }

    /// <summary>Downloads the script modules.</summary>
    public RelayCommand GetContentCommand { get; }

    /// <summary>Reads the script settings.</summary>
    public RelayCommand GetSettingsCommand { get; }

    /// <summary>Uploads (creates or replaces) a script.</summary>
    public RelayCommand UploadCommand { get; }

    /// <summary>Deletes a script.</summary>
    public RelayCommand DeleteCommand { get; }

    /// <summary>Lists a script's uploaded versions.</summary>
    public RelayCommand ListVersionsCommand { get; }

    /// <summary>Reads one version, including its resources.</summary>
    public RelayCommand GetVersionCommand { get; }

    /// <summary>Uploads a version without deploying it.</summary>
    public RelayCommand CreateVersionCommand { get; }

    /// <summary>Reads the script's cron triggers.</summary>
    public RelayCommand ListSchedulesCommand { get; }

    /// <summary>Replaces the script's cron triggers.</summary>
    public RelayCommand UpdateSchedulesCommand { get; }

    /// <summary>Removes every cron trigger from the script.</summary>
    public RelayCommand ClearSchedulesCommand { get; }

    /// <summary>Lists deployments.</summary>
    public RelayCommand ListDeploymentsCommand { get; }

    /// <summary>Creates a percentage deployment.</summary>
    public RelayCommand CreateDeploymentCommand { get; }

    /// <summary>Reads the account subdomain.</summary>
    public RelayCommand GetSubdomainCommand { get; }

    /// <summary>Renames or toggles the account subdomain.</summary>
    public RelayCommand UpdateSubdomainCommand { get; }

    /// <summary>Reads the script's subdomain state.</summary>
    public RelayCommand GetScriptSubdomainCommand { get; }

    /// <summary>Publishes or unpublishes the script on the subdomain.</summary>
    public RelayCommand UpdateScriptSubdomainCommand { get; }

    private Task ListAsync() => RunAsync("List worker scripts", async () =>
        await Client.Workers.ListAsync(new WorkerScriptListParameters
        {
            PerPage = OptionalInt(ListPerPage),
            Name = Blank(ListName),
        }));

    private Task ListAllAsync() => RunAsync("List every worker script", async () =>
    {
        var scripts = new List<WorkerScript>();
        await foreach (var script in Client.Workers.ListAllAsync())
        {
            scripts.Add(script);
        }

        return scripts;
    });

    private Task GetAsync() => RunAsync("Get worker script", async () =>
        await Client.Workers.GetAsync(Script()));

    private Task GetContentAsync() => RunAsync("Get worker content", async () =>
        await Client.Workers.GetContentAsync(Script()));

    private Task GetSettingsAsync() => RunAsync("Get worker settings", async () =>
        await Client.Workers.GetSettingsAsync(Script()));

    private Task UploadAsync() => RunAsync("Upload worker script", () =>
    {
        var request = new UploadWorkerScriptRequest(
            Script(),
            UploadSource ?? string.Empty,
            Blank(CompatibilityDate))
        {
            Bindings = ParseBindings(),
        };

        request.Validate();

        return Client.Workers.UploadAsync(request);
    });

    private Task SearchAsync() => RunAsync("Search worker scripts", async () =>
        await Client.Workers.SearchAsync(new WorkerScriptSearchParameters
        {
            Id = Blank(SearchId),
            Name = Blank(SearchName),
            OrderBy = Blank(SearchOrderBy),
            PerPage = OptionalInt(ListPerPage),
        }));

    private Task DeleteAsync() => RunVoidAsync("Delete worker script", () =>
        Client.Workers.DeleteAsync(Script()));

    private Task ListVersionsAsync() => RunAsync("List worker versions", async () =>
        await Client.Workers.ListVersionsAsync(
            Script(),
            new WorkerVersionListParameters { PerPage = OptionalInt(ListPerPage) }));

    private Task GetVersionAsync() => RunAsync("Get worker version", () =>
    {
        var versionId = Blank(VersionId)
            ?? throw new ArgumentException("Enter a version id - list the script's versions first.");

        return Client.Workers.GetVersionAsync(Script(), versionId);
    });

    private Task CreateVersionAsync() => RunAsync("Upload worker version", () =>
    {
        var request = new UploadWorkerScriptRequest(
            Script(),
            UploadSource ?? string.Empty,
            Blank(CompatibilityDate))
        {
            Annotations = string.IsNullOrWhiteSpace(VersionMessage)
                ? null
                : new WorkerAnnotations { Message = VersionMessage },
        };

        request.Validate();

        return Client.Workers.CreateVersionAsync(Script(), request);
    });

    private IEnumerable<string> ParseCronExpressions() =>
        (CronExpressions ?? string.Empty)
            .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    private Task ListSchedulesAsync() => RunAsync("Get worker cron triggers", async () =>
        await Client.Workers.ListSchedulesAsync(Script()));

    private Task UpdateSchedulesAsync() => RunAsync("Set worker cron triggers", () =>
        Client.Workers.UpdateSchedulesAsync(Script(), ParseCronExpressions()));

    private Task ClearSchedulesAsync() => RunAsync("Clear worker cron triggers", () =>
        Client.Workers.UpdateSchedulesAsync(Script(), Array.Empty<string>()));

    private Task ListDeploymentsAsync() => RunAsync("List worker deployments", async () =>
        await Client.Workers.ListDeploymentsAsync(Script(), new WorkerDeploymentListParameters
        {
            PerPage = OptionalInt(ListPerPage),
        }));

    private Task CreateDeploymentAsync() => RunAsync("Create worker deployment", () =>
    {
        var versionId = Blank(VersionId)
            ?? throw new ArgumentException("Enter a version id - list the script's versions first.");

        var request = new CreateWorkerDeploymentRequest
        {
            Versions = new List<WorkerDeploymentVersion>
            {
                new()
                {
                    VersionId = versionId,
                    Percentage = decimal.TryParse(Percentage, out var percentage) ? percentage : 100m,
                },
            },
        };

        return Client.Workers.CreateDeploymentAsync(Script(), request);
    });

    private Task GetSubdomainAsync() => RunAsync("Get workers.dev subdomain", () =>
        Client.Workers.GetSubdomainAsync());

    private Task UpdateSubdomainAsync() => RunAsync("Update workers.dev subdomain", () =>
        Client.Workers.UpdateSubdomainAsync(new UpdateWorkerSubdomainRequest
        {
            Subdomain = Blank(Subdomain),
            Enabled = SubdomainEnabled,
        }));

    private Task GetScriptSubdomainAsync() => RunAsync("Get script subdomain", () =>
        Client.Workers.GetScriptSubdomainAsync(Script()));

    private Task UpdateScriptSubdomainAsync() => RunAsync("Update script subdomain", () =>
        Client.Workers.UpdateScriptSubdomainAsync(Script(), new UpdateWorkerScriptSubdomainRequest
        {
            Enabled = ScriptSubdomainEnabled,
        }));

    private Task ListDomainsAsync() => RunAsync("List worker domains", async () =>
        await Client.Workers.ListDomainsAsync());

    private Task ListDomainsForWorkerAsync() => RunAsync("List this worker's domains", async () =>
        await Client.Workers.ListDomainsForWorkerAsync(Script()));

    private Task GetDomainAsync() => RunAsync("Get worker domain", async () =>
        await Client.Workers.GetDomainAsync(Domain()));

    private Task AttachDomainAsync() => RunAsync("Attach a hostname to this worker", async () =>
        await Client.Workers.AttachDomainAsync(Hostname(), Script(), Blank(DomainZoneId)));

    private Task AttachDomainWithFlagsAsync() => RunAsync("Attach a hostname with routing flags", () =>
        Client.Workers.AttachDomainAsync(new AttachWorkerDomainRequest
        {
            Hostname = Hostname(),
            Service = Script(),
            ZoneId = Blank(DomainZoneId),
            Enabled = DomainEnabled,
            PreviewsEnabled = DomainPreviewsEnabled,
        }));

    private Task DeleteDomainAsync() => RunVoidAsync("Delete worker domain", () =>
        Client.Workers.DeleteDomainAsync(Domain()));

    private string Domain() =>
        Blank(DomainId) ?? throw new ArgumentException("Enter a domain id - list the domains first.");

    private string Hostname() =>
        Blank(DomainHostname) ?? throw new ArgumentException("Enter a hostname, for example app.example.com.");

    private string Script() =>
        Blank(ScriptName) ?? throw new ArgumentException("Enter a script name.");

    private List<WorkerBinding>? ParseBindings()
    {
        var json = Blank(BindingsJson);
        if (json is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<WorkerBinding>>(json, CloudflareJson.Default);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"The bindings are not valid JSON: {exception.Message}", exception);
        }
    }
}
