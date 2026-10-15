using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Http;
using CFNet.Models.Common;

namespace CFNet.Models.Zones;

/// <summary>Query filters for <c>GET /zones</c>.</summary>
public sealed class ZoneListParameters : CloudflarePagedParameters
{
    /// <summary>Exact domain name to match.</summary>
    public string? Name { get; set; }

    /// <summary>Only zones with this activation status.</summary>
    public string? Status { get; set; }

    /// <summary>Only zones belonging to this account. Serialised as <c>account.id</c>.</summary>
    public string? AccountId { get; set; }

    /// <summary>Field to sort by, e.g. <c>name</c>, <c>status</c>, <c>plan.id</c>.</summary>
    public string? Order { get; set; }

    /// <summary>Sort direction.</summary>
    public SortDirection? Direction { get; set; }

    /// <summary>Whether all filters must match or any of them may.</summary>
    public FilterMatchMode? Match { get; set; }

    /// <inheritdoc />
    public override void Apply(QueryStringBuilder query)
    {
        base.Apply(query);
        query.AddIfNotEmpty("name", Name);
        query.AddIfNotEmpty("status", Status);
        query.AddIfNotEmpty("account.id", AccountId);
        query.AddIfNotEmpty("order", Order);
        if (Direction.HasValue)
        {
            query.Add("direction", QueryEnums.ToWire(Direction.Value));
        }

        if (Match.HasValue)
        {
            query.Add("match", QueryEnums.ToWire(Match.Value));
        }
    }
}

/// <summary>Request body of <c>POST /zones</c>.</summary>
public sealed class CreateZoneRequest
{
    /// <summary>Creates a request for a full zone on the given account.</summary>
    /// <param name="name">Domain name, e.g. <c>example.com</c>.</param>
    /// <param name="accountId">Owning account id.</param>
    /// <param name="type">Zone type; defaults to <c>full</c>.</param>
    public CreateZoneRequest(string name, string accountId, string type = ZoneTypeValues.Full)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Account = new CloudflareIdentifier { Id = accountId ?? throw new ArgumentNullException(nameof(accountId)) };
        Type = type;
    }

    /// <summary>Parameterless constructor for configuration binding and deserialisation.</summary>
    [System.Text.Json.Serialization.JsonConstructor]
    public CreateZoneRequest()
    {
    }

    /// <summary>The domain name to add.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>The account that will own the zone. Only <c>id</c> is read by the API.</summary>
    [JsonPropertyName("account")]
    public CloudflareIdentifier? Account { get; set; }

    /// <summary><c>full</c> (default) or <c>partial</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

/// <summary>Request body of <c>PATCH /zones/{zone_id}</c>. Only set members are sent.</summary>
public sealed class UpdateZoneRequest
{
    /// <summary>Pause or unpause the zone.</summary>
    [JsonPropertyName("paused")]
    public bool? Paused { get; set; }

    /// <summary>Change the plan. Only <c>id</c> is read by the API.</summary>
    [JsonPropertyName("plan")]
    public CloudflareIdentifier? Plan { get; set; }

    /// <summary>Switch between <c>full</c> and <c>partial</c> setup.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Vanity nameservers for enterprise accounts.</summary>
    [JsonPropertyName("vanity_name_servers")]
    public List<string>? VanityNameServers { get; set; }

    /// <summary>Convenience: sets <see cref="Plan"/> from a plan id.</summary>
    /// <param name="planId">The target plan id.</param>
    public UpdateZoneRequest WithPlan(string planId)
    {
        Plan = new CloudflareIdentifier { Id = planId };
        return this;
    }
}

/// <summary>Request body of <c>POST /zones/{zone_id}/purge_cache</c>.</summary>
/// <remarks>
/// Either <see cref="PurgeEverything"/> or at least one of the targeted collections must be set;
/// Cloudflare rejects a request that has neither.
/// </remarks>
public sealed class PurgeCacheRequest
{
    /// <summary>Creates a request that purges the whole cache.</summary>
    public static PurgeCacheRequest Everything() => new() { PurgeEverything = true };

    /// <summary>Creates a request that purges specific URLs.</summary>
    /// <param name="files">Absolute URLs to purge.</param>
    public static PurgeCacheRequest ForFiles(IEnumerable<string> files) => new() { Files = files?.ToList() };

    /// <summary>Creates a request that purges by cache tags.</summary>
    /// <param name="tags">Cache tags to purge.</param>
    public static PurgeCacheRequest ForTags(IEnumerable<string> tags) => new() { Tags = tags?.ToList() };

    /// <summary>Purge every cached resource. Enterprise only for the targeted variants.</summary>
    [JsonPropertyName("purge_everything")]
    public bool? PurgeEverything { get; set; }

    /// <summary>Absolute URLs to purge.</summary>
    [JsonPropertyName("files")]
    public List<string>? Files { get; set; }

    /// <summary>Cache tags to purge.</summary>
    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    /// <summary>Hostnames to purge.</summary>
    [JsonPropertyName("hosts")]
    public List<string>? Hosts { get; set; }

    /// <summary>URL prefixes to purge.</summary>
    [JsonPropertyName("prefixes")]
    public List<string>? Prefixes { get; set; }

    /// <summary>Tags that must not be purged even when matched.</summary>
    [JsonPropertyName("ignore_tags")]
    public List<string>? IgnoreTags { get; set; }

    /// <summary>
    /// Validates the request the way Cloudflare would, so a bad call fails locally instead of
    /// after a round trip.
    /// </summary>
    public void EnsureValid()
    {
        var hasTarget =
            (Files is { Count: > 0 }) ||
            (Tags is { Count: > 0 }) ||
            (Hosts is { Count: > 0 }) ||
            (Prefixes is { Count: > 0 });

        if (PurgeEverything != true && !hasTarget)
        {
            throw new Exceptions.CloudflareConfigurationException(
                "A purge request must either set PurgeEverything = true or list at least one of Files, Tags, Hosts or Prefixes.");
        }

        if (PurgeEverything == true && hasTarget)
        {
            throw new Exceptions.CloudflareConfigurationException(
                "Cloudflare rejects a purge request that combines PurgeEverything with targeted Files/Tags/Hosts/Prefixes.");
        }
    }
}

/// <summary>Result of <c>POST /zones/{zone_id}/purge_cache</c>.</summary>
public sealed class PurgeCacheResult
{
    /// <summary>Timestamp when the purge finished.</summary>
    [JsonPropertyName("purge_time")]
    public int? PurgeTime { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>Result of <c>POST /zones/{zone_id}/activation_check</c>.</summary>
public sealed class ZoneActivationCheckResult
{
    /// <summary>Identifier of the zone the check was queued for.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}
