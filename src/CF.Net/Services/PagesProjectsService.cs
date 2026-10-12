using System.Runtime.CompilerServices;
using CFNet.Http;
using CFNet.Models.Pages;

namespace CFNet.Services;

/// <summary>
/// Operations on Cloudflare Pages projects, their deployments and their custom domains.
/// </summary>
public interface IPagesProjectsService
{
    /// <summary>Lists Pages projects one page at a time.</summary>
    /// <param name="parameters">Optional filters and paging.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CloudflarePage<PagesProject>> ListAsync(
        PagesProjectListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Enumerates every Pages project, following page-number pagination automatically.</summary>
    /// <param name="parameters">Optional filters; <c>Page</c> is ignored.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<PagesProject> ListAllAsync(
        PagesProjectListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches one project by name.</summary>
    /// <param name="projectName">Project name.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The project, or <c>null</c> when it does not exist.</returns>
    Task<PagesProject?> GetAsync(string projectName, string? accountId = null, CancellationToken cancellationToken = default);

    /// <summary>Creates a project.</summary>
    /// <param name="request">Project name, production branch and optional configuration. Validated locally first.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PagesProject> CreateAsync(CreatePagesProjectRequest request, string? accountId = null, CancellationToken cancellationToken = default);

    /// <summary>Creates a directly uploaded project with sensible defaults.</summary>
    /// <param name="projectName">Project name.</param>
    /// <param name="productionBranch">Production branch; defaults to <c>main</c>.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PagesProject> CreateAsync(string projectName, string productionBranch = "main", string? accountId = null, CancellationToken cancellationToken = default);

    /// <summary>Partially updates a project.</summary>
    /// <param name="projectName">Project name.</param>
    /// <param name="request">The fields to change.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PagesProject> UpdateAsync(string projectName, UpdatePagesProjectRequest request, string? accountId = null, CancellationToken cancellationToken = default);

    /// <summary>Deletes a project together with all of its deployments.</summary>
    /// <param name="projectName">Project name.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(string projectName, string? accountId = null, CancellationToken cancellationToken = default);

    /// <summary>Lists deployments of a project.</summary>
    /// <param name="projectName">Project name.</param>
    /// <param name="parameters">Optional filters and paging.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CloudflarePage<PagesDeployment>> ListDeploymentsAsync(
        string projectName,
        PagesDeploymentListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches one deployment.</summary>
    /// <param name="projectName">Project name.</param>
    /// <param name="deploymentId">Deployment id.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PagesDeployment?> GetDeploymentAsync(
        string projectName,
        string deploymentId,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Starts a new deployment.</summary>
    /// <param name="projectName">Project name.</param>
    /// <param name="request">Deployment description; <c>null</c> starts an empty Direct Upload deployment.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PagesDeployment> CreateDeploymentAsync(
        string projectName,
        CreatePagesDeploymentRequest? request = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a deployment.</summary>
    /// <param name="projectName">Project name.</param>
    /// <param name="deploymentId">Deployment id.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteDeploymentAsync(string projectName, string deploymentId, string? accountId = null, CancellationToken cancellationToken = default);

    /// <summary>Lists the custom domains attached to a project.</summary>
    /// <param name="projectName">Project name.</param>
    /// <param name="parameters">Optional paging.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CloudflarePage<PagesDomain>> ListDomainsAsync(
        string projectName,
        PagesDomainListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Attaches a custom domain to a project.</summary>
    /// <param name="projectName">Project name.</param>
    /// <param name="domainName">Domain to attach.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PagesDomain> AddDomainAsync(string projectName, string domainName, string? accountId = null, CancellationToken cancellationToken = default);

    /// <summary>Updates a custom domain's verification flags.</summary>
    /// <param name="projectName">Project name.</param>
    /// <param name="domainName">Domain to update.</param>
    /// <param name="request">The flags to change.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PagesDomain> UpdateDomainAsync(
        string projectName,
        string domainName,
        UpdatePagesDomainRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Detaches a custom domain from a project.</summary>
    /// <param name="projectName">Project name.</param>
    /// <param name="domainName">Domain to detach.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteDomainAsync(string projectName, string domainName, string? accountId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls production back to an earlier deployment via
    /// <c>POST /accounts/{account_id}/pages/projects/{project_name}/deployments/{deployment_id}/rollback</c>.
    /// </summary>
    /// <param name="projectName">Project name.</param>
    /// <param name="deploymentId">Id of the deployment to roll production back to.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deployment that is now serving production.</returns>
    /// <remarks>
    /// Needs <c>Pages Write</c>. Only a deployment that previously succeeded can be rolled back to, and
    /// the call takes no body - the deployment in the path is the target, not the one being undone.
    /// </remarks>
    Task<PagesDeployment> RollbackDeploymentAsync(
        string projectName,
        string deploymentId,
        string? accountId = null,
        CancellationToken cancellationToken = default);
}


