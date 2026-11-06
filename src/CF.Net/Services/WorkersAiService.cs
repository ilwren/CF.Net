using System.Runtime.CompilerServices;
using System.Text.Json;
using CFNet.Http;
using CFNet.Models.WorkersAi;

namespace CFNet.Services;

/// <summary>
/// Workers AI: running a model, and the catalogue behind it - the models an account can run, the tasks they
/// perform, the authors behind them, and each model's own input and output schema.
/// </summary>
/// <remarks>
/// <para>
/// These are account-level resources under <c>/accounts/{account_id}/ai/</c>, which is a sibling of the
/// Workers paths rather than one of them: the product is Workers AI but the API does not put it under
/// <c>workers/</c>.
/// </para>
/// <para>
/// All three searches here are catalogues of what Cloudflare offers, not of what the account owns, so none of
/// them needs anything but read access - and the published schema types the items of all three as
/// <c>unknown</c>, which is why the models behind them are deliberately narrow and preserve whatever else
/// arrives. <see cref="GetModelSchemaAsync"/> is the call that says what a run takes, and
/// <see cref="RunAsync(string, WorkersAiRunRequest, string?, CancellationToken)"/> is the run itself. The
/// fine-tunes, the to-markdown converter and the AI Gateway are not wrapped yet.
/// </para>
/// </remarks>
public interface IWorkersAiService
{
    /// <summary>Searches the Workers AI model catalogue, one page at a time.</summary>
    /// <param name="parameters">Search term and filters by author, task or source, or null for every model.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One page of models, with the paging counts Cloudflare reports.</returns>
    /// <remarks>
    /// <c>GET /accounts/{account_id}/ai/models/search</c>. With no filters this is the whole catalogue, which
    /// is a few dozen models; <see cref="ListAllModelsAsync"/> walks it.
    /// </remarks>
    Task<CloudflarePage<WorkersAiModel>> ListModelsAsync(
        WorkersAiModelListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Searches the model catalogue and walks every page.</summary>
    /// <param name="parameters">Search term and filters, or null for every model.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Every matching model, page by page.</returns>
    IAsyncEnumerable<WorkersAiModel> ListAllModelsAsync(
        WorkersAiModelListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one model's input and output JSON Schema.</summary>
    /// <param name="model">The model's full name, namespace included, such as <c>@cf/meta/llama-2-7b-chat-int8</c>.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The two schemas, as Cloudflare publishes them.</returns>
    /// <remarks>
    /// <c>GET /accounts/{account_id}/ai/models/schema?model={model}</c>. This is the only published source for
    /// what an inference call to this model takes and answers, and the shapes differ per task - so it is the
    /// call to make before building one.
    /// </remarks>
    Task<WorkersAiModelSchema> GetModelSchemaAsync(
        string model,
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the task types the catalogue's models perform.</summary>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Every task type, empty when the account sees none.</returns>
    /// <remarks>
    /// <c>GET /accounts/{account_id}/ai/tasks/search</c>. One page with no paging parameters and no filters -
    /// the published schema takes only the account id - so this is the whole list, and a task's name is what
    /// the model search's <c>task</c> filter takes.
    /// </remarks>
    Task<IReadOnlyList<WorkersAiTask>> ListTasksAsync(
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the authors and organisations behind the catalogue's models.</summary>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Every author, empty when the account sees none.</returns>
    /// <remarks>
    /// <c>GET /accounts/{account_id}/ai/authors/search</c>. Like the task list this takes only the account id,
    /// and an author's name is what the model search's <c>author</c> filter takes.
    /// </remarks>
    Task<IReadOnlyList<WorkersAiAuthor>> ListAuthorsAsync(
        string? accountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Runs a model: one inference call against the model named in the path.</summary>
    /// <param name="model">
    /// The model's full name, namespace included, such as <c>@cf/meta/llama-3.2-1b-instruct</c>.
    /// </param>
    /// <param name="request">The model's inputs, which go in the body directly with no wrapper.</param>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The answer as the JSON that arrived, plus a projection onto whichever arm the caller asks for.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <c>POST /accounts/{account_id}/ai/run/{model_name}</c>. The permission the catalogue needs is the one
    /// this needs too: Cloudflare's own words for <b>Workers AI Read</b> are "grants access to invoke Workers
    /// AI models", so running a model asks for nothing a token that can list them lacks.
    /// </para>
    /// <para>
    /// What comes back depends on the model, and Cloudflare publishes thirteen shapes for it - an object for a
    /// chat or embedding model, an array of objects for a classification or detection model, a bare string for
    /// a few others. That is why the answer is <see cref="WorkersAiRunResult"/> rather than one typed model:
    /// it holds the JSON and reads the arm a caller asks for, and <see cref="WorkersAiRunResult.Text"/> is the
    /// words whichever arm carries them.
    /// </para>
    /// <para>
    /// A streamed answer is refused locally rather than sent: <c>stream: true</c> makes Cloudflare answer
    /// server-sent events, which is not the one JSON envelope this library's transport reads.
    /// </para>
    /// </remarks>
    Task<WorkersAiRunResult> RunAsync(
        string model,
        WorkersAiRunRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class WorkersAiService : CloudflareService, IWorkersAiService
{
    /// <summary>Creates the service.</summary>
    /// <param name="transport">The transport that executes requests.</param>
    public WorkersAiService(ICloudflareTransport transport)
        : base(transport)
    {
    }

    /// <inheritdoc />
    public async Task<CloudflarePage<WorkersAiModel>> ListModelsAsync(
        WorkersAiModelListParameters? parameters = null,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var account = ResolveAccountId(accountId);
        var request = Apply(CloudflareRequest.Get($"{AiPath(account)}/models/search"), parameters);
        request.OperationName = "Search Workers AI Models";

        var response = await Transport
            .SendAsync<List<WorkersAiModel>>(request, cancellationToken)
            .ConfigureAwait(false);
        return response.ToPage();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<WorkersAiModel> ListAllModelsAsync(
        WorkersAiModelListParameters? parameters = null,
        string? accountId = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var account = ResolveAccountId(accountId);
        var page = 1;

        while (true)
        {
            var effective = new WorkersAiModelListParameters
            {
                Author = parameters?.Author,
                Format = parameters?.Format,
                HideExperimental = parameters?.HideExperimental,
                IncludeDeprecated = parameters?.IncludeDeprecated,
                Search = parameters?.Search,
                Source = parameters?.Source,
                Task = parameters?.Task,
                PerPage = parameters?.PerPage,
                Page = page,
            };

            var result = await ListModelsAsync(effective, account, cancellationToken).ConfigureAwait(false);
            foreach (var model in result.Items)
            {
                yield return model;
            }

            // Stop on a short page as well as on result_info saying so: a catalogue that answers everything
            // in one page is the common case, and not every list reports its paging.
            if (result.Items.Count == 0 || !result.HasMore)
            {
                yield break;
            }

            page++;
        }
    }

    /// <inheritdoc />
    public async Task<WorkersAiModelSchema> GetModelSchemaAsync(
        string model,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var account = ResolveAccountId(accountId);
        var request = CloudflareRequest.Get($"{AiPath(account)}/models/schema");
        request.Query.Add("model", Require(model, nameof(model)));
        request.OperationName = "Get Workers AI Model Schema";

        var response = await Transport
            .SendAsync<WorkersAiModelSchema>(request, cancellationToken)
            .ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkersAiTask>> ListTasksAsync(
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get($"{AiPath(ResolveAccountId(accountId))}/tasks/search");
        request.OperationName = "Search Workers AI Tasks";

        var response = await Transport
            .SendAsync<List<WorkersAiTask>>(request, cancellationToken)
            .ConfigureAwait(false);
        return response.Result ?? new List<WorkersAiTask>();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkersAiAuthor>> ListAuthorsAsync(
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get($"{AiPath(ResolveAccountId(accountId))}/authors/search");
        request.OperationName = "Search Workers AI Authors";

        var response = await Transport
            .SendAsync<List<WorkersAiAuthor>>(request, cancellationToken)
            .ConfigureAwait(false);
        return response.Result ?? new List<WorkersAiAuthor>();
    }

    /// <inheritdoc />
    public async Task<WorkersAiRunResult> RunAsync(
        string model,
        WorkersAiRunRequest request,
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();

        var cloudflareRequest = CloudflareRequest.Post(
            $"{AiPath(ResolveAccountId(accountId))}/run/{ModelInPath(model)}",
            request);
        cloudflareRequest.OperationName = "Run Workers AI Model";

        // The answer is read as the JSON it arrived in. Cloudflare publishes thirteen shapes for it - an
        // object, an array of objects, a bare string - and which one comes back is a property of the model in
        // the path rather than of the call, so typing it here would be a guess about a model this library has
        // never seen. WorkersAiRunResult holds the JSON and reads whichever arm the caller asks for.
        var response = await Transport
            .SendAsync<JsonElement>(cloudflareRequest, cancellationToken)
            .ConfigureAwait(false);
        return new WorkersAiRunResult(response.EnsureResult());
    }

    /// <summary>
    /// Puts a model's name into a path exactly as it is written, which is the one path segment in this library
    /// that is not escaped.
    /// </summary>
    /// <param name="model">The model's full name, namespace included.</param>
    /// <returns>The name, checked rather than escaped.</returns>
    /// <exception cref="CFNet.Exceptions.CloudflareConfigurationException">
    /// Thrown when the name is blank, or carries a character that would change what the request means.
    /// </exception>
    /// <remarks>
    /// A live run of <c>POST …/ai/run/%40cf%2Fmeta%2Fllama-3.2-1b-instruct</c> - the escaped form this library
    /// uses for every other path segment - answered <b>400</b> with error <b>7000</b>, "No route for that URI".
    /// Cloudflare routes inference on the model's name as it is spelled, <c>@cf/meta/llama-3.2-1b-instruct</c>
    /// with its slashes and its <c>@</c>, so the name goes into the path verbatim. It is checked instead of
    /// escaped: whitespace, <c>?</c>, <c>#</c> and <c>%</c> are refused locally, because the first two would
    /// end the path and the third would start an escape this method is deliberately not making. The same name
    /// is escaped when it is a query parameter, as it is for <see cref="GetModelSchemaAsync"/>, and that call
    /// answers 200 either way.
    /// </remarks>
    private static string ModelInPath(string model)
    {
        var name = Require(model, nameof(model));
        var problems = new List<string>();

        if (name.Any(char.IsWhiteSpace))
        {
            problems.Add("a model name cannot contain whitespace.");
        }

        if (name.IndexOfAny(new[] { '?', '#', '%' }) >= 0)
        {
            problems.Add(
                "a model name cannot contain '?', '#' or '%': the first two would end the path, and the third " +
                "would start an escape that Cloudflare's own router answers with 400 and error 7000.");
        }

        if (name.StartsWith('/') || name.EndsWith('/'))
        {
            problems.Add("a model name cannot start or end with a slash.");
        }

        if (problems.Count > 0)
        {
            throw new CFNet.Exceptions.CloudflareConfigurationException(problems);
        }

        return name;
    }

    private static string AiPath(string accountId) => $"{AccountPath(accountId)}/ai";
}
