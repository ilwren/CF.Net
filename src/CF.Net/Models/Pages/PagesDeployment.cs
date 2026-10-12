using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Http;
using CFNet.Models.Common;

namespace CFNet.Models.Pages;

/// <summary>
/// A Pages deployment, from <c>GET /accounts/{account_id}/pages/projects/{project_name}/deployments</c>.
/// </summary>
public sealed class PagesDeployment
{
    /// <summary>Deployment identifier.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Project the deployment belongs to.</summary>
    [JsonPropertyName("project_id")]
    public string? ProjectId { get; set; }

    /// <summary>Name of the project.</summary>
    [JsonPropertyName("project_name")]
    public string? ProjectName { get; set; }

    /// <summary>The <c>*.pages.dev</c> or custom URL this deployment serves.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>Additional URLs (aliases) pointing at this deployment.</summary>
    [JsonPropertyName("aliases")]
    public List<string>? Aliases { get; set; }

    /// <summary>Environment: <c>production</c> or <c>preview</c>.</summary>
    [JsonPropertyName("environment")]
    public string? Environment { get; set; }

    /// <summary>What triggered the deployment, e.g. <c>api</c>, <c>git</c>.</summary>
    [JsonPropertyName("trigger_source")]
    public string? TriggerSource { get; set; }

    /// <summary>Legacy spelling of <see cref="TriggerSource"/>.</summary>
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    /// <summary>The branch this deployment was built from.</summary>
    [JsonPropertyName("branch")]
    public string? Branch { get; set; }

    /// <summary>Overall deployment status. See <see cref="PagesDeploymentStatusValues"/>.</summary>
    [JsonPropertyName("latest_stage")]
    public PagesDeploymentStage? LatestStage { get; set; }

    /// <summary>Every stage the deployment went through.</summary>
    [JsonPropertyName("stages")]
    public List<PagesDeploymentStage>? Stages { get; set; }

    /// <summary>Environment variables captured for this deployment. Secret values are redacted.</summary>
    [JsonPropertyName("env_vars")]
    public Dictionary<string, PagesEnvVar>? EnvVars { get; set; }

    /// <summary>Build configuration used for this deployment.</summary>
    [JsonPropertyName("build_config")]
    public PagesBuildConfig? BuildConfig { get; set; }

    /// <summary>Deployment configuration used for this deployment.</summary>
    [JsonPropertyName("deployment_config")]
    public PagesDeploymentConfig? DeploymentConfig { get; set; }

    /// <summary>Manifest of the uploaded assets.</summary>
    [JsonPropertyName("manifest")]
    public JsonElement? Manifest { get; set; }

    /// <summary>Whether the deployment was skipped.</summary>
    [JsonPropertyName("is_skipped")]
    public bool? IsSkipped { get; set; }

    /// <summary>Creation timestamp.</summary>
    [JsonPropertyName("created_on")]
    public DateTimeOffset? CreatedOn { get; set; }

    /// <summary>Last modification timestamp.</summary>
    [JsonPropertyName("modified_on")]
    public DateTimeOffset? ModifiedOn { get; set; }

    /// <summary>
    /// Overall status, derived from <see cref="LatestStage"/>. Falls back to <c>unknown</c>.
    /// </summary>
    [JsonIgnore]
    public string Status => LatestStage?.Status ?? PagesDeploymentStatusValues.Unknown;

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"{Id} [{Environment}] {Status}";
}

/// <summary>One build/deploy stage of a Pages deployment.</summary>
public sealed class PagesDeploymentStage
{
    /// <summary>Stage name, e.g. <c>initialize</c>, <c>build</c>, <c>deploy</c>, <c>deployment_started</c>, <c>deployment_succeeded</c>.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Stage status. See <see cref="PagesDeploymentStatusValues"/>.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>When the stage started.</summary>
    [JsonPropertyName("started_on")]
    public DateTimeOffset? StartedOn { get; set; }

