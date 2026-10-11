using System.Runtime.CompilerServices;
using CFNet.Http;
using CFNet.Models.Workers;

namespace CFNet.Services;

/// <summary>
/// Operations on Workers scripts: listing, uploading, deleting, deployments and the
/// <c>*.workers.dev</c> subdomain.
/// </summary>
public interface IWorkersScriptsService
{
    /// <summary>Lists the Worker scripts in an account.</summary>
    /// <param name="parameters">Optional filters and cursor.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CloudflarePage<WorkerScript>> ListAsync(
        WorkerScriptListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Enumerates every Worker script, following cursor pagination automatically.</summary>
    /// <param name="parameters">Optional filters; <c>Cursor</c> is ignored.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<WorkerScript> ListAllAsync(
        WorkerScriptListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches one script's metadata.</summary>
    /// <param name="scriptName">The Worker name.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The script, or <c>null</c> when it does not exist.</returns>
    Task<WorkerScript?> GetAsync(
        string scriptName,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Downloads the deployed module list of a script.</summary>
    /// <param name="scriptName">The Worker name.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<WorkerScriptContent?> GetContentAsync(
        string scriptName,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches the script-level settings (Logpush, tail consumers, observability, tags).</summary>
    /// <param name="scriptName">The Worker name.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<WorkerScriptSettings?> GetSettingsAsync(
        string scriptName,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads (creates or replaces) a Worker. Uses the <c>multipart/form-data</c> shape Cloudflare
    /// requires: a <c>metadata</c> part plus one part per module.
    /// </summary>
    /// <param name="request">The bundle and its configuration. Validated locally first.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<WorkerScript> UploadAsync(
        UploadWorkerScriptRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Uploads a single-module Worker from source text.</summary>
    /// <param name="scriptName">The Worker name.</param>
    /// <param name="source">JavaScript module source.</param>
    /// <param name="compatibilityDate">Runtime compatibility date; defaults to today's date.</param>
    /// <param name="bindings">Bindings to attach.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<WorkerScript> UploadAsync(
        string scriptName,
        string source,
        string? compatibilityDate = null,
        IEnumerable<WorkerBinding>? bindings = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a Worker. This removes all of its versions and deployments.</summary>
    /// <param name="scriptName">The Worker name.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(string scriptName, string? accountId = null, CancellationToken cancellationToken = default);

    /// <summary>Lists deployments of a script.</summary>
    /// <param name="scriptName">The Worker name.</param>
    /// <param name="parameters">Optional filters and cursor.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CloudflarePage<WorkerDeployment>> ListDeploymentsAsync(
        string scriptName,
        WorkerDeploymentListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a deployment, routing traffic to the given versions.</summary>
    /// <param name="scriptName">The Worker name.</param>
    /// <param name="request">The rollout description.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<WorkerDeployment> CreateDeploymentAsync(
        string scriptName,
        CreateWorkerDeploymentRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches the account's <c>*.workers.dev</c> subdomain.</summary>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<WorkerSubdomain?> GetSubdomainAsync(string? accountId = null, CancellationToken cancellationToken = default);

    /// <summary>Changes the account's <c>*.workers.dev</c> subdomain or enables it.</summary>
    /// <param name="request">The fields to change.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<WorkerSubdomain> UpdateSubdomainAsync(
        UpdateWorkerSubdomainRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches a script's <c>workers.dev</c> publication state.</summary>
    /// <param name="scriptName">The Worker name.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<WorkerScriptSubdomain?> GetScriptSubdomainAsync(
        string scriptName,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Publishes or unpublishes a script on <c>workers.dev</c>.</summary>
    /// <param name="scriptName">The Worker name.</param>
    /// <param name="request">The desired state.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<WorkerScriptSubdomain?> UpdateScriptSubdomainAsync(
        string scriptName,
        UpdateWorkerScriptSubdomainRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class WorkersScriptsService : CloudflareService, IWorkersScriptsService
{
    /// <summary>Creates the service.</summary>
    /// <param name="transport">The transport to execute requests through.</param>
    public WorkersScriptsService(ICloudflareTransport transport)
        : base(transport)
    {
    }

    /// <inheritdoc />
    public async Task<CloudflarePage<WorkerScript>> ListAsync(
        WorkerScriptListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var account = ResolveAccountId(accountId);
        var request = Apply(CloudflareRequest.Get($"{AccountPath(account)}/workers/scripts"), parameters);
        request.OperationName = "List Worker Scripts";

        var response = await Transport.SendAsync<List<WorkerScript>>(request, cancellationToken).ConfigureAwait(false);
        return response.ToPage();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<WorkerScript> ListAllAsync(
        WorkerScriptListParameters? parameters = null,
        string? accountId = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var account = ResolveAccountId(accountId);
        string? cursor = null;
        var guard = 0;

        while (true)
        {
            var effective = new WorkerScriptListParameters
            {
                PerPage = parameters?.PerPage,
                Name = parameters?.Name,
                Order = parameters?.Order,
                Direction = parameters?.Direction,
                Cursor = cursor,
            };

            var page = await ListAsync(effective, account, cancellationToken).ConfigureAwait(false);
            foreach (var script in page.Items)
            {
                yield return script;
            }

            var next = page.NextCursor;
            if (string.IsNullOrEmpty(next) || string.Equals(next, cursor, StringComparison.Ordinal) || page.Items.Count == 0)
            {
                yield break;
            }

            cursor = next;

            // A cursor that never terminates would spin forever against a buggy endpoint.
            if (++guard > 10_000)
            {
                yield break;
            }
        }
    }

    /// <inheritdoc />
    public async Task<WorkerScript?> GetAsync(
        string scriptName,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var account = ResolveAccountId(accountId);
        var name = Require(scriptName, nameof(scriptName));

        var request = CloudflareRequest.Get($"{AccountPath(account)}/workers/scripts/{CloudflareUrl.EncodeSegment(name)}")
            .Tolerate(System.Net.HttpStatusCode.NotFound);
        request.OperationName = "Get Worker Script";

        var response = await Transport.SendAsync<WorkerScript>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<WorkerScriptContent?> GetContentAsync(
        string scriptName,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var account = ResolveAccountId(accountId);
        var name = Require(scriptName, nameof(scriptName));

        var request = CloudflareRequest
            .Get($"{AccountPath(account)}/workers/scripts/{CloudflareUrl.EncodeSegment(name)}/content")
            .Tolerate(System.Net.HttpStatusCode.NotFound);
        request.OperationName = "Get Worker Script Content";

        var response = await Transport.SendAsync<WorkerScriptContent>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<WorkerScriptSettings?> GetSettingsAsync(
        string scriptName,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var account = ResolveAccountId(accountId);
        var name = Require(scriptName, nameof(scriptName));

        var request = CloudflareRequest
            .Get($"{AccountPath(account)}/workers/scripts/{CloudflareUrl.EncodeSegment(name)}/script-settings")
            .Tolerate(System.Net.HttpStatusCode.NotFound);
        request.OperationName = "Get Worker Script Settings";

        var response = await Transport.SendAsync<WorkerScriptSettings>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<WorkerScript> UploadAsync(
        UploadWorkerScriptRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        var account = ResolveAccountId(accountId);
        var name = Require(request.ScriptName, nameof(request.ScriptName));
        var path = $"{AccountPath(account)}/workers/scripts/{CloudflareUrl.EncodeSegment(name)}";

        // The request object itself is the metadata part: ScriptName and Modules are [JsonIgnore]d,
        // so only the fields Cloudflare expects inside "metadata" are serialised.
        var multipart = new MultipartRequestBody().AddJson("metadata", request);

        foreach (var module in request.Modules!)
        {
            var bytes = module.Bytes ?? DecodeBase64(module);
            multipart.AddFile(module.Name!, bytes, module.ContentType ?? WorkerModuleContentTypes.JavaScriptModule, module.Name);
        }

        var httpRequest = new CloudflareRequest(HttpMethod.Put, path)
        {
            Body = multipart,
            OperationName = "Upload Worker Script",
        };

        var response = await Transport.SendAsync<WorkerScript>(httpRequest, cancellationToken).ConfigureAwait(false);

        // Some accounts get a bare success response for an upload; fall back to the known name so the
        // caller always receives something usable.
        return response.Result ?? new WorkerScript { Id = name, ScriptName = name };
    }

    /// <inheritdoc />
    public Task<WorkerScript> UploadAsync(
        string scriptName,
        string source,
        string? compatibilityDate = null,
        IEnumerable<WorkerBinding>? bindings = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        Require(scriptName, nameof(scriptName));
        if (source is null)
        {
            throw new Exceptions.CloudflareConfigurationException("Worker source must not be null.");
        }

        var request = new UploadWorkerScriptRequest(
            scriptName.Trim(),
            source,
            compatibilityDate ?? DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))
        {
            Bindings = bindings?.ToList(),
        };

        return UploadAsync(request, accountId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string scriptName, string? accountId = null, CancellationToken cancellationToken = default)
    {
        var account = ResolveAccountId(accountId);
        var name = Require(scriptName, nameof(scriptName));

        var request = CloudflareRequest.Delete($"{AccountPath(account)}/workers/scripts/{CloudflareUrl.EncodeSegment(name)}");
        request.OperationName = "Delete Worker Script";

        await SendVoidAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CloudflarePage<WorkerDeployment>> ListDeploymentsAsync(
        string scriptName,
        WorkerDeploymentListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var account = ResolveAccountId(accountId);
        var name = Require(scriptName, nameof(scriptName));

        var request = Apply(
            CloudflareRequest.Get($"{AccountPath(account)}/workers/scripts/{CloudflareUrl.EncodeSegment(name)}/deployments"),
            parameters);
        request.OperationName = "List Worker Deployments";

        var response = await Transport.SendAsync<List<WorkerDeployment>>(request, cancellationToken).ConfigureAwait(false);
        return response.ToPage();
    }

    /// <inheritdoc />
    public async Task<WorkerDeployment> CreateDeploymentAsync(
        string scriptName,
        CreateWorkerDeploymentRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var account = ResolveAccountId(accountId);
        var name = Require(scriptName, nameof(scriptName));

        var total = request.Versions?.Sum(v => v.Percentage ?? 0) ?? 0;
        if (request.Versions is null || request.Versions.Count == 0)
        {
            throw new Exceptions.CloudflareConfigurationException("A deployment needs at least one version.");
        }

        if (Math.Abs(total - 100d) > 0.0001d)
        {
            throw new Exceptions.CloudflareConfigurationException(
                $"Deployment version percentages must add up to 100 (got {total}).");
        }

        request.Strategy ??= "percentage";

        var httpRequest = CloudflareRequest.Post(
            $"{AccountPath(account)}/workers/scripts/{CloudflareUrl.EncodeSegment(name)}/deployments",
            request);
        httpRequest.OperationName = "Create Worker Deployment";

        var response = await Transport.SendAsync<WorkerDeployment>(httpRequest, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<WorkerSubdomain?> GetSubdomainAsync(string? accountId = null, CancellationToken cancellationToken = default)
    {
        var account = ResolveAccountId(accountId);
        var request = CloudflareRequest.Get($"{AccountPath(account)}/workers/subdomain");
        request.OperationName = "Get Workers Subdomain";

        var response = await Transport.SendAsync<WorkerSubdomain>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<WorkerSubdomain> UpdateSubdomainAsync(
        UpdateWorkerSubdomainRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var account = ResolveAccountId(accountId);
        var httpRequest = CloudflareRequest.Patch($"{AccountPath(account)}/workers/subdomain", request);
        httpRequest.OperationName = "Update Workers Subdomain";

        var response = await Transport.SendAsync<WorkerSubdomain>(httpRequest, cancellationToken).ConfigureAwait(false);
        return response.Result ?? new WorkerSubdomain
        {
            Subdomain = request.Subdomain,
            Enabled = request.Enabled,
            PreviewsEnabled = request.PreviewsEnabled,
        };
    }

    /// <inheritdoc />
    public async Task<WorkerScriptSubdomain?> GetScriptSubdomainAsync(
        string scriptName,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var account = ResolveAccountId(accountId);
        var name = Require(scriptName, nameof(scriptName));

        var request = CloudflareRequest
            .Get($"{AccountPath(account)}/workers/scripts/{CloudflareUrl.EncodeSegment(name)}/subdomain")
            .Tolerate(System.Net.HttpStatusCode.NotFound);
        request.OperationName = "Get Worker Script Subdomain";

        var response = await Transport.SendAsync<WorkerScriptSubdomain>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <inheritdoc />
    public async Task<WorkerScriptSubdomain?> UpdateScriptSubdomainAsync(
        string scriptName,
        UpdateWorkerScriptSubdomainRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var account = ResolveAccountId(accountId);
        var name = Require(scriptName, nameof(scriptName));

        var httpRequest = CloudflareRequest.Post(
            $"{AccountPath(account)}/workers/scripts/{CloudflareUrl.EncodeSegment(name)}/subdomain",
            request);
        httpRequest.OperationName = "Update Worker Script Subdomain";

        var response = await Transport.SendAsync<WorkerScriptSubdomain>(httpRequest, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    private static byte[] DecodeBase64(WorkerModule module)
    {
        if (string.IsNullOrEmpty(module.ContentBase64))
        {
            throw new Exceptions.CloudflareConfigurationException(
                $"Module '{module.Name}' has neither Bytes nor ContentBase64 set.");
        }

        try
        {
            return Convert.FromBase64String(module.ContentBase64!);
        }
        catch (FormatException exception)
        {
            throw new Exceptions.CloudflareConfigurationException(
                $"Module '{module.Name}' has a ContentBase64 value that is not valid base64.", exception);
        }
    }
}
