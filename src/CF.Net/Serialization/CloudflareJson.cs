using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Serialization;

/// <summary>
/// The single source of truth for JSON (de)serialisation inside CF.Net.
/// </summary>
/// <remarks>
/// Cloudflare's v4 API is overwhelmingly <c>snake_case</c>, but not exclusively: the R2 create-bucket
/// request body uses <c>locationHint</c> / <c>storageClass</c>, and a few nested objects use
/// <c>camelCase</c>. The default options therefore apply a snake_case naming policy as the fallback
/// and every model that deviates carries an explicit <see cref="JsonPropertyNameAttribute"/>.
/// </remarks>
public static class CloudflareJson
{
    /// <summary>Shared, immutable options used unless overridden through the client options.</summary>
    public static JsonSerializerOptions Default { get; } = CreateOptions();

    /// <summary>
    /// Creates a fresh set of options identical to <see cref="Default"/>. Use this when you need to
    /// tweak a copy without affecting the shared instance.
    /// </summary>
    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            // snake_case for properties, but stay forgiving about what Cloudflare sends back.
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = null,
            PropertyNameCaseInsensitive = true,
            // A missing field must stay missing - sending "foo": null makes several endpoints 400.
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            // Some endpoints return numbers as strings ("file_size": "12345").
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = false,
            // Never explode on a resource Cloudflare extended after this library was released.
            // [JsonExtensionData] dictionaries on the models capture the overflow.
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        return options;
    }

    /// <summary>
    /// Pretty prints any value using the library options. Used by the gallery and by test failure
    /// messages so that a payload can be read by a human.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value to serialise.</param>
    public static string ToIndentedJson<T>(T? value)
    {
        if (value is null)
        {
            return "null";
        }

        var options = new JsonSerializerOptions(Default) { WriteIndented = true };
        return JsonSerializer.Serialize(value, options);
    }

    /// <summary>
    /// Re-formats an arbitrary JSON string with indentation, or returns the input unchanged when it
    /// is not valid JSON. Handy for echoing raw Cloudflare responses into a UI.
    /// </summary>
    /// <param name="json">Raw JSON text.</param>
    public static string PrettyPrint(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var options = new JsonSerializerOptions { WriteIndented = true, Encoder = Default.Encoder };
            return JsonSerializer.Serialize(document.RootElement, options);
        }
        catch (JsonException)
        {
            return json;
        }
    }
}