    /// <summary>When the stage ended.</summary>
    [JsonPropertyName("ended_on")]
    public DateTimeOffset? EndedOn { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>Literal values Cloudflare uses for a deployment stage status.</summary>
/// <remarks>
/// The current schema lists <c>success</c>, <c>idle</c>, <c>active</c>, <c>failure</c>, <c>canceled</c> and
/// <c>skipped</c>. <c>in_progress</c>, <c>pending</c>, <c>none</c> and <c>unknown</c> are kept as well:
/// older responses and the dashboard still use them, and a status this library has never heard of is not a
/// deserialisation failure - the field is a string precisely so that a new value cannot break a caller.
/// </remarks>
public static class PagesDeploymentStatusValues
{
    /// <summary>Status was not reported.</summary>
    public const string Unknown = "unknown";

    /// <summary>The stage is waiting to run.</summary>
    public const string Queued = "queued";

    /// <summary>The stage has not started and nothing is waiting on it.</summary>
    public const string Idle = "idle";

    /// <summary>The stage is running - the schema's current spelling of "in flight".</summary>
    public const string Active = "active";

    /// <summary>The stage is running. Older spelling, still seen in responses.</summary>
    public const string InProgress = "in_progress";

    /// <summary>The stage finished successfully.</summary>
    public const string Success = "success";

    /// <summary>The stage failed.</summary>
    public const string Failure = "failure";

    /// <summary>The stage was cancelled.</summary>
    public const string Canceled = "canceled";

    /// <summary>The stage did not need to run.</summary>
    public const string Skipped = "skipped";

    /// <summary>The stage is waiting for an approval or a pending action.</summary>
    public const string Pending = "pending";

    /// <summary>The stage did not start.</summary>
    public const string None = "none";
}

/// <summary>Query filters for the Pages deployments list endpoint.</summary>
public sealed class PagesDeploymentListParameters : CloudflarePagedParameters
{
    /// <summary>
    /// Which environment to return: <see cref="PagesDeploymentEnvironments.Production"/> or
    /// <see cref="PagesDeploymentEnvironments.Preview"/>. Omit it for both.
    /// </summary>
    public string? Env { get; set; }

    /// <summary>Only deployments for this branch.</summary>
    public string? Branch { get; set; }

    /// <summary>Field to sort by.</summary>
    public string? Order { get; set; }

    /// <summary>Sort direction.</summary>
    public SortDirection? Direction { get; set; }

    /// <inheritdoc />
    /// <remarks>
    /// The published schema for this endpoint lists <c>env</c>, <c>page</c> and <c>per_page</c>.
    /// <see cref="Branch"/>, <see cref="Order"/> and <see cref="Direction"/> are accepted by the service but
    /// are not part of that schema, and are only sent when set - the same caveat the project list carries
    /// for its <c>search</c> parameter.
    /// </remarks>
    public override void Apply(QueryStringBuilder query)
    {
        base.Apply(query);
        query.AddIfNotEmpty("env", Env);
        query.AddIfNotEmpty("branch", Branch);
        query.AddIfNotEmpty("order", Order);
        if (Direction.HasValue)
        {
            query.Add("direction", QueryEnums.ToWire(Direction.Value));
        }
    }
}

/// <summary>The <c>env</c> values the deployment list accepts.</summary>
public static class PagesDeploymentEnvironments
{
    /// <summary>Deployments serving the production branch and its aliases.</summary>
    public const string Production = "production";

    /// <summary>Preview deployments, each on its own <c>&lt;id&gt;.&lt;project&gt;.pages.dev</c> URL.</summary>
    public const string Preview = "preview";

    /// <summary>Every value the schema lists.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Production, Preview };
}

/// <summary>
/// Request body of <c>POST /accounts/{account_id}/pages/projects</c>.
/// </summary>
public sealed class CreatePagesProjectRequest
{
    /// <summary>Creates a request for a directly uploaded (non-Git) project.</summary>
    /// <param name="name">
    /// Project name. Must begin with a lowercase letter or digit and contain only lowercase letters,
    /// digits and hyphens.
    /// </param>
    /// <param name="productionBranch">Branch treated as production; commonly <c>main</c>.</param>
    public CreatePagesProjectRequest(string name, string productionBranch = "main")
    {
        Name = name;
        ProductionBranch = productionBranch;
    }

    /// <summary>Parameterless constructor for object initialisers and deserialisation.</summary>
    [System.Text.Json.Serialization.JsonConstructor]
    public CreatePagesProjectRequest()
    {
    }

    /// <summary>Project name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Branch treated as production.</summary>
    [JsonPropertyName("production_branch")]
    public string? ProductionBranch { get; set; }

    /// <summary>Git source configuration.</summary>
    [JsonPropertyName("source")]
    public PagesProjectSource? Source { get; set; }

    /// <summary>Build configuration.</summary>
    [JsonPropertyName("build_config")]
    public PagesBuildConfig? BuildConfig { get; set; }

    /// <summary>Per-environment deployment configuration.</summary>
    [JsonPropertyName("deployment_configs")]
    public PagesDeploymentConfigs? DeploymentConfigs { get; set; }

    /// <summary>
    /// Validates the two required fields and the name pattern Cloudflare enforces, before the call
    /// is made.
    /// </summary>
    public void Validate()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
        {
            problems.Add($"{nameof(Name)} is required.");
        }
        else if (!IsValidProjectName(Name!))
        {
            problems.Add(
                $"{nameof(Name)} '{Name}' is not a valid Pages project name. It must begin with a lowercase " +
                "letter or digit and contain only lowercase letters, digits and hyphens.");
        }

        if (string.IsNullOrWhiteSpace(ProductionBranch))
        {
            problems.Add($"{nameof(ProductionBranch)} is required (for example 'main').");
        }

        if (problems.Count > 0)
        {
            throw new Exceptions.CloudflareConfigurationException(problems);
        }
    }

    /// <summary>Checks the Pages project name rules.</summary>
    /// <param name="name">Candidate name.</param>
    public static bool IsValidProjectName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 58)
        {
            return false;
        }

