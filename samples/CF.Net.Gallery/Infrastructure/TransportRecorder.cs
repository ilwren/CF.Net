using System.Diagnostics;
using System.Text;
using CFNet.Configuration;
using CFNet.Exceptions;
using CFNet.Http;
using CFNet.Serialization;

namespace CFNet.Gallery.Infrastructure;

/// <summary>
/// One round trip: what the library asked for and what Cloudflare answered.
/// </summary>
/// <param name="Operation">Friendly name of the operation, e.g. <c>List zones</c>.</param>
/// <param name="RequestText">Rendered request: method, URL, per-request headers and body.</param>
/// <param name="ResponseText">Rendered response: status, timing, headers and body.</param>
/// <param name="ErrorText">Exception text when the call never produced a response, otherwise <c>null</c>.</param>
/// <param name="StartedUtc">When the call started.</param>
public sealed record TransportRecord(
    string Operation,
    string RequestText,
    string ResponseText,
    string? ErrorText,
    DateTimeOffset StartedUtc);

/// <summary>
/// Fan-out point for recorded calls. The gallery binds the inspector panes to whatever arrives last
/// and keeps the whole session in <see cref="History"/>.
/// </summary>
public sealed class TransportRecorder
{
    private readonly List<TransportRecord> _history = new();

    /// <summary>Raised for every completed (or failed) call.</summary>
    public event Action<TransportRecord>? Recorded;

    /// <summary>The most recent record, or <c>null</c> before the first call.</summary>
    public TransportRecord? Last { get; private set; }

    /// <summary>Every record of this session, oldest first.</summary>
    public IReadOnlyList<TransportRecord> History => _history;

    /// <summary>Publishes a record.</summary>
    /// <param name="record">The record to publish.</param>
    public void Publish(TransportRecord record)
    {
        Last = record;
        _history.Add(record);
        Recorded?.Invoke(record);
    }
}

/// <summary>
/// Decorator around <see cref="ICloudflareTransport"/> that renders each call for the inspector.
/// </summary>
/// <remarks>
/// This is the extension point the library documents: <c>CloudflareClient</c> takes any
/// <see cref="ICloudflareTransport"/>, so cross-cutting behaviour (logging, caching, metrics, and
/// here, a UI inspector) never has to touch the services.
/// <para>
/// The <c>Authorization</c>, <c>X-Auth-Key</c> and <c>X-Auth-Email</c> headers are attached by the
/// inner transport and are deliberately not rendered, so credentials never reach the screen.
/// </para>
/// </remarks>
public sealed class RecordingTransport : ICloudflareTransport, IDisposable
{
    private readonly ICloudflareTransport _inner;
    private readonly TransportRecorder _recorder;

