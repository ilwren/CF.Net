using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Models.Ssl;

/// <summary>
/// The zone-wide fallback origin for SSL for SaaS: where traffic goes for a custom hostname that does not
/// name its own <c>custom_origin_server</c>. Read from and written to
/// <c>/zones/{zone_id}/custom_hostnames/fallback_origin</c>.
/// </summary>
/// <remarks>
/// The <c>errors</c> list is not the envelope's <c>errors</c>: it is activation problems Cloudflare hit
/// while trying to bring this origin up, reported as plain strings inside a successful response. A fallback
/// origin can therefore be <c>active</c>-bound and broken at the same time, which is why reading it back
/// after setting it is worth the call.
/// </remarks>
public sealed class CustomHostnameFallbackOrigin
{
    /// <summary>When the fallback origin was created.</summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Errors encountered while trying to activate it, as plain strings.</summary>
    [JsonPropertyName("errors")]
    public List<string>? Errors { get; set; }

    /// <summary>The origin hostname requests to custom hostnames are sent to, at most 255 characters.</summary>
    [JsonPropertyName("origin")]
    public string? Origin { get; set; }

    /// <summary>
    /// Activation status, one of <see cref="CustomHostnameFallbackOriginStatuses"/>.
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>When it was last updated.</summary>
    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Whether Cloudflare reported any problem activating it.</summary>
    [JsonIgnore]
    public bool HasErrors => Errors is { Count: > 0 };

    /// <inheritdoc />
    public override string ToString()
    {
        if (Origin is not { Length: > 0 } origin)
        {
            return Status ?? "(no fallback origin)";
        }

        var state = Status is { Length: > 0 } status ? status : "unknown";
        return HasErrors ? $"{origin} ({state}, {Errors!.Count} activation errors)" : $"{origin} ({state})";
    }
}

/// <summary>Body of <c>PUT /zones/{zone_id}/custom_hostnames/fallback_origin</c>.</summary>
/// <remarks>
/// The endpoint is a <c>PUT</c> and the body is one field, so there is nothing to merge and nothing else to
/// send: setting a fallback origin replaces whatever was there.
/// </remarks>
public sealed class UpdateCustomHostnameFallbackOriginRequest
{
    /// <summary>The origin hostname to send custom hostname traffic to. Required.</summary>
    [JsonPropertyName("origin")]
    public string? Origin { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Builds the body for one origin.</summary>
    /// <param name="origin">Origin hostname, for example <c>fallback.example.com</c>.</param>
    public static UpdateCustomHostnameFallbackOriginRequest For(string origin) => new() { Origin = origin };

    /// <summary>Checks the request before anything is sent.</summary>
    /// <exception cref="Exceptions.CloudflareConfigurationException">The origin is missing.</exception>
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Origin))
        {
            throw new Exceptions.CloudflareConfigurationException(
                "A fallback origin needs the origin hostname to fall back to.");
        }
    }

    /// <inheritdoc />
    public override string ToString() => Origin ?? "(no origin)";
}

/// <summary>Activation statuses of a custom hostname fallback origin.</summary>
public static class CustomHostnameFallbackOriginStatuses
{
    /// <summary>Setup has begun.</summary>
    public const string Initializing = "initializing";

    /// <summary>Waiting to be deployed to the edge.</summary>
    public const string PendingDeployment = "pending_deployment";

    /// <summary>Deletion has started.</summary>
    public const string PendingDeletion = "pending_deletion";

    /// <summary>Live.</summary>
    public const string Active = "active";

    /// <summary>Deployment took too long.</summary>
    public const string DeploymentTimedOut = "deployment_timed_out";

    /// <summary>Deletion took too long.</summary>
    public const string DeletionTimedOut = "deletion_timed_out";

    /// <summary>Every status the schema defines, in the order it defines them.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Initializing, PendingDeployment, PendingDeletion, Active, DeploymentTimedOut, DeletionTimedOut,
    };
}
