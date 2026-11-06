using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Http;
using CFNet.Models.Common;
using CFNet.Serialization;

namespace CFNet.Models.Turnstile;

/// <summary>
/// One Turnstile widget as a <b>list</b> answers it: everything Cloudflare publishes about a widget except its
/// secret.
/// </summary>
/// <remarks>
/// <para>
/// Cloudflare types the list's item and a single widget's answer as two shapes, and the difference is the one
/// field that matters most: <c>GET …/challenges/widgets</c> answers every widget without <c>secret</c>, where
/// a get, create, update, delete or rotation of one answers it with. So the list is typed as this class and a
/// single widget as <see cref="TurnstileWidget"/>, which adds the secret: a caller who has a list cannot read
/// a secret out of it by accident, and does not have to wonder why one is null.
/// </para>
/// <para>
/// Both timestamps arrive as strings, because this endpoint's are ISO 8601 in Cloudflare's own examples and
/// nothing here has seen one live in another shape - <see cref="CloudflareTimestamps"/> reads either way if
/// that changes, and <see cref="CreatedOnUtc"/> and <see cref="ModifiedOnUtc"/> are where it would show.
/// </para>
/// </remarks>
public class TurnstileWidgetSummary
{
    /// <summary>The widget's site key, the public half of its pair and the id every method here uses.</summary>
    [JsonPropertyName("sitekey")]
    public string? Sitekey { get; set; }

    /// <summary>The widget's name, as it appears in the dashboard.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>How the challenge presents itself to a visitor.</summary>
    [JsonPropertyName("mode")]
    public TurnstileWidgetMode? Mode { get; set; }

    /// <summary>
    /// The hostnames the widget is allowed to run on. Cloudflare validates every one of them and refuses a
    /// pattern it does not accept: a live <c>PUT</c> carrying <c>*.example.org</c> answered 400 with
    /// <c>invalid value in `domains`, found invalid hostname</c>, so a wildcard subdomain is not one, however
    /// the dashboard's "all hostnames" is spelled. Nothing here validates a hostname itself - Cloudflare is
    /// the authority on which ones it accepts, and it says so immediately.
    /// </summary>
    [JsonPropertyName("domains")]
    public List<string>? Domains { get; set; }

    /// <summary>Where the challenge may be solved, which is a data-residency choice rather than a routing one.</summary>
    [JsonPropertyName("region")]
    public TurnstileRegion? Region { get; set; }

    /// <summary>How much clearance a visitor needs, for the modes that ask.</summary>
    [JsonPropertyName("clearance_level")]
    public TurnstileClearanceLevel? ClearanceLevel { get; set; }

    /// <summary>Whether Bot Fight Mode is on for the zone this widget belongs to.</summary>
    [JsonPropertyName("bot_fight_mode")]
    public bool? BotFightMode { get; set; }

    /// <summary>Whether the widget hands out a one-time token id rather than a reusable one.</summary>
    [JsonPropertyName("ephemeral_id")]
    public bool? EphemeralId { get; set; }

    /// <summary>
    /// Whether the challenge is served without Cloudflare's branding. One word on the wire, and one word here:
    /// <c>offlabel</c> rather than <c>off_label</c>.
    /// </summary>
    [JsonPropertyName("offlabel")]
    public bool? Offlabel { get; set; }

    /// <summary>When the widget was created, as Cloudflare wrote it.</summary>
    [JsonPropertyName("created_on")]
    public string? CreatedOn { get; set; }

    /// <summary>When the widget was created, as an instant at offset zero, when the string can be read as one.</summary>
    [JsonIgnore]
    public DateTimeOffset? CreatedOnUtc => CloudflareTimestamps.Read(CreatedOn);

    /// <summary>When the widget last changed, as Cloudflare wrote it.</summary>
    [JsonPropertyName("modified_on")]
    public string? ModifiedOn { get; set; }

    /// <summary>When the widget last changed, as an instant at offset zero, when the string can be read as one.</summary>
    [JsonIgnore]
    public DateTimeOffset? ModifiedOnUtc => CloudflareTimestamps.Read(ModifiedOn);

    /// <summary>What last deployed this widget: wrangler, the dashboard, the API, or nothing recorded.</summary>
    [JsonPropertyName("deployed_via")]
    public TurnstileDeploymentSource? DeployedVia { get; set; }

    /// <summary>What last changed this widget, in the same words as <see cref="DeployedVia"/>.</summary>
    [JsonPropertyName("last_modified_via")]
    public TurnstileDeploymentSource? LastModifiedVia { get; set; }

    /// <summary>Captures any field Cloudflare adds.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Name and site key, on one line. No secret is ever part of it.</summary>
    /// <returns>The widget described in one line.</returns>
    public override string ToString() => $"{Name} ({Sitekey})";
}

