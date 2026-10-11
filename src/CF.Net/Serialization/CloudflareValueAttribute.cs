namespace CFNet.Serialization;

/// <summary>
/// Declares the exact string Cloudflare uses for an enum member on the wire.
/// Required whenever the member name cannot be the literal API value
/// (for example <c>KvNamespace</c> for <c>"kv_namespace"</c>, or <c>Default</c> for <c>"default"</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class CloudflareValueAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    /// <param name="value">The literal value used in requests and responses.</param>
    public CloudflareValueAttribute(string value)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>The literal wire value.</summary>
    public string Value { get; }
}
