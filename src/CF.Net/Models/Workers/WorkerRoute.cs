using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Serialization;

namespace CFNet.Models.Workers;

/// <summary>
/// A Workers route: a URL pattern in one zone, mapped to a Worker, as
/// <c>/zones/{zone_id}/workers/routes</c> reports it.
/// </summary>
/// <remarks>
/// <para>
/// A route is how a Worker is put in front of an existing proxied hostname rather than given one of its own -
/// which is what a custom domain (<see cref="WorkerDomain"/>) does instead. The pattern is matched against the
/// whole request URL, the most specific pattern wins when several match, and the hostname has to have a
/// proxied DNS record in the zone for traffic to arrive at all.
/// </para>
/// <para>
/// <see cref="Script"/> being absent is a feature rather than a gap: a route with no script <em>negates</em>
/// every less specific pattern that would otherwise have matched, so it is how one path is taken back out of a
/// Worker. See <see cref="BypassesWorkers"/>.
/// </para>
/// </remarks>
public sealed class WorkerRoute
{
    /// <summary>Immutable id of the route.</summary>
    public string? Id { get; set; }

    /// <summary>
    /// The URL pattern matched against incoming requests, for example <c>*.example.com/images/*</c>. Its
    /// grammar is Cloudflare's own and not a regex: <c>*</c> is the only operator, a pattern may begin with a
    /// scheme and a hostname wildcard, may end with a path wildcard, and may not carry query parameters.
    /// </summary>
    public string? Pattern { get; set; }

    /// <summary>
    /// Name of the Worker that runs when the pattern matches. Null for a route that runs nothing, which is a
    /// deliberate kind of route rather than an unfinished one.
    /// </summary>
    public string? Script { get; set; }

    /// <summary>
    /// Whether a request that runs into the Worker's limits fails open - reaching the origin instead of
    /// erroring. Cloudflare answers this on a route and no published schema mentions it, so the kind it writes
    /// it in is not published either and the tolerant reader takes a boolean, a number or a word.
    /// </summary>
    [JsonConverter(typeof(TolerantBooleanConverter))]
    public bool? RequestLimitFailOpen { get; set; }

    /// <summary>
    /// Whether this route takes a path back out of Workers: with no script of its own, it wins over the less
    /// specific patterns that do have one and lets the request through to the origin.
    /// </summary>
    [JsonIgnore]
    public bool BypassesWorkers => string.IsNullOrEmpty(Script);

    /// <summary>Members Cloudflare added after this library was released.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The pattern and where it points, on one line.</summary>
    /// <returns>The route described in one line.</returns>
    public override string ToString() =>
        Script is { Length: > 0 } script ? $"{Pattern} -> {script}" : $"{Pattern} -> no script";
}

/// <summary>
/// Body of <c>POST /zones/{zone_id}/workers/routes</c> and of the <c>PUT</c> that changes one: a pattern, and
/// optionally the Worker it runs.
/// </summary>
/// <remarks>
/// <para>
/// Create and update carry the same two fields, and the update is a whole-route replacement rather than a
/// patch - a <see cref="Script"/> left null on a <c>PUT</c> removes the script from a route that had one,
/// which turns it into a bypass rather than leaving it alone.
/// </para>
/// <para>
/// Only the parts of the pattern grammar that Cloudflare documents as outright invalid are refused here. That
/// a pattern has to belong to the zone it is created in, and that a wildcard matches any characters rather
/// than only subdomain labels, are Cloudflare's calls to make: it refuses them and says why, and guessing at
/// that grammar locally would only disagree with it eventually.
/// </para>
/// </remarks>
public sealed class WorkerRouteRequest
{
    /// <summary>The URL pattern to match, for example <c>app.example.com/api/*</c>.</summary>
    public string? Pattern { get; set; }

    /// <summary>
    /// Name of the Worker to run when the pattern matches. Leave it null for a route that runs nothing, which
    /// negates the less specific patterns around it.
    /// </summary>
    public string? Script { get; set; }

    /// <summary>Builds a route that runs a Worker on a pattern.</summary>
    /// <param name="pattern">The URL pattern to match.</param>
    /// <param name="scriptName">The Worker to run, or null for a route that runs nothing.</param>
    /// <returns>A request ready to send.</returns>
    public static WorkerRouteRequest For(string pattern, string? scriptName = null) =>
        new() { Pattern = pattern, Script = scriptName };

    /// <summary>
    /// Builds a route that runs nothing, which takes a path back out of whatever less specific route would
    /// otherwise have caught it.
    /// </summary>
    /// <param name="pattern">The URL pattern to match.</param>
    /// <returns>A request ready to send.</returns>
    public static WorkerRouteRequest Bypassing(string pattern) => new() { Pattern = pattern };

    /// <summary>Refuses a pattern Cloudflare documents as invalid, naming everything wrong with it at once.</summary>
    /// <exception cref="Exceptions.CloudflareConfigurationException">
    /// Thrown when the pattern is missing or is one of the shapes the published rules rule out; its
    /// <c>Problems</c> lists every reason.
    /// </exception>
    public void EnsureValid()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(Pattern))
        {
            problems.Add("pattern is required: a route is its pattern, and there is nothing to match without one.");
        }
        else
        {
            // Documented as invalid outright, so these are worth refusing here rather than spending a call.
            if (Pattern.Contains('?') || Pattern.Contains('#'))
            {
                problems.Add(
                    $"pattern '{Pattern}' carries a query or a fragment: route patterns may not contain query " +
                    "parameters, and matching considers the whole request URL, so a pattern that has to catch " +
                    "URLs with a query string ends in a wildcard instead.");
            }

            if (Pattern.Any(char.IsWhiteSpace))
            {
                problems.Add($"pattern '{Pattern}' has whitespace in it, which no request URL can match.");
            }
        }

        if (Script is { } script && string.IsNullOrWhiteSpace(script))
        {
            problems.Add(
                "script, when given, names a Worker: an empty one is neither a name nor the absence of a " +
                "script, which is what a route that runs nothing is written as.");
        }

        if (problems.Count > 0)
        {
            throw new Exceptions.CloudflareConfigurationException(problems);
        }
    }
}