/// <summary>
/// One Turnstile widget as a <b>single widget's</b> answer gives it: everything in
/// <see cref="TurnstileWidgetSummary"/> plus the secret.
/// </summary>
/// <remarks>
/// A get, a create, an update, a delete and a rotation each answer the widget with its secret, so each of
/// those calls returns this type. Rotating the secret answers the <em>new</em> one, and is the only way to
/// read a secret that was not written down when the widget was made.
/// </remarks>
public sealed class TurnstileWidget : TurnstileWidgetSummary
{
    /// <summary>
    /// The widget's secret key - the half of the pair that stays on a server and is sent with a site
    /// verification. It is a credential: this library never logs it, <see cref="TurnstileWidgetSummary.ToString"/>
    /// leaves it out on purpose, and a caller is expected to keep it where the rest of its secrets are.
    /// </summary>
    [JsonPropertyName("secret")]
    public string? Secret { get; set; }
}

/// <summary>
/// The fields a widget is created with - and, because updating one is a <c>PUT</c> that replaces the whole
/// widget rather than patching part of it, the fields it is updated with too.
/// </summary>
/// <remarks>
/// <see cref="Name"/>, <see cref="Mode"/> and <see cref="Domains"/> are required by Cloudflare on both calls,
/// so an update has to send them again even when only one of them is changing: a <c>PUT</c> with a field left
/// out is not a field left alone. Everything else is optional and, left out, is not sent at all.
/// </remarks>
public sealed class TurnstileWidgetRequest
{
    /// <summary>The widget's name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>How the challenge presents itself to a visitor.</summary>
    [JsonPropertyName("mode")]
    public TurnstileWidgetMode? Mode { get; set; }

    /// <summary>
    /// The hostnames the widget may run on, which replace any it had. Each has to be one Cloudflare accepts -
    /// a wildcard subdomain such as <c>*.example.org</c> is refused with 400 and
    /// <c>invalid value in `domains`, found invalid hostname</c>, as a live <c>PUT</c> reported.
    /// </summary>
    [JsonPropertyName("domains")]
    public List<string>? Domains { get; set; }

    /// <summary>Where the challenge may be solved. Left out, Cloudflare keeps what the widget has.</summary>
    [JsonPropertyName("region")]
    public TurnstileRegion? Region { get; set; }

    /// <summary>How much clearance a visitor needs. Left out, Cloudflare keeps what the widget has.</summary>
    [JsonPropertyName("clearance_level")]
    public TurnstileClearanceLevel? ClearanceLevel { get; set; }

    /// <summary>Whether Bot Fight Mode applies. Left out, Cloudflare keeps what the widget has.</summary>
    [JsonPropertyName("bot_fight_mode")]
    public bool? BotFightMode { get; set; }

    /// <summary>Whether tokens are one-time. Left out, Cloudflare keeps what the widget has.</summary>
    [JsonPropertyName("ephemeral_id")]
    public bool? EphemeralId { get; set; }

    /// <summary>Whether the challenge is served without Cloudflare's branding.</summary>
    [JsonPropertyName("offlabel")]
    public bool? Offlabel { get; set; }

    /// <summary>Refuses a request Cloudflare would refuse, naming everything wrong with it at once.</summary>
    /// <exception cref="CFNet.Exceptions.CloudflareConfigurationException">
    /// Thrown when a required field is missing or a domain is blank; its <c>Problems</c> lists every reason.
    /// </exception>
    public void EnsureValid()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
        {
            problems.Add("name is required: Cloudflare creates and replaces a widget by name.");
        }

        if (Mode is null)
        {
            problems.Add(
                "mode is required, and has to be one Cloudflare publishes: non-interactive, invisible or " +
                "managed.");
        }

        if (Domains is not { Count: > 0 })
        {
            problems.Add(
                "domains is required and cannot be empty: a widget runs on the hostnames listed here. An " +
                "update replaces the list, so sending none would not leave the widget's hostnames alone.");
        }
        else if (Domains.Any(string.IsNullOrWhiteSpace))
        {
            problems.Add("every domain has to name a hostname; one of them is blank.");
        }

        if (problems.Count > 0)
        {
            throw new CFNet.Exceptions.CloudflareConfigurationException(problems);
        }
    }
}

/// <summary>The body of a secret rotation, which is one optional flag.</summary>
/// <remarks>
/// Rotating replaces the secret and answers the new one. Left alone, Cloudflare keeps the old secret working
/// for a grace period so a deployment can be rolled; <see cref="InvalidateImmediately"/> ends that.
/// </remarks>
public sealed class TurnstileSecretRotationRequest
{
    /// <summary>Whether the old secret stops working at once rather than after a grace period.</summary>
    [JsonPropertyName("invalidate_immediately")]
    public bool? InvalidateImmediately { get; set; }
}

/// <summary>Query parameters for <c>GET …/challenges/widgets</c>, which pages by page and per_page.</summary>
/// <remarks>
/// <see cref="Filter"/> is a string Cloudflare applies to the list, and its grammar is not published, so this
/// library sends whatever a caller gives it and does not build one of its own.
/// </remarks>
public sealed class TurnstileWidgetListParameters : CloudflarePagedParameters
{
    /// <summary>A filter Cloudflare applies to the list; its grammar is not published.</summary>
    public string? Filter { get; set; }

