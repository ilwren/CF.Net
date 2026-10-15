using CFNet.Models.Pages;

namespace CFNet.Gallery.ViewModels;

/// <summary>
/// Pages projects, their deployments and their custom domains.
/// </summary>
public sealed class PagesViewModel : ModuleViewModelBase
{
    private string _listPerPage = "20";
    private string _listSearch = string.Empty;
    private string _projectName = string.Empty;
    private string _productionBranch = "main";
    private string _deploymentId = string.Empty;
    private string _deploymentBranch = string.Empty;
    private string _deploymentEnv = string.Empty;
    private string _domainName = string.Empty;
    private bool _domainVerified = true;

    /// <summary>Creates the page.</summary>
    /// <param name="gallery">Owning shell.</param>
    public PagesViewModel(GalleryViewModel gallery)
        : base(gallery)
    {
        ListCommand = new RelayCommand(ListAsync);
        ListAllCommand = new RelayCommand(ListAllAsync);
        GetCommand = new RelayCommand(GetAsync);
        CreateCommand = new RelayCommand(CreateAsync);
        UpdateCommand = new RelayCommand(UpdateAsync);
        DeleteCommand = new RelayCommand(DeleteAsync);
        ListDeploymentsCommand = new RelayCommand(ListDeploymentsAsync);
        GetDeploymentCommand = new RelayCommand(GetDeploymentAsync);
        CreateDeploymentCommand = new RelayCommand(CreateDeploymentAsync);
        DeleteDeploymentCommand = new RelayCommand(DeleteDeploymentAsync);
        RetryDeploymentCommand = new RelayCommand(RetryDeploymentAsync);
        RollbackDeploymentCommand = new RelayCommand(RollbackDeploymentAsync);
        GetDeploymentLogsCommand = new RelayCommand(GetDeploymentLogsAsync);
        GetUploadTokenCommand = new RelayCommand(GetUploadTokenAsync);
        PurgeBuildCacheCommand = new RelayCommand(PurgeBuildCacheAsync);
        GetDomainCommand = new RelayCommand(GetDomainAsync);
        ListDomainsCommand = new RelayCommand(ListDomainsAsync);
        AddDomainCommand = new RelayCommand(AddDomainAsync);
        VerifyDomainCommand = new RelayCommand(VerifyDomainAsync);
        DeleteDomainCommand = new RelayCommand(DeleteDomainAsync);
    }

    /// <inheritdoc />
    public override string Title => "Pages";

    /// <inheritdoc />
    public override string Description =>
        "Projects, deployments and custom domains for Cloudflare Pages.";

    /// <summary>Items per page.</summary>
    public string ListPerPage { get => _listPerPage; set => SetProperty(ref _listPerPage, value); }

    /// <summary>Project name filter.</summary>
    public string ListSearch { get => _listSearch; set => SetProperty(ref _listSearch, value); }

    /// <summary>Project every single-project call operates on.</summary>
    public string ProjectName { get => _projectName; set => SetProperty(ref _projectName, value); }

    /// <summary>Production branch used when creating or updating a project.</summary>
    public string ProductionBranch { get => _productionBranch; set => SetProperty(ref _productionBranch, value); }

    /// <summary>Deployment id for the get / delete calls.</summary>
    public string DeploymentId { get => _deploymentId; set => SetProperty(ref _deploymentId, value); }

    /// <summary>Branch for a new direct-upload deployment.</summary>
    public string DeploymentBranch { get => _deploymentBranch; set => SetProperty(ref _deploymentBranch, value); }

    /// <summary>Environment filter for the deployment list; blank sends none.</summary>
    public string DeploymentEnv { get => _deploymentEnv; set => SetProperty(ref _deploymentEnv, value); }

    /// <summary>The <c>env</c> values the deployment list accepts, plus blank for "both".</summary>
    public IReadOnlyList<string> Environments { get; } = new[]
    {
        string.Empty,
        PagesDeploymentEnvironments.Production,
        PagesDeploymentEnvironments.Preview,
    };

    /// <summary>Custom domain name.</summary>
    public string DomainName { get => _domainName; set => SetProperty(ref _domainName, value); }

    /// <summary>Whether the custom domain should be marked verified.</summary>
    public bool DomainVerified { get => _domainVerified; set => SetProperty(ref _domainVerified, value); }

    /// <summary>Lists projects.</summary>
    public RelayCommand ListCommand { get; }

    /// <summary>Walks every project page.</summary>
    public RelayCommand ListAllCommand { get; }

    /// <summary>Gets one project.</summary>
    public RelayCommand GetCommand { get; }

    /// <summary>Creates a project.</summary>
    public RelayCommand CreateCommand { get; }

    /// <summary>Changes the production branch.</summary>
    public RelayCommand UpdateCommand { get; }

    /// <summary>Deletes a project.</summary>
    public RelayCommand DeleteCommand { get; }

    /// <summary>Lists deployments.</summary>
    public RelayCommand ListDeploymentsCommand { get; }

    /// <summary>Gets one deployment.</summary>
    public RelayCommand GetDeploymentCommand { get; }

    /// <summary>Creates a deployment.</summary>
    public RelayCommand CreateDeploymentCommand { get; }

    /// <summary>Deletes a deployment.</summary>
    public RelayCommand DeleteDeploymentCommand { get; }

    /// <summary>Runs a failed deployment's build again.</summary>
    public RelayCommand RetryDeploymentCommand { get; }