    /// <summary>Wraps a transport.</summary>
    /// <param name="inner">The transport doing the real work.</param>
    /// <param name="recorder">Where records are published.</param>
    public RecordingTransport(ICloudflareTransport inner, TransportRecorder recorder)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
    }

    /// <inheritdoc />
    public CloudflareClientOptions Options => _inner.Options;

    /// <inheritdoc />
    public async Task<CloudflareResponse<T>> SendAsync<T>(CloudflareRequest request, CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var requestText = RenderRequest(request);

        try
        {
            var response = await _inner.SendAsync<T>(request, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            _recorder.Publish(new TransportRecord(
                Describe(request),
                requestText,
                RenderResponse(response, stopwatch.Elapsed),
                null,
                started));

            return response;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();

            _recorder.Publish(new TransportRecord(
                Describe(request),
                requestText,
                string.Empty,
                RenderException(exception, stopwatch.Elapsed),
                started));

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<CloudflareRawResponse> SendRawAsync(CloudflareRequest request, CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var requestText = RenderRequest(request);

        try
        {
            var response = await _inner.SendRawAsync(request, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            _recorder.Publish(new TransportRecord(
                Describe(request),
                requestText,
                RenderRawResponse(response, stopwatch.Elapsed),
                null,
                started));

            return response;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();

            _recorder.Publish(new TransportRecord(
                Describe(request),
                requestText,
                string.Empty,
                RenderException(exception, stopwatch.Elapsed),
                started));

            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_inner is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static string Describe(CloudflareRequest request) =>
        request.OperationName ?? $"{request.Method.Method} {request.Path}";

    private static string RenderRequest(CloudflareRequest request)
    {
        var builder = new StringBuilder();

        builder.Append(request.Method.Method).Append(' ').Append(request.Path);
        if (!request.Query.IsEmpty)
        {
            builder.Append('?').Append(request.Query);
        }

        builder.AppendLine();
        builder.Append("operation: ").AppendLine(request.OperationName ?? "(none)");

        if (request.Headers.Count > 0)
        {
            builder.AppendLine("headers:");
            foreach (var header in request.Headers)
            {
                builder.Append("  ").Append(header.Key).Append(": ").AppendLine(header.Value);
            }
        }

        if (request.ToleratedStatusCodes.Count > 0)
        {
            builder.Append("tolerated status codes: ")
                .AppendLine(string.Join(", ", request.ToleratedStatusCodes.Select(code => (int)code)));
        }

        builder.AppendLine();
        builder.AppendLine("body:");
        builder.AppendLine(RenderBody(request.Body));

        return builder.ToString().TrimEnd();
    }

    private static string RenderBody(CloudflareRequestBody? body) => body switch
    {
        null => "(no body)",
        JsonRequestBody json => Json.Indent(json.ToJson(CloudflareJson.Default)),
        TextRequestBody text => $"[{text.ContentType}]\n{text.Text}",
        BinaryRequestBody binary => $"[{binary.ContentType}] {binary.Data.Length} byte(s), not rendered",
        MultipartRequestBody multipart => RenderMultipart(multipart),
        _ => body.GetType().Name,
    };

    private static string RenderMultipart(MultipartRequestBody multipart)
    {
        var builder = new StringBuilder("[multipart/form-data]");

        foreach (var part in multipart.Parts)
        {
            builder.AppendLine();
            builder.Append("  part '").Append(part.Name).Append("' (").Append(part.ContentType).Append(')');

            if (!string.IsNullOrEmpty(part.FileName))
            {
                builder.Append(" filename='").Append(part.FileName).Append('\'');
            }

            if (part.Bytes is { } bytes)
            {
                builder.Append(' ').Append(bytes.Length).Append(" byte(s)");

                // Worker source is small and worth reading, so show it when it looks like text.
                if (bytes.Length <= 64 * 1024 && part.ContentType.Contains("javascript", StringComparison.OrdinalIgnoreCase))
                {
                    builder.AppendLine();
                    builder.Append("    ").Append(Encoding.UTF8.GetString(bytes).Replace("\n", "\n    ", StringComparison.Ordinal));
                }
            }
            else if (part.Json is not null)
            {
                builder.AppendLine();
                builder.Append("    ").Append(Json.Indent(Json.Serialize(part.Json)).Replace("\n", "\n    ", StringComparison.Ordinal));
            }
        }

        return builder.ToString();
    }

    private static string RenderResponse<T>(CloudflareResponse<T> response, TimeSpan elapsed)
    {
        var builder = new StringBuilder();

        builder.Append((int)response.StatusCode).Append(' ').Append(response.StatusCode).Append("   (").Append(elapsed.TotalMilliseconds.ToString("F0")).Append(" ms)");
        builder.AppendLine();

        if (!string.IsNullOrEmpty(response.RayId))
        {
            builder.Append("CF-RAY: ").AppendLine(response.RayId);
        }

        foreach (var header in response.ResponseHeaders)
        {
            builder.Append(header.Key).Append(": ").AppendLine(header.Value);
        }

        builder.AppendLine();
        builder.AppendLine(Json.PrettyPrint(response.RawBody));

        if (response.Errors.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("errors:");
            foreach (var error in response.Errors)
            {
                builder.Append("  ").AppendLine(error.ToString());
            }
        }

        if (response.Messages.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("messages:");
            foreach (var message in response.Messages)
            {
                builder.Append("  ").AppendLine(message);
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string RenderRawResponse(CloudflareRawResponse response, TimeSpan elapsed)
    {
        var builder = new StringBuilder();

        builder.Append((int)response.StatusCode).Append(' ').Append(response.StatusCode).Append("   (").Append(elapsed.TotalMilliseconds.ToString("F0")).Append(" ms)");
        builder.AppendLine();

        foreach (var header in response.Headers)
        {
            builder.Append(header.Key).Append(": ").AppendLine(header.Value);
        }

        builder.AppendLine();
        builder.AppendLine(response.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true
            ? Json.PrettyPrint(response.Body)
            : response.Body);

        return builder.ToString().TrimEnd();
    }

    private static string RenderException(Exception exception, TimeSpan elapsed)
    {
        var builder = new StringBuilder();

        builder.Append(exception.GetType().FullName).Append(" after ").Append(elapsed.TotalMilliseconds.ToString("F0")).AppendLine(" ms");
        builder.AppendLine();
        builder.AppendLine(exception.Message);

        if (exception is CloudflareApiException api)
        {
            builder.AppendLine();
            builder.Append("status: ").Append((int?)api.StatusCode).AppendLine();
            builder.Append("CF-RAY: ").AppendLine(api.RayId ?? "(none)");

            foreach (var error in api.Errors)
            {
                builder.Append("  ").AppendLine(error.ToString());
            }
        }

        return builder.ToString().TrimEnd();
    }
}
