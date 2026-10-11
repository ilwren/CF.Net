using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Serialization;

/// <summary>
/// Serialises a Cloudflare enum to and from its exact wire string.
/// </summary>
/// <remarks>
/// <para>
/// The built-in <see cref="JsonStringEnumConverter"/> applies one naming policy to every member, but
/// Cloudflare mixes conventions across - and even inside - a single resource: R2 bucket create takes
/// <c>"storageClass": "Standard"</c> while the matching response field is <c>"storage_class"</c>,
/// and Worker bindings use <c>"kv_namespace"</c>. A single global policy therefore cannot be correct.
/// </para>
/// <para>
/// Reading is deliberately forgiving: an unknown value maps to the member with the numeric value
/// <c>0</c> (by convention named <c>Unknown</c>) instead of throwing, so that Cloudflare adding a new
/// enum value never breaks a consumer that is pinned to an older version of this library. Writing an
/// unknown value throws, because sending a wrong literal would silently corrupt data.
/// </para>
/// </remarks>
/// <typeparam name="TEnum">The enum type.</typeparam>
public sealed class CloudflareEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private static readonly Lazy<EnumMaps> Maps = new(CreateMaps);

    /// <inheritdoc />
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
            {
                var text = reader.GetString();
                return FromWire(text);
            }

            case JsonTokenType.Number:
            {
                // A handful of legacy endpoints emit numeric codes. Accept them when they map.
                if (reader.TryGetInt32(out var numeric) && Enum.IsDefined(typeToConvert, numeric))
                {
                    return (TEnum)(object)numeric;
                }

                return Maps.Value.Default;
            }

            case JsonTokenType.Null:
                return Maps.Value.Default;

            default:
                throw new JsonException($"Unexpected token '{reader.TokenType}' when reading enum '{typeof(TEnum).Name}'.");
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        if (!Maps.Value.ToWire.TryGetValue(value, out var text))
        {
            throw new JsonException(
                $"Enum value '{value}' of type '{typeof(TEnum).Name}' has no Cloudflare wire representation. " +
                $"Known values: {string.Join(", ", Maps.Value.ToWire.Keys)}.");
        }

        writer.WriteStringValue(text);
    }

    /// <summary>
    /// Converts a Cloudflare string into the matching enum member, returning the enum default
    /// (conventionally <c>Unknown</c>) when nothing matches.
    /// </summary>
    /// <param name="wire">The value received from the API.</param>
    public static TEnum FromWire(string? wire)
    {
        if (string.IsNullOrEmpty(wire))
        {
            return Maps.Value.Default;
        }

        if (Maps.Value.FromWire.TryGetValue(wire, out var exact))
        {
            return exact;
        }

        // Tolerate casing drift (e.g. "WNAM" vs "wnam") without weakening the exact match above.
        foreach (var pair in Maps.Value.FromWire)
        {
            if (string.Equals(pair.Key, wire, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return Maps.Value.Default;
    }

    /// <summary>Converts an enum member into the exact string Cloudflare expects.</summary>
    /// <param name="value">The enum value.</param>
    /// <exception cref="InvalidOperationException">The value has no wire representation.</exception>
    public static string ToWire(TEnum value)
    {
        if (!Maps.Value.ToWire.TryGetValue(value, out var text))
        {
            throw new InvalidOperationException(
                $"Enum value '{value}' of type '{typeof(TEnum).Name}' has no Cloudflare wire representation.");
        }

        return text;
    }

    /// <summary>Attempts to convert an enum member into its wire representation.</summary>
    /// <param name="value">The enum value.</param>
    /// <param name="wire">Receives the wire string when the conversion succeeded.</param>
    public static bool TryToWire(TEnum value, out string wire)
    {
        if (Maps.Value.ToWire.TryGetValue(value, out var text))
        {
            wire = text;
            return true;
        }

        wire = string.Empty;
        return false;
    }

    /// <summary>Every wire string this enum knows about, in declaration order.</summary>
    public static IReadOnlyCollection<string> KnownValues => Maps.Value.KnownValues;

    private sealed class EnumMaps
    {
        public required Dictionary<TEnum, string> ToWire { get; init; }
        public required Dictionary<string, TEnum> FromWire { get; init; }
        public required IReadOnlyCollection<string> KnownValues { get; init; }
        public TEnum Default => default;
    }

    private static EnumMaps CreateMaps()
    {
        var type = typeof(TEnum);
        var toWire = new Dictionary<TEnum, string>();
        var fromWire = new Dictionary<string, TEnum>(StringComparer.Ordinal);
        var known = new List<string>();

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            // Only members carrying [CloudflareValue] participate in (de)serialisation. That keeps
            // sentinel members such as `Unknown = 0` - which exist so an unrecognised value from a
            // newer Cloudflare release can be represented - from ever being written back to the API.
            var attribute = field.GetCustomAttribute<CloudflareValueAttribute>();
            if (attribute is null)
            {
                continue;
            }

            var value = (TEnum)field.GetValue(null)!;
            var wire = attribute.Value;

            toWire[value] = wire;
            fromWire[wire] = value;
            known.Add(wire);
        }

        return new EnumMaps { ToWire = toWire, FromWire = fromWire, KnownValues = known };
    }
}
