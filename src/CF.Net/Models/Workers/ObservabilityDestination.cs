using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Http;

namespace CFNet.Models.Workers;

/// <summary>
/// A Workers Observability telemetry destination: somewhere a Worker's logs, traces or metrics are exported
/// to.
/// </summary>
/// <remarks>
/// <para>
/// Destinations are account-level, and a Worker reaches one by being named in <see cref="Scripts"/> or by
/// having the destination set in its own script settings - see <c>WorkerObservabilityLogs.Destinations</c>
/// and <c>WorkerObservabilityTraces.Destinations</c>, which carry slugs of destinations defined here.
/// </para>
/// <para>
/// All four endpoints on this resource answer with this same object, including the delete, which returns the
/// destination as it was rather than an empty result.
/// </para>
/// </remarks>
public sealed class ObservabilityDestination
{
    /// <summary>The destination's slug, which every call after the create addresses it by.</summary>
    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    /// <summary>The name given to the destination when it was created.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Whether anything is currently being exported to it.</summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    /// <summary>The Workers exporting to this destination.</summary>
    [JsonPropertyName("scripts")]
    public List<string>? Scripts { get; set; }

    /// <summary>Where and how the telemetry is delivered.</summary>
    [JsonPropertyName("configuration")]
    public ObservabilityDestinationConfiguration? Configuration { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Name and slug, on one line.</summary>
    /// <returns>The destination described in one line.</returns>
    public override string ToString() => $"{Name} ({Slug})";
}

/// <summary>
/// Where and how a destination delivers telemetry.
/// </summary>
/// <remarks>
/// <para>
/// The field names here are camelCase on the wire (<c>logpushDataset</c>, <c>jobStatus</c>,
/// <c>replaceHeaders</c>) while the job status inside is snake_case, so every property carries its wire name
/// explicitly rather than relying on a naming policy.
/// </para>
/// <para>
/// <see cref="LogpushDataset"/> is set when the destination is created and is not part of an update: which
/// kind of telemetry a destination carries cannot be changed afterwards. <see cref="JobStatus"/> and
/// <see cref="ReplaceHeaders"/> are read-only and write-only halves of the same type - the first only ever
/// arrives, the second is only ever sent.
/// </para>
/// <para>
/// <see cref="Headers"/> is required on a create and on any update that sends a configuration, and required
/// to be an object: a destination with no headers sends <c>{}</c>. Observed live, leaving it out answers 400
/// with <c>invalid_type</c> at <c>configuration.headers</c>, and the camelCase names above were echoed back
/// unchanged in the same answer, so they are what the endpoint really spells them.
/// </para>
/// <para>
/// Because this library never writes a JSON <c>null</c>, a header whose value is set to <c>null</c> is
/// dropped from the request rather than sent as null. Removing a header therefore means sending
/// <see cref="ReplaceHeaders"/> as <c>true</c> together with the headers that should remain.
/// </para>
/// </remarks>
public sealed class ObservabilityDestinationConfiguration
{
    /// <summary>The destination kind. <c>logpush</c> is the only one published.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>The address telemetry is delivered to.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>Headers sent with each delivery, for authenticating against the receiving end.</summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string?>? Headers { get; set; }

    /// <summary>Which telemetry this destination carries: one of <c>ObservabilityLogpushDatasets</c>.</summary>
    [JsonPropertyName("logpushDataset")]
    public string? LogpushDataset { get; set; }

    /// <summary>How the last delivery went. Read-only.</summary>
    [JsonPropertyName("jobStatus")]
    public ObservabilityDestinationJobStatus? JobStatus { get; set; }

