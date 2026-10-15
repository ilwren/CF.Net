using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Models.Workers;

/// <summary>
/// One Cron Trigger on a Worker script: the schedule, plus when Cloudflare recorded it.
/// </summary>
/// <remarks>
/// The same three fields are the whole request body of the update and the items of both responses, so one
/// type serves all three. <c>created_on</c> and <c>modified_on</c> are Cloudflare's to write: an update sends
/// <c>cron</c> alone, because nulls are not serialised. The reference publishes no format for either
/// timestamp, only that they are strings, so the integration run is what confirms they parse as timestamps
/// here rather than being kept as text.
/// </remarks>
public sealed class WorkerSchedule
{
    /// <summary>Creates a trigger from a cron expression.</summary>
    /// <param name="cron">The expression, e.g. <c>*/30 * * * *</c>.</param>
    public WorkerSchedule(string cron)
    {
        Cron = cron;
    }

    /// <summary>Parameterless constructor for object initialisers and deserialisation.</summary>
    [JsonConstructor]
    public WorkerSchedule()
    {
    }

    /// <summary>
    /// The cron expression. Cloudflare validates it against its own scheduler; this library only insists
    /// that it is not blank, because guessing at the grammar would reject expressions the API accepts.
    /// </summary>
    [JsonPropertyName("cron")]
    public string? Cron { get; set; }

    /// <summary>When the trigger was created. Written by Cloudflare, not by a caller.</summary>
    [JsonPropertyName("created_on")]
    public DateTimeOffset? CreatedOn { get; set; }

    /// <summary>When the trigger was last modified. Written by Cloudflare, not by a caller.</summary>
    [JsonPropertyName("modified_on")]
    public DateTimeOffset? ModifiedOn { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => Cron ?? string.Empty;
}

/// <summary>
/// The <c>result</c> of both
/// <c>GET /accounts/{account_id}/workers/scripts/{script_name}/schedules</c> and its <c>PUT</c>.
/// </summary>
/// <remarks>
/// The third Workers endpoint in a row that nests its array inside <c>result</c> - under <c>schedules</c> -
/// and returns no <c>result_info</c>, so <see cref="Http.CloudflarePage{T}"/>, which expects <c>result</c> to
/// be the array itself, does not describe it. Unlike the versions and deployments endpoints, whose read and
/// write answers differ, these two are published with the same shape, so one type serves both.
/// </remarks>
public sealed class WorkerScheduleListResult
{
    /// <summary>The script's cron triggers. The <c>PUT</c> replaces this whole set.</summary>
    [JsonPropertyName("schedules")]
    public List<WorkerSchedule>? Schedules { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>How many triggers the script has.</summary>
    [JsonIgnore]
    public int Count => Schedules?.Count ?? 0;

    /// <summary>The expressions on their own, in the order Cloudflare returned them.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> Expressions =>
        (Schedules ?? new List<WorkerSchedule>())
            .Select(schedule => schedule.Cron ?? string.Empty)
            .Where(expression => expression.Length > 0)
            .ToList();

    /// <inheritdoc />
    public override string ToString() =>
        Count == 0 ? "no cron triggers" : string.Join(", ", Expressions);
}