        if (!char.IsAsciiLetterLower(name[0]) && !char.IsAsciiDigit(name[0]))
        {
            return false;
        }

        foreach (var character in name)
        {
            if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '-')
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Request body of <c>PATCH /accounts/{account_id}/pages/projects/{project_name}</c>.
/// Only set members are sent.
/// </summary>
public sealed class UpdatePagesProjectRequest
{
    /// <summary>New production branch.</summary>
    [JsonPropertyName("production_branch")]
    public string? ProductionBranch { get; set; }

    /// <summary>New build configuration.</summary>
    [JsonPropertyName("build_config")]
    public PagesBuildConfig? BuildConfig { get; set; }

    /// <summary>New per-environment deployment configuration.</summary>
    [JsonPropertyName("deployment_configs")]
    public PagesDeploymentConfigs? DeploymentConfigs { get; set; }

    /// <summary>Git source configuration.</summary>
    [JsonPropertyName("source")]
    public PagesProjectSource? Source { get; set; }
}

/// <summary>
/// Request body of <c>POST .../pages/projects/{project_name}/deployments</c>.
/// </summary>
/// <remarks>
/// An empty body starts a Direct Upload deployment; Cloudflare then expects the asset upload steps.
/// </remarks>
public sealed class CreatePagesDeploymentRequest
{
    /// <summary>Branch the deployment represents.</summary>
    [JsonPropertyName("branch")]
    public string? Branch { get; set; }

    /// <summary>Manifest of the files that will be uploaded.</summary>
    [JsonPropertyName("manifest")]
    public JsonElement? Manifest { get; set; }

    /// <summary>Environment variables for this deployment.</summary>
    [JsonPropertyName("env_vars")]
    public Dictionary<string, PagesEnvVar>? EnvVars { get; set; }

    /// <summary>Build configuration override.</summary>
    [JsonPropertyName("build_config")]
    public PagesBuildConfig? BuildConfig { get; set; }

    /// <summary>Deployment configuration override.</summary>
    [JsonPropertyName("deployment_config")]
    public PagesDeploymentConfig? DeploymentConfig { get; set; }

    /// <summary>Deployment id to reuse, when resuming an interrupted upload.</summary>
    [JsonPropertyName("deployment_id")]
    public string? DeploymentId { get; set; }

    /// <summary>Aliases to attach to the deployment.</summary>
    [JsonPropertyName("aliases")]
    public List<string>? Aliases { get; set; }

    /// <summary>Additional fields merged into the body as-is.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>A custom domain attached to a Pages project.</summary>
public sealed class PagesDomain
{
    /// <summary>The domain name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Project the domain is attached to.</summary>
    [JsonPropertyName("project_name")]
    public string? ProjectName { get; set; }

    /// <summary>Verification status.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Verification data (TXT record details).</summary>
    [JsonPropertyName("verification_data")]
    public JsonElement? VerificationData { get; set; }

    /// <summary>Ownership verification data.</summary>
    [JsonPropertyName("ownership_verification_data")]
    public JsonElement? OwnershipVerificationData { get; set; }

    /// <summary>Creation timestamp.</summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>Request body of <c>PATCH .../pages/projects/{project_name}/domains/{domain_name}</c>.</summary>
public sealed class UpdatePagesDomainRequest
{
    /// <summary>Whether the domain has been verified.</summary>
    [JsonPropertyName("verified")]
    public bool? Verified { get; set; }

    /// <summary>Whether ownership of the domain has been verified.</summary>
    [JsonPropertyName("ownership_verified")]
    public bool? OwnershipVerified { get; set; }
}

/// <summary>Request body of <c>POST .../pages/projects/{project_name}/domains</c>.</summary>
public sealed class AddPagesDomainRequest
{
    /// <summary>Creates a request for one domain.</summary>
    /// <param name="name">The domain name to attach.</param>
    public AddPagesDomainRequest(string name)
    {
        Name = name;
    }

    /// <summary>Parameterless constructor for deserialisation.</summary>
    [System.Text.Json.Serialization.JsonConstructor]
    public AddPagesDomainRequest()
    {
    }

    /// <summary>The domain name to attach.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>Query filters for the Pages domains list endpoint.</summary>
public sealed class PagesDomainListParameters : CloudflarePagedParameters
{
}