    /// <summary>Points production at an earlier deployment that already succeeded.</summary>
    public RelayCommand RollbackDeploymentCommand { get; }

    /// <summary>Reads a deployment's build log.</summary>
    public RelayCommand GetDeploymentLogsCommand { get; }

    /// <summary>Fetches the short-lived Direct Upload JWT.</summary>
    public RelayCommand GetUploadTokenCommand { get; }

    /// <summary>Throws away the project's cached build artefacts.</summary>
    public RelayCommand PurgeBuildCacheCommand { get; }

    /// <summary>Reads one custom domain.</summary>
    public RelayCommand GetDomainCommand { get; }

    /// <summary>Lists custom domains.</summary>
    public RelayCommand ListDomainsCommand { get; }

    /// <summary>Adds a custom domain.</summary>
    public RelayCommand AddDomainCommand { get; }

    /// <summary>Marks a custom domain verified.</summary>
    public RelayCommand VerifyDomainCommand { get; }

    /// <summary>Removes a custom domain.</summary>
    public RelayCommand DeleteDomainCommand { get; }

    private Task ListAsync() => RunAsync("List Pages projects", async () =>
        await Client.Pages.ListAsync(new PagesProjectListParameters
        {
            PerPage = OptionalInt(ListPerPage),
            Search = Blank(ListSearch),
        }));

    private Task ListAllAsync() => RunAsync("List every Pages project", async () =>
    {
        var projects = new List<PagesProject>();
        await foreach (var project in Client.Pages.ListAllAsync())
        {
            projects.Add(project);
        }

        return projects;
    });

    private Task GetAsync() => RunAsync("Get Pages project", async () =>
        await Client.Pages.GetAsync(Project()));

    private Task CreateAsync() => RunAsync("Create Pages project", async () =>
        await Client.Pages.CreateAsync(Project(), Blank(ProductionBranch) ?? "main"));

    private Task UpdateAsync() => RunAsync("Update Pages project", () =>
        Client.Pages.UpdateAsync(Project(), new UpdatePagesProjectRequest
        {
            ProductionBranch = Blank(ProductionBranch),
        }));

    private Task DeleteAsync() => RunVoidAsync("Delete Pages project", () =>
        Client.Pages.DeleteAsync(Project()));

    private Task ListDeploymentsAsync() => RunAsync("List Pages deployments", async () =>
        await Client.Pages.ListDeploymentsAsync(Project(), new PagesDeploymentListParameters
        {
            PerPage = OptionalInt(ListPerPage),
            // env is the filter the schema documents; branch is not, and is only sent when filled in.
            Env = Blank(DeploymentEnv),
            Branch = Blank(DeploymentBranch),
        }));

    private Task RetryDeploymentAsync() => RunAsync("Retry Pages deployment", async () =>
        await Client.Pages.RetryDeploymentAsync(Project(), Deployment()));

    private Task RollbackDeploymentAsync() => RunAsync("Roll back Pages production", async () =>
        await Client.Pages.RollbackDeploymentAsync(Project(), Deployment()));

    private Task GetDeploymentLogsAsync() => RunAsync("Read Pages deployment logs", async () =>
        await Client.Pages.GetDeploymentLogsAsync(Project(), Deployment()));

    private Task GetUploadTokenAsync() => RunAsync("Get Pages upload token", async () =>
        await Client.Pages.GetUploadTokenAsync(Project()));

    private Task PurgeBuildCacheAsync() => RunVoidAsync("Purge Pages build cache", () =>
        Client.Pages.PurgeBuildCacheAsync(Project()));

    private Task GetDomainAsync() => RunAsync("Get Pages domain", async () =>
        await Client.Pages.GetDomainAsync(Project(), Domain()));

    private Task GetDeploymentAsync() => RunAsync("Get Pages deployment", async () =>
        await Client.Pages.GetDeploymentAsync(Project(), Deployment()));

    private Task CreateDeploymentAsync() => RunAsync("Create Pages deployment", () =>
        Client.Pages.CreateDeploymentAsync(Project(), new CreatePagesDeploymentRequest
        {
            Branch = Blank(DeploymentBranch),
        }));

    private Task DeleteDeploymentAsync() => RunVoidAsync("Delete Pages deployment", () =>
        Client.Pages.DeleteDeploymentAsync(Project(), Deployment()));

    private Task ListDomainsAsync() => RunAsync("List Pages domains", async () =>
        await Client.Pages.ListDomainsAsync(Project(), new PagesDomainListParameters
        {
            PerPage = OptionalInt(ListPerPage),
        }));

    private Task AddDomainAsync() => RunAsync("Add Pages domain", () =>
        Client.Pages.AddDomainAsync(Project(), Domain()));

    private Task VerifyDomainAsync() => RunAsync("Verify Pages domain", () =>
        Client.Pages.UpdateDomainAsync(Project(), Domain(), new UpdatePagesDomainRequest
        {
            Verified = DomainVerified,
        }));

    private Task DeleteDomainAsync() => RunVoidAsync("Delete Pages domain", () =>
        Client.Pages.DeleteDomainAsync(Project(), Domain()));

    private string Project() =>
        Blank(ProjectName) ?? throw new ArgumentException("Enter a project name.");

    private string Deployment() =>
        Blank(DeploymentId) ?? throw new ArgumentException("Enter a deployment id - list the deployments first.");

    private string Domain() =>
        Blank(DomainName) ?? throw new ArgumentException("Enter a domain name.");
}