/// <inheritdoc />
public sealed class PagesProjectsService : CloudflareService, IPagesProjectsService
{
    /// <summary>Creates the service.</summary>
    /// <param name="transport">The transport to execute requests through.</param>
    public PagesProjectsService(ICloudflareTransport transport)
        : base(transport)
    {
    }

    /// <inheritdoc />
    public async Task<CloudflarePage<PagesProject>> ListAsync(
        PagesProjectListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var account = ResolveAccountId(accountId);
        var request = Apply(CloudflareRequest.Get($"{AccountPath(account)}/pages/projects"), parameters);
        request.OperationName = "List Pages Projects";

        var response = await Transport.SendAsync<List<PagesProject>>(request, cancellationToken).ConfigureAwait(false);
        return response.ToPage();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<PagesProject> ListAllAsync(
        PagesProjectListParameters? parameters = null,
        string? accountId = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var account = ResolveAccountId(accountId);
        var page = 1;

        while (true)
        {
            var effective = new PagesProjectListParameters
            {
                Search = parameters?.Search,
                PerPage = parameters?.PerPage,
                Page = page,
            };

            var result = await ListAsync(effective, account, cancellationToken).ConfigureAwait(false);
            foreach (var project in result.Items)
            {
                yield return project;
            }

            if (result.Items.Count == 0)
            {
                yield break;
            }

            // Some Pages list responses omit result_info, so fall back to "stop at a short page".
            if (!result.HasMore)
            {
                yield break;
            }

            page++;
        }
    }

    /// <inheritdoc />
    public async Task<PagesProject?> GetAsync(string projectName, string? accountId = null, CancellationToken cancellationToken = default)
    {
        var path = ProjectPath(ResolveAccountId(accountId), projectName);
        var request = CloudflareRequest.Get(path).Tolerate(System.Net.HttpStatusCode.NotFound);
        request.OperationName = "Get Pages Project";

        var response = await Transport.SendAsync<PagesProject>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<PagesProject> CreateAsync(
        CreatePagesProjectRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        var account = ResolveAccountId(accountId);
        var httpRequest = CloudflareRequest.Post($"{AccountPath(account)}/pages/projects", request);
        httpRequest.OperationName = "Create Pages Project";

        var response = await Transport.SendAsync<PagesProject>(httpRequest, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public Task<PagesProject> CreateAsync(
        string projectName,
        string productionBranch = "main",
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        Require(projectName, nameof(projectName));
        return CreateAsync(new CreatePagesProjectRequest(projectName.Trim(), productionBranch), accountId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PagesProject> UpdateAsync(
        string projectName,
        UpdatePagesProjectRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var path = ProjectPath(ResolveAccountId(accountId), projectName);
        var httpRequest = CloudflareRequest.Patch(path, request);
        httpRequest.OperationName = "Update Pages Project";

        var response = await Transport.SendAsync<PagesProject>(httpRequest, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string projectName, string? accountId = null, CancellationToken cancellationToken = default)
    {
        var path = ProjectPath(ResolveAccountId(accountId), projectName);
        var request = CloudflareRequest.Delete(path);
        request.OperationName = "Delete Pages Project";

        await SendVoidAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CloudflarePage<PagesDeployment>> ListDeploymentsAsync(
        string projectName,
        PagesDeploymentListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var path = ProjectPath(ResolveAccountId(accountId), projectName);
        var request = Apply(CloudflareRequest.Get($"{path}/deployments"), parameters);
        request.OperationName = "List Pages Deployments";

        var response = await Transport.SendAsync<List<PagesDeployment>>(request, cancellationToken).ConfigureAwait(false);
        return response.ToPage();
    }

    /// <inheritdoc />
    public async Task<PagesDeployment?> GetDeploymentAsync(
        string projectName,
        string deploymentId,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var path = ProjectPath(ResolveAccountId(accountId), projectName);
        var id = Require(deploymentId, nameof(deploymentId));

        var request = CloudflareRequest
            .Get($"{path}/deployments/{CloudflareUrl.EncodeSegment(id)}")
            .Tolerate(System.Net.HttpStatusCode.NotFound);
        request.OperationName = "Get Pages Deployment";

        var response = await Transport.SendAsync<PagesDeployment>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<PagesDeployment> CreateDeploymentAsync(
        string projectName,
        CreatePagesDeploymentRequest? request = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var path = ProjectPath(ResolveAccountId(accountId), projectName);
        var httpRequest = CloudflareRequest.Post($"{path}/deployments", request ?? new CreatePagesDeploymentRequest());
        httpRequest.OperationName = "Create Pages Deployment";

        var response = await Transport.SendAsync<PagesDeployment>(httpRequest, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task DeleteDeploymentAsync(
        string projectName,
        string deploymentId,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var path = ProjectPath(ResolveAccountId(accountId), projectName);
        var id = Require(deploymentId, nameof(deploymentId));

        var request = CloudflareRequest.Delete($"{path}/deployments/{CloudflareUrl.EncodeSegment(id)}");
        request.OperationName = "Delete Pages Deployment";

        await SendVoidAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CloudflarePage<PagesDomain>> ListDomainsAsync(
        string projectName,
        PagesDomainListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var path = ProjectPath(ResolveAccountId(accountId), projectName);
        var request = Apply(CloudflareRequest.Get($"{path}/domains"), parameters);
        request.OperationName = "List Pages Domains";

        var response = await Transport.SendAsync<List<PagesDomain>>(request, cancellationToken).ConfigureAwait(false);
        return response.ToPage();
    }

    /// <inheritdoc />
    public async Task<PagesDomain> AddDomainAsync(
        string projectName,
        string domainName,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        Require(domainName, nameof(domainName));

        var path = ProjectPath(ResolveAccountId(accountId), projectName);
        var httpRequest = CloudflareRequest.Post($"{path}/domains", new AddPagesDomainRequest(domainName.Trim()));
        httpRequest.OperationName = "Add Pages Domain";

        var response = await Transport.SendAsync<PagesDomain>(httpRequest, cancellationToken).ConfigureAwait(false);
        return response.Result ?? new PagesDomain { Name = domainName.Trim() };
    }

    /// <inheritdoc />
    public async Task<PagesDomain> UpdateDomainAsync(
        string projectName,
        string domainName,
        UpdatePagesDomainRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var path = ProjectPath(ResolveAccountId(accountId), projectName);
        var domain = Require(domainName, nameof(domainName));

        var httpRequest = CloudflareRequest.Patch($"{path}/domains/{CloudflareUrl.EncodeSegment(domain)}", request);
        httpRequest.OperationName = "Update Pages Domain";

        var response = await Transport.SendAsync<PagesDomain>(httpRequest, cancellationToken).ConfigureAwait(false);
        return response.Result ?? new PagesDomain { Name = domain };
    }

    /// <inheritdoc />
    public async Task DeleteDomainAsync(
        string projectName,
        string domainName,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var path = ProjectPath(ResolveAccountId(accountId), projectName);
        var domain = Require(domainName, nameof(domainName));

        var request = CloudflareRequest.Delete($"{path}/domains/{CloudflareUrl.EncodeSegment(domain)}");
        request.OperationName = "Delete Pages Domain";

        await SendVoidAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds <c>accounts/{account_id}/pages/projects/{project_name}</c>. Pages addresses projects by
    /// name, and the name travels as a path segment, so it is escaped.
    /// </summary>
    private static string ProjectPath(string accountId, string projectName) =>
        $"{AccountPath(accountId)}/pages/projects/{CloudflareUrl.EncodeSegment(Require(projectName, nameof(projectName)))}";

    /// <inheritdoc />
    public async Task<PagesDeployment> RollbackDeploymentAsync(
        string projectName,
        string deploymentId,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var account = ResolveAccountId(accountId);
        var deployment = Require(deploymentId, nameof(deploymentId));

        var request = CloudflareRequest.Post(
            $"{ProjectPath(account, projectName)}/deployments/{CloudflareUrl.EncodeSegment(deployment)}/rollback");
        request.OperationName = "Roll Back Pages Deployment";

        var response = await Transport.SendAsync<PagesDeployment>(request, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }
}
