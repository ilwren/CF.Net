using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CFNet.Serialization;

namespace CFNet.Http;

/// <summary>
/// Describes the payload of a <see cref="CloudflareRequest"/> in a transport neutral way, so unit
/// tests can assert on what a service intends to send without touching <see cref="HttpContent"/>.
/// </summary>
public abstract class CloudflareRequestBody
{
    /// <summary>Creates an <see cref="HttpContent"/> instance for this body. Called once per attempt.</summary>
    /// <param name="serializerOptions">The JSON options to use for JSON serialised parts.</param>
    public abstract HttpContent CreateContent(JsonSerializerOptions serializerOptions);
}

/// <summary>A JSON document body (<c>Content-Type: application/json</c>).</summary>
public sealed class JsonRequestBody : CloudflareRequestBody
{
    /// <summary>Creates a JSON body.</summary>
    /// <param name="value">The object graph to serialise.</param>
    public JsonRequestBody(object? value)
    {
        Value = value;
    }

    /// <summary>The object graph that will be serialised.</summary>
    public object? Value { get; }

    /// <summary>Renders the body exactly as it goes on the wire.</summary>
    public string ToJson(JsonSerializerOptions options) => JsonSerializer.Serialize(Value, Value?.GetType() ?? typeof(object), options);

    /// <inheritdoc />
    public override HttpContent CreateContent(JsonSerializerOptions serializerOptions)
    {
        var json = ToJson(serializerOptions);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }
}

/// <summary>
/// A body written verbatim. Used for KV values, where Cloudflare expects the raw bytes rather than
/// a JSON envelope.
/// </summary>
public sealed class TextRequestBody : CloudflareRequestBody
{
    /// <summary>Creates a text body.</summary>
    /// <param name="text">The payload.</param>
    /// <param name="contentType">Media type to send.</param>
    public TextRequestBody(string text, string contentType = "text/plain")
    {
        Text = text ?? string.Empty;
        ContentType = contentType;
    }

    /// <summary>The payload.</summary>
    public string Text { get; }

    /// <summary>Media type sent in <c>Content-Type</c>.</summary>
    public string ContentType { get; }

    /// <inheritdoc />
    public override HttpContent CreateContent(JsonSerializerOptions serializerOptions)
    {
        var content = new StringContent(Text, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue(ContentType);
        return content;
    }
}

/// <summary>A body of raw bytes.</summary>
public sealed class BinaryRequestBody : CloudflareRequestBody
{
    /// <summary>Creates a binary body.</summary>
    /// <param name="data">The payload.</param>
    /// <param name="contentType">Media type to send.</param>
    public BinaryRequestBody(byte[] data, string contentType = "application/octet-stream")
    {
        Data = data ?? Array.Empty<byte>();
        ContentType = contentType;
    }

    /// <summary>The payload.</summary>
    public byte[] Data { get; }

    /// <summary>Media type sent in <c>Content-Type</c>.</summary>
    public string ContentType { get; }

    /// <inheritdoc />
    public override HttpContent CreateContent(JsonSerializerOptions serializerOptions)
    {
        var content = new ByteArrayContent(Data);
        content.Headers.ContentType = new MediaTypeHeaderValue(ContentType);
        return content;
    }
}

/// <summary>A <c>multipart/form-data</c> body, used by the Workers script upload endpoint.</summary>
public sealed class MultipartRequestBody : CloudflareRequestBody
{
    /// <summary>Creates an empty multipart body.</summary>
    public MultipartRequestBody()
    {
        Parts = new List<MultipartPart>();
    }

    /// <summary>Creates a multipart body from existing parts.</summary>
    /// <param name="parts">The parts, in order.</param>
    public MultipartRequestBody(IEnumerable<MultipartPart> parts)
    {
        Parts = new List<MultipartPart>(parts ?? Enumerable.Empty<MultipartPart>());
    }

    /// <summary>The parts in the order they will be written.</summary>
    public IList<MultipartPart> Parts { get; }

    /// <summary>Appends a part holding a serialised object, sent as JSON.</summary>
    /// <param name="name">Form field name.</param>
    /// <param name="value">Object to serialise.</param>
    public MultipartRequestBody AddJson(string name, object? value)
    {
        Parts.Add(new MultipartPart(name) { Json = value });
        return this;
    }

    /// <summary>Appends a file-like part.</summary>
    /// <param name="name">Form field name; for Workers this is the module name.</param>
    /// <param name="content">Payload bytes.</param>
    /// <param name="contentType">Media type, e.g. <c>application/javascript+module</c>.</param>
    /// <param name="fileName">File name reported in <c>Content-Disposition</c>.</param>
    public MultipartRequestBody AddFile(string name, byte[] content, string contentType, string? fileName = null)
    {
        Parts.Add(new MultipartPart(name)
        {
            Bytes = content,
            ContentType = contentType,
            FileName = fileName ?? name,
        });
        return this;
    }

    /// <inheritdoc />
    public override HttpContent CreateContent(JsonSerializerOptions serializerOptions)
    {
        var form = new MultipartFormDataContent("CFNet----" + Guid.NewGuid().ToString("N"));
        foreach (var part in Parts)
        {
            var content = part.CreateContent(serializerOptions);

            // The 3 argument overload throws on a null file name, so a plain form field
            // (the "metadata" part) has to go through the 2 argument overload.
            if (part.FileName is null)
            {
                form.Add(content, part.Name);
            }
            else
            {
                form.Add(content, part.Name, part.FileName);
            }
        }

        return form;
    }
}

/// <summary>One part of a <see cref="MultipartRequestBody"/>.</summary>
public sealed class MultipartPart
{
    /// <summary>Creates a part.</summary>
    /// <param name="name">Form field name.</param>
    public MultipartPart(string name)
    {
        Name = name;
    }

    /// <summary>Form field name.</summary>
    public string Name { get; }

    /// <summary>File name; when <c>null</c> the part is a plain form field.</summary>
    public string? FileName { get; set; }

    /// <summary>Object to serialise as JSON, mutually exclusive with <see cref="Bytes"/>.</summary>
    public object? Json { get; set; }

    /// <summary>Raw payload, mutually exclusive with <see cref="Json"/>.</summary>
    public byte[]? Bytes { get; set; }

    /// <summary>Media type of the part.</summary>
    public string ContentType { get; set; } = "application/octet-stream";

    /// <summary>Materialises the part as <see cref="HttpContent"/>.</summary>
    /// <param name="serializerOptions">JSON options used when <see cref="Json"/> is set.</param>
    public HttpContent CreateContent(JsonSerializerOptions serializerOptions)
    {
        if (Json is not null)
        {
            var json = JsonSerializer.Serialize(Json, Json.GetType(), serializerOptions);
            return new StringContent(json, Encoding.UTF8, "application/json");
        }

        var content = new ByteArrayContent(Bytes ?? Array.Empty<byte>());
        content.Headers.ContentType = new MediaTypeHeaderValue(ContentType);
        return content;
    }
}
