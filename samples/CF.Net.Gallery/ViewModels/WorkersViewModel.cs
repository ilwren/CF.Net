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
    private string _scriptName = string.Empty;
    private string _uploadSource = DefaultSource;
    private string _compatibilityDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
    private string _bindingsJson = string.Empty;
    private string _versionId = string.Empty;
    private string _percentage = "100";
    private string _subdomain = string.Empty;
    private bool _subdomainEnabled = true;
    private bool _scriptSubdomainEnabled = true;

    /// <summary>Creates the page.</summary>
    /// <param name="gallery">Owning shell.</param>
    public WorkersViewModel(GalleryViewModel gallery)
        : base(gallery)
    {
        ListCommand = new RelayCommand(ListAsync);
        ListAllCommand = new RelayCommand(ListAllAsync);
        GetCommand = new RelayCommand(GetAsync);
        GetContentCommand = new RelayCommand(GetContentAsync);
        GetSettingsCommand = new RelayCommand(GetSettingsAsync);
        UploadCommand = new RelayCommand(UploadAsync);
        DeleteCommand = new RelayCommand(DeleteAsync);
        ListDeploymentsCommand = new RelayCommand(ListDeploymentsAsync);
        CreateDeploymentCommand = new RelayCommand(CreateDeploymentAsync);
        GetSubdomainCommand = new RelayCommand(GetSubdomainAsync);
        UpdateSubdomainCommand = new RelayCommand(UpdateSubdomainAsync);
        GetScriptSubdomainCommand = new RelayCommand(GetScriptSubdomainAsync);
        UpdateScriptSubdomainCommand = new RelayCommand(UpdateScriptSubdomainAsync);
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

    /// <summary>Version id to deploy.</summary>
    public string VersionId { get => _versionId; set => SetProperty(ref _versionId, value); }

    /// <summary>Traffic percentage for the version.</summary>
    public string Percentage { get => _percentage; set => SetProperty(ref _percentage, value); }

    /// <summary>New <c>workers.dev</c> subdomain prefix.</summary>
    public string Subdomain { get => _subdomain; set => SetProperty(ref _subdomain, value); }

    /// <summary>Whether the <c>workers.dev</c> subdomain is enabled.</summary>
    public bool SubdomainEnabled { get => _subdomainEnabled; set => SetProperty(ref _subdomainEnabled, value); }

    /// <summary>Whether this script is published on the subdomain.</summary>
    public bool ScriptSubdomainEnabled { get => _scriptSubdomainEnabled; set => SetProperty(ref _scriptSubdomainEnabled, value); }

    /// <summary>Lists scripts.</summary>
    public RelayCommand ListCommand { get; }

    /// <summary>Walks every script page.</summary>
    public RelayCommand ListAllCommand { get; }

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

    private Task DeleteAsync() => RunVoidAsync("Delete worker script", () =>
        Client.Workers.DeleteAsync(Script()));

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
                    Percentage = double.TryParse(Percentage, out var percentage) ? percentage : 100d,
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
