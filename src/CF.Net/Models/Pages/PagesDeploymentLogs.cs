using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Models.Pages;

/// <summary>
/// The build logs of one Pages deployment, from
/// <c>GET /accounts/{account_id}/pages/projects/{project_name}/deployments/{deployment_id}/history/logs</c>.
/// </summary>
/// <remarks>
/// Cloudflare returns one page-worth of lines in <see cref="Data"/> and the count it has in
/// <see cref="Total"/>, so the two disagree when a build produced more than the API is willing to return in
/// one call. <see cref="IncludesContainerLogs"/> says whether the build container's own output is mixed in.
/// </remarks>
public sealed class PagesDeploymentLogs
{
    /// <summary>The log lines, in the order Cloudflare returns them.</summary>
    [JsonPropertyName("data")]
    public List<PagesDeploymentLogLine>? Data { get; set; }

    /// <summary>Whether the build container's logs are included.</summary>
    [JsonPropertyName("includes_container_logs")]
    public bool? IncludesContainerLogs { get; set; }

    /// <summary>How many lines Cloudflare has for this deployment.</summary>
    [JsonPropertyName("total")]
    public int? Total { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The lines joined with newlines - the form you want to read, print or paste into a bug report.</summary>
    [JsonIgnore]
    public string Text
    {
        get
        {
            if (Data is null || Data.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var line in Data)
            {
                builder.AppendLine(line.Line ?? string.Empty);
            }

            return builder.ToString();
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"{Data?.Count ?? 0} of {Total ?? 0} log lines";
}

/// <summary>One line of a Pages deployment's build log.</summary>
public sealed class PagesDeploymentLogLine
{
    /// <summary>The line itself.</summary>
    [JsonPropertyName("line")]
    public string? Line { get; set; }

    /// <summary>
    /// When the line was written. Cloudflare types this as a string rather than a timestamp, so it is kept
    /// as one instead of being parsed on a guess about its format.
    /// </summary>
    [JsonPropertyName("ts")]
    public string? Timestamp { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => Line ?? string.Empty;
}
