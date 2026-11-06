using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Serialization;

/// <summary>
/// Reads a flag Cloudflare wrote as a boolean, as a number or as a word, and writes it as a boolean.
/// </summary>
/// <remarks>
/// <para>
/// Cloudflare mostly answers <c>true</c> and <c>false</c>, and this library mostly models a flag as
/// <see cref="bool"/>. Not always: <c>GET …/ai/finetunes/public</c> answered <c>"public": 1</c> for every
/// published fine-tune, where the same field's published shape says <c>boolean</c> - one endpoint, two kinds,
/// and a model typed as <see cref="bool"/> throws on the first of them. This converter reads <c>true</c>,
/// <c>false</c>, <c>1</c>, <c>0</c>, <c>"true"</c>, <c>"false"</c>, <c>"1"</c>, <c>"0"</c>, a blank string and
/// <c>null</c>, and answers <see langword="null"/> for the last two: a blank is nothing said, not a flag set to
/// false. Any other number than 0 is true, because that is what a number used as a flag means.
/// </para>
/// <para>
/// It is applied per property with <see cref="JsonConverterAttribute"/> rather than installed in
/// <see cref="CloudflareJson"/>, on the same reasoning that keeps
/// <see cref="BlankStringAsNullConverterFactory"/> away from booleans: a flag this library declares as a flag
/// should still fail loudly, with the transport naming the field, when Cloudflare sends something stranger
/// than a number or a word for one. Where an endpoint is known to send both, the property says so.
/// </para>
/// </remarks>
public sealed class TolerantBooleanConverter : JsonConverter<bool?>
{
    /// <inheritdoc />
    public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True:
                return true;
            case JsonTokenType.False:
                return false;
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.Number:
                // An integer flag is the common case; a fractional one is still a flag, and 0 is the only false.
                return reader.TryGetInt64(out var number) ? number != 0 : reader.GetDouble() != 0d;
            case JsonTokenType.String:
                return ReadWord(reader.GetString());
            default:
                throw new JsonException(
                    $"The JSON value of type '{reader.TokenType}' could not be read as a boolean flag.");
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (value is { } flag)
        {
            writer.WriteBooleanValue(flag);
        }
        else
        {
            writer.WriteNullValue();
        }
    }

    private static bool? ReadWord(string? word)
    {
        var text = word?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (bool.TryParse(text, out var flag))
        {
            return flag;
        }

        return text switch
        {
            "1" => true,
            "0" => false,
            "yes" => true,
            "no" => false,
            _ => throw new JsonException(
                $"The JSON string '{text}' could not be read as a boolean flag, in " +
                CultureInfo.InvariantCulture.Name + " or otherwise."),
        };
    }
}
