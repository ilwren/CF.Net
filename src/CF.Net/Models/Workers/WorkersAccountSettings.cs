using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Models.Workers;

/// <summary>
/// The account-wide Workers settings, as <c>/accounts/{account_id}/workers/account-settings</c> answers them
/// and as the <c>PUT</c> of the same path takes them.
/// </summary>
/// <remarks>
/// <para>
/// The read and the write are the same two fields, so one type serves both: what a <c>GET</c> answers can be
/// handed straight back to a <c>PUT</c>, which is how these settings are changed without guessing at the one
/// the account already has. A <c>PUT</c> is a replacement of what is sent, so a field left null is a field
/// left for Cloudflare to decide rather than one preserved from the read.
/// </para>
/// <para>
/// These are account defaults, not a script's settings: a script's own <c>logpush</c>, <c>observability</c> and
/// <c>tags</c> live on <c>…/workers/scripts/{script_name}/script-settings</c> and are
/// <see cref="WorkerScriptSettings"/>.
/// </para>
/// </remarks>
public sealed class WorkersAccountSettings
{
    /// <summary>
    /// The usage model a new Worker in this account starts on, as the word Cloudflare uses for it. The
    /// published schema types this as a plain string and does not enumerate the words; <see cref="WorkerUsageModels"/>
    /// carries the ones this library has met - <c>standard</c> among them, on an account whose plan has no
    /// Workers Paid subscription - and is deliberately constants rather than an enum, because a word it has not
    /// met is not necessarily a wrong one.
    /// </summary>
    public string? DefaultUsageModel { get; set; }

    /// <summary>
    /// Whether Workers in this account prefer to run on renewable energy. Cloudflare answers a real boolean
    /// for this one, so it is typed as one.
    /// </summary>
    public bool? GreenCompute { get; set; }

    /// <summary>Members Cloudflare added after this library was released.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Builds a write from the two settings, either of which may be left for Cloudflare.</summary>
    /// <param name="defaultUsageModel">The usage model new Workers start on, or null to leave it alone.</param>
    /// <param name="greenCompute">Whether to prefer renewable energy, or null to leave it alone.</param>
    /// <returns>A settings object ready to put.</returns>
    public static WorkersAccountSettings Of(string? defaultUsageModel = null, bool? greenCompute = null) =>
        new() { DefaultUsageModel = defaultUsageModel, GreenCompute = greenCompute };

    /// <summary>Both settings on one line.</summary>
    /// <returns>The settings described in one line.</returns>
    public override string ToString() =>
        "default_usage_model=" + (DefaultUsageModel ?? "(unset)") +
        ", green_compute=" + (GreenCompute?.ToString() ?? "(unset)");
}
