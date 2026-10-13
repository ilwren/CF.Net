using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Serialization;

/// <summary>
/// Reads a blank JSON string as <see langword="null"/> for nullable value types, so a field Cloudflare
/// empties out does not take the whole response with it.
/// </summary>
/// <remarks>
/// <para>
/// Cloudflare spells "no value" inconsistently across the v4 API. Most of the time an absent date or number
/// arrives as JSON <c>null</c>, which System.Text.Json already handles; in places it arrives as <c>""</c>,
/// which it does not - the parser throws, and because CF.Net parses a whole envelope at a time, one blank
/// field discards an entire zone object or an entire page of them. Treating a blank string as null for
/// <c>DateTimeOffset?</c>, <c>int?</c>, <c>decimal?</c>, <c>bool?</c> and the rest of the nullable value
/// types costs nothing and keeps the rest of the payload.
/// </para>
/// <para>
/// <see cref="JsonElement"/> is deliberately excluded: it is the escape hatch used by the fields this
/// library intentionally leaves untyped, and a blank string there is data rather than an absent value.
/// Writing is unaffected - a null still writes as <c>null</c>, and is still omitted entirely where
/// <see cref="JsonIgnoreCondition.WhenWritingNull"/> applies.
/// </para>
/// </remarks>
public sealed class BlankStringAsNullConverterFactory : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) =>
        Nullable.GetUnderlyingType(typeToConvert) is { } underlying && underlying != typeof(JsonElement);

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(
            typeof(BlankStringAsNullConverter<>).MakeGenericType(Nullable.GetUnderlyingType(typeToConvert)!))!;
}

/// <summary>
/// The per-type half of <see cref="BlankStringAsNullConverterFactory"/>: delegates everything except a blank
/// string to the converter System.Text.Json would have used anyway.
/// </summary>
/// <typeparam name="T">The underlying value type.</typeparam>
internal sealed class BlankStringAsNullConverter<T> : JsonConverter<T?>
    where T : struct
{
    /// <inheritdoc />
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && string.IsNullOrWhiteSpace(reader.GetString()))
        {
            return null;
        }

        // Asking for T rather than T? is what keeps this from recursing back into itself.
        var inner = (JsonConverter<T>)options.GetConverter(typeof(T));
        return inner.Read(ref reader, typeof(T), options);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
    {
        if (!value.HasValue)
        {
            writer.WriteNullValue();
            return;
        }

        var inner = (JsonConverter<T>)options.GetConverter(typeof(T));
        inner.Write(writer, value.Value, options);
    }
}