    /// <summary>
    /// Whether <see cref="Headers"/> replaces the destination's headers wholesale instead of merging into
    /// them. Write-only, and only on an update.
    /// </summary>
    [JsonPropertyName("replaceHeaders")]
    public bool? ReplaceHeaders { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>How a destination's last export job went.</summary>
public sealed class ObservabilityDestinationJobStatus
{
    /// <summary>What went wrong, when something did.</summary>
    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// When the last export completed. Cloudflare types this as a string rather than a timestamp, and it is
    /// left as one here so an empty or unexpected value is readable rather than a parse failure.
    /// </summary>
    [JsonPropertyName("last_complete")]
    public string? LastComplete { get; set; }

    /// <summary>When the last export failed, in the same string form.</summary>
    [JsonPropertyName("last_error")]
    public string? LastError { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>The destination kinds <c>configuration.type</c> accepts.</summary>
public static class ObservabilityDestinationTypes
{
    /// <summary>Deliver through Logpush. The only kind published.</summary>
    public const string Logpush = "logpush";
}

/// <summary>The telemetry kinds <c>configuration.logpushDataset</c> accepts.</summary>
public static class ObservabilityLogpushDatasets
{
    /// <summary>OpenTelemetry traces.</summary>
    public const string OpenTelemetryTraces = "opentelemetry-traces";

    /// <summary>OpenTelemetry logs.</summary>
    public const string OpenTelemetryLogs = "opentelemetry-logs";

    /// <summary>OpenTelemetry metrics.</summary>
    public const string OpenTelemetryMetrics = "opentelemetry-metrics";
}

/// <summary>The directions <c>order</c> accepts when listing destinations.</summary>
public static class ObservabilityDestinationOrders
{
    /// <summary>Oldest first.</summary>
    public const string Ascending = "asc";

    /// <summary>Newest first.</summary>
    public const string Descending = "desc";
}

/// <summary>The fields <c>orderBy</c> accepts when listing destinations.</summary>
public static class ObservabilityDestinationOrderBy
{
    /// <summary>Order by when the destination was created.</summary>
    public const string Created = "created";

    /// <summary>Order by when it was last updated.</summary>
    public const string Updated = "updated";
}

/// <summary>Creates a telemetry destination.</summary>
/// <remarks>
/// <para>
/// The body of <c>POST /accounts/{account_id}/workers/observability/destinations</c>. A name, an enabled flag
/// and a configuration are all required by the published schema, and the configuration's dataset is fixed
/// here because an update cannot change it.
/// </para>
/// <para>
/// Cloudflare checks that the destination can actually be delivered to before creating it, unless
/// <see cref="SkipPreflightCheck"/> says not to.
/// </para>
/// </remarks>
public sealed class CreateObservabilityDestinationRequest
{
    /// <summary>A name for the destination.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Whether telemetry starts flowing to it straight away.</summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    /// <summary>Where and how it delivers.</summary>
    [JsonPropertyName("configuration")]
    public ObservabilityDestinationConfiguration? Configuration { get; set; }

    /// <summary>Whether to create it without first checking that delivery would work.</summary>
    [JsonPropertyName("skipPreflightCheck")]
    public bool? SkipPreflightCheck { get; set; }

    /// <summary>
    /// Builds a request for a Logpush destination, which is the only kind published.
    /// </summary>
    /// <param name="name">A name for the destination.</param>
    /// <param name="url">The address telemetry is delivered to.</param>
    /// <param name="logpushDataset">One of <c>ObservabilityLogpushDatasets</c>.</param>
    /// <param name="enabled">Whether telemetry starts flowing straight away.</param>
    /// <returns>A request ready to send.</returns>
    public static CreateObservabilityDestinationRequest ForLogpush(
        string name,
        string url,
        string logpushDataset,
        bool enabled = false) =>
        new()
        {
            Name = name,
            Enabled = enabled,
            Configuration = new ObservabilityDestinationConfiguration
            {
                Type = ObservabilityDestinationTypes.Logpush,
                Url = url,
                LogpushDataset = logpushDataset,

                // Required, and required to be an object: a destination with no headers still has to say so.
                Headers = new Dictionary<string, string?>(),
            },
        };

    /// <summary>
    /// Checks the request against what the published schema requires, so a missing half is reported here
    /// rather than as a 400 from Cloudflare.
    /// </summary>
    /// <exception cref="Exceptions.CloudflareConfigurationException">Something required is missing.</exception>
    public void EnsureValid()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
        {
            problems.Add("Name is required to create a destination.");
        }

        if (Enabled is null)
        {
            problems.Add(
                "Enabled is required: say whether telemetry should start flowing to this destination, " +
                "because Cloudflare will not assume either way.");
        }

        if (Configuration is null)
        {
            problems.Add("Configuration is required: a destination needs somewhere to deliver to.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(Configuration.Type))
            {
                problems.Add(
                    $"Configuration.Type is required; {ObservabilityDestinationTypes.Logpush} is the only " +
                    "kind published.");
            }

            if (string.IsNullOrWhiteSpace(Configuration.Url))
            {
                problems.Add("Configuration.Url is required: it is the address telemetry is delivered to.");
            }

            if (Configuration.Headers is null)
            {
                problems.Add(
                    "Configuration.Headers is required and has to be an object, so a destination that carries " +
                    "no headers sends an empty one. Observed live: leaving it out answers 400 with " +
                    "'invalid_type' at configuration.headers, expecting a record.");
            }

            if (string.IsNullOrWhiteSpace(Configuration.LogpushDataset))
            {
                problems.Add(
                    "Configuration.LogpushDataset is required and cannot be changed later, so it has to be " +
                    "one of ObservabilityLogpushDatasets.");
            }
        }

        if (problems.Count > 0)
        {
            throw new Exceptions.CloudflareConfigurationException(problems);
        }
    }
}

/// <summary>Changes a telemetry destination.</summary>
/// <remarks>
/// The body of <c>PATCH /accounts/{account_id}/workers/observability/destinations/{slug}</c>.
/// <see cref="Enabled"/> is required even when it is not what is being changed, so turning a destination's
/// headers or address round means sending its enabled flag again with them. The configuration here takes a
/// type, a url, headers and <c>replaceHeaders</c>; the dataset is not part of an update.
/// </remarks>
public sealed class UpdateObservabilityDestinationRequest
{
    /// <summary>Whether telemetry flows to it. Required by the schema on every update.</summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    /// <summary>Where and how it delivers, when that is what is changing.</summary>
    [JsonPropertyName("configuration")]
    public ObservabilityDestinationConfiguration? Configuration { get; set; }

    /// <summary>
    /// Checks the request against what the published schema requires.
    /// </summary>
    /// <exception cref="Exceptions.CloudflareConfigurationException">The enabled flag is missing.</exception>
    public void EnsureValid()
    {
        var problems = new List<string>();

        if (Enabled is null)
        {
            problems.Add(
                "Enabled is required on every update of a destination, even when it is not the field being " +
                "changed. Send the value it already has to leave it alone.");
        }

        if (Configuration is not null)
        {
            if (string.IsNullOrWhiteSpace(Configuration.Type))
            {
                problems.Add(
                    $"Configuration.Type is required whenever a configuration is sent; " +
                    $"{ObservabilityDestinationTypes.Logpush} is the only kind published.");
            }

            if (string.IsNullOrWhiteSpace(Configuration.Url))
            {
                problems.Add("Configuration.Url is required whenever a configuration is sent.");
            }

            if (Configuration.Headers is null)
            {
                problems.Add(
                    "Configuration.Headers is required whenever a configuration is sent, and has to be an " +
                    "object. Send the headers that should remain with ReplaceHeaders set, or an empty object " +
                    "with it unset to leave the destination's headers alone.");
            }
        }

        if (problems.Count > 0)
        {
            throw new Exceptions.CloudflareConfigurationException(problems);
        }
    }
}

/// <summary>Filters for listing telemetry destinations.</summary>
/// <remarks>
/// The query names here are camelCase on the wire (<c>orderBy</c>, <c>perPage</c>) rather than Cloudflare's
/// usual snake_case, which is why this does not extend <c>CloudflarePagedParameters</c>.
/// </remarks>
public sealed class ObservabilityDestinationListParameters : ICloudflareQueryParameters
{
    /// <summary>Direction, one of <c>ObservabilityDestinationOrders</c>.</summary>
    public string? Order { get; set; }

    /// <summary>What to order by, one of <c>ObservabilityDestinationOrderBy</c>.</summary>
    public string? OrderBy { get; set; }

    /// <summary>1-based page number.</summary>
    public int? Page { get; set; }

    /// <summary>Page size.</summary>
    public int? PerPage { get; set; }

    /// <inheritdoc />
    public void Apply(QueryStringBuilder query)
    {
        query.AddIfNotEmpty("order", Order);
        query.AddIfNotEmpty("orderBy", OrderBy);
        query.Add("page", Page);
        query.Add("perPage", PerPage);
    }

    /// <summary>
    /// Checks the filter values against the ones the endpoint publishes.
    /// </summary>
    /// <exception cref="Exceptions.CloudflareConfigurationException">A filter value is not one it accepts.</exception>
    public void EnsureValid()
    {
        var problems = new List<string>();

        if (Order is { Length: > 0 } order &&
            order != ObservabilityDestinationOrders.Ascending &&
            order != ObservabilityDestinationOrders.Descending)
        {
            problems.Add(
                $"Order must be '{ObservabilityDestinationOrders.Ascending}' or " +
                $"'{ObservabilityDestinationOrders.Descending}', but was '{order}'.");
        }

        if (OrderBy is { Length: > 0 } orderBy &&
            orderBy != ObservabilityDestinationOrderBy.Created &&
            orderBy != ObservabilityDestinationOrderBy.Updated)
        {
            problems.Add(
                $"OrderBy must be '{ObservabilityDestinationOrderBy.Created}' or " +
                $"'{ObservabilityDestinationOrderBy.Updated}', but was '{orderBy}'.");
        }

        if (Page is < 1)
        {
            problems.Add($"Page must be at least 1, but was {Page}.");
        }

        if (PerPage is < 1)
        {
            problems.Add($"PerPage must be at least 1, but was {PerPage}.");
        }

        if (problems.Count > 0)
        {
            throw new Exceptions.CloudflareConfigurationException(problems);
        }
    }
}