    /// <summary>Field to order the list by.</summary>
    public TurnstileWidgetOrder? Order { get; set; }

    /// <summary>Sort direction.</summary>
    public SortDirection? Direction { get; set; }

    /// <inheritdoc />
    public override void Apply(QueryStringBuilder query)
    {
        base.Apply(query);
        query.AddIfNotEmpty("filter", Filter);
        if (Order.HasValue)
        {
            query.Add("order", CloudflareEnumConverter<TurnstileWidgetOrder>.ToWire(Order.Value));
        }

        if (Direction.HasValue)
        {
            query.Add("direction", QueryEnums.ToWire(Direction.Value));
        }
    }
}

/// <summary>How a Turnstile challenge presents itself to a visitor.</summary>
[JsonConverter(typeof(CloudflareEnumConverter<TurnstileWidgetMode>))]
public enum TurnstileWidgetMode
{
    /// <summary>Value Cloudflare returned that this version of the library does not know.</summary>
    Unknown = 0,

    /// <summary>A visible challenge the visitor does not have to solve.</summary>
    [CloudflareValue("non-interactive")]
    NonInteractive = 1,

    /// <summary>No visible challenge at all.</summary>
    [CloudflareValue("invisible")]
    Invisible = 2,

    /// <summary>Cloudflare decides, and may show an interactive challenge.</summary>
    [CloudflareValue("managed")]
    Managed = 3,
}

/// <summary>Where a Turnstile challenge may be solved.</summary>
[JsonConverter(typeof(CloudflareEnumConverter<TurnstileRegion>))]
public enum TurnstileRegion
{
    /// <summary>Value Cloudflare returned that this version of the library does not know.</summary>
    Unknown = 0,

    /// <summary>Everywhere but mainland China.</summary>
    [CloudflareValue("world")]
    World = 1,

    /// <summary>Mainland China, which needs a separate arrangement with Cloudflare.</summary>
    [CloudflareValue("china")]
    China = 2,
}

/// <summary>How much clearance a visitor needs, for the widget modes that ask.</summary>
[JsonConverter(typeof(CloudflareEnumConverter<TurnstileClearanceLevel>))]
public enum TurnstileClearanceLevel
{
    /// <summary>Value Cloudflare returned that this version of the library does not know.</summary>
    Unknown = 0,

    /// <summary>No clearance is asked for.</summary>
    [CloudflareValue("no_clearance")]
    NoClearance = 1,

    /// <summary>A JavaScript challenge.</summary>
    [CloudflareValue("jschallenge")]
    JsChallenge = 2,

    /// <summary>Cloudflare's managed clearance.</summary>
    [CloudflareValue("managed")]
    Managed = 3,

    /// <summary>An interactive challenge the visitor has to answer.</summary>
    [CloudflareValue("interactive")]
    Interactive = 4,
}

/// <summary>What deployed or last changed a widget.</summary>
/// <remarks>
/// Cloudflare answers <c>unknown</c> as a value of its own here, which is not the same thing as a value this
/// library does not know: the first is <see cref="NotRecorded"/> and the second is <see cref="Unknown"/>.
/// </remarks>
[JsonConverter(typeof(CloudflareEnumConverter<TurnstileDeploymentSource>))]
public enum TurnstileDeploymentSource
{
    /// <summary>Value Cloudflare returned that this version of the library does not know.</summary>
    Unknown = 0,

    /// <summary>Wrangler, Cloudflare's command-line tool.</summary>
    [CloudflareValue("wrangler")]
    Wrangler = 1,

    /// <summary>The dashboard.</summary>
    [CloudflareValue("dashboard")]
    Dashboard = 2,

    /// <summary>Spin, Cloudflare's own internal tooling.</summary>
    [CloudflareValue("spin")]
    Spin = 3,

    /// <summary>This API.</summary>
    [CloudflareValue("api")]
    Api = 4,

    /// <summary>Cloudflare's own word for "we do not know": the wire value <c>unknown</c>.</summary>
    [CloudflareValue("unknown")]
    NotRecorded = 5,
}

/// <summary>Field a widget list can be ordered by.</summary>
[JsonConverter(typeof(CloudflareEnumConverter<TurnstileWidgetOrder>))]
public enum TurnstileWidgetOrder
{
    /// <summary>Value Cloudflare returned that this version of the library does not know.</summary>
    Unknown = 0,

    /// <summary>The widget's id.</summary>
    [CloudflareValue("id")]
    Id = 1,

    /// <summary>The widget's site key.</summary>
    [CloudflareValue("sitekey")]
    Sitekey = 2,

    /// <summary>The widget's name.</summary>
    [CloudflareValue("name")]
    Name = 3,

    /// <summary>When the widget was created.</summary>
    [CloudflareValue("created_on")]
    CreatedOn = 4,

    /// <summary>When the widget last changed.</summary>
    [CloudflareValue("modified_on")]
    ModifiedOn = 5,
}
