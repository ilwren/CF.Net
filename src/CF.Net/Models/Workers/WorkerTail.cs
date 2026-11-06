using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Models.Workers;

/// <summary>
/// A tail on a Worker: the live stream of its <c>console.log</c> output, exceptions and subrequest
/// failures.
/// </summary>
/// <remarks>
/// <para>
/// Created by <c>POST /accounts/{account_id}/workers/scripts/{script_name}/tails</c>, which takes no body and
/// answers with one tail. The <c>GET</c> on the same path answers differently - with an <em>array</em> of
/// tails, empty for a script that is not being tailed - and the items in it carry the dispatch namespace, the
/// script name and the tail's consumers rather than a url. Both shapes are one model, so which fields are
/// filled in says which call produced it.
/// </para>
/// <para>
/// <see cref="Url"/> is what a consumer connects to, and <see cref="ExpiresAt"/> is why a tail is not a
/// durable resource: it stops on its own, so a long-lived log pipeline has to be prepared to create a new
/// one. Deleting a tail stops the stream immediately.
/// </para>
/// <para>
/// Reading the events themselves is not part of this model: they arrive over the connection at
/// <see cref="Url"/>, not from the REST API.
/// </para>
/// </remarks>
public sealed class WorkerTail
{
    /// <summary>The tail's id, which deleting it addresses.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>When the tail stops on its own.</summary>
    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>The address a consumer connects to for this tail's events.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>The namespace the tail dispatches through, as a listed tail reports it.</summary>
    [JsonPropertyName("dispatch_namespace")]
    public string? DispatchNamespace { get; set; }

    /// <summary>The Worker being tailed, as a listed tail reports it.</summary>
    [JsonPropertyName("script_name")]
    public string? ScriptName { get; set; }

    /// <summary>
    /// The Workers registered to receive this tail's events, as <c>WorkerTailConsumer</c> - the same type a
    /// script's settings report its tail consumers with. A tail nothing consumes yet has none.
    /// </summary>
    [JsonPropertyName("consumers")]
    public List<WorkerTailConsumer>? Consumers { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>True once <see cref="ExpiresAt"/> has passed, so the stream is no longer running.</summary>
    [JsonIgnore]
    public bool HasExpired => ExpiresAt is { } until && until <= DateTimeOffset.UtcNow;

    /// <summary>Id and when it expires, on one line.</summary>
    /// <returns>The tail described in one line.</returns>
    public override string ToString() =>
        ExpiresAt is { } until ? $"{Id} (expires {until:u})" : $"{Id} (no expiry reported)";
}
