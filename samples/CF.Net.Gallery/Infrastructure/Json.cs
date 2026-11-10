using System.Text.Json;
using CFNet.Serialization;

namespace CFNet.Gallery.Infrastructure;

/// <summary>
/// Renders anything for the inspector panes, using the same serializer settings the library uses so
/// what the gallery shows matches what Cloudflare saw.
/// </summary>
internal static class Json
{
    /// <summary>Serializes a value with the library's options.</summary>
    /// <param name="value">The value to serialize.</param>
    /// <returns>A compact JSON string, or the value's <c>ToString()</c> when it cannot be serialized.</returns>
    internal static string Serialize(object? value)
    {
        switch (value)
        {
            case null:
                return "null";
            case string text:
                return text;
        }

        try
        {
            return JsonSerializer.Serialize(value, value.GetType(), CloudflareJson.Default);
        }
        catch (Exception exception)
        {
            return $"{value.GetType().Name} could not be serialized: {exception.Message}";
        }
    }

    /// <summary>Serializes a value and indents it.</summary>
    /// <param name="value">The value to serialize.</param>
    /// <returns>Indented JSON.</returns>
    internal static string Pretty(object? value) => Indent(Serialize(value));

    /// <summary>Re-indents a compact JSON string.</summary>
    /// <param name="json">Compact JSON.</param>
    /// <returns>Indented JSON, or the input unchanged when it is not valid JSON.</returns>
    internal static string Indent(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        return CloudflareJson.PrettyPrint(json);
    }
}
