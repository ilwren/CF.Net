using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CFNet.Configuration;
using CFNet.Exceptions;
using CFNet.Models.Common;
using CFNet.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CFNet.Http;

/// <summary>
/// Default <see cref="ICloudflareTransport"/> implementation: builds the URI, applies authentication,
/// retries transient failures, and parses the Cloudflare envelope.
/// </summary>
public sealed class CloudflareTransport : ICloudflareTransport, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly bool _ownsHttpClient;

    /// <summary>
    /// Test seam: replaces <see cref="Task.Delay(TimeSpan, CancellationToken)"/> between retries so
    /// back-off can be asserted without actually waiting.
    /// </summary>
    internal Func<TimeSpan, CancellationToken, Task>? DelayOverride { get; set; }

    /// <summary>Creates a transport that owns a dedicated <see cref="HttpClient"/>.</summary>
    /// <param name="options">Client options.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    public CloudflareTransport(CloudflareClientOptions options, ILoggerFactory? loggerFactory = null)
        : this(CreateHttpClient(options), options, loggerFactory, ownsHttpClient: true)
    {
    }

    /// <summary>Creates a transport around an externally supplied <see cref="HttpClient"/>.</summary>
    /// <param name="httpClient">The client to send through. Its <c>BaseAddress</c> is ignored.</param>
    /// <param name="options">Client options.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    public CloudflareTransport(HttpClient httpClient, CloudflareClientOptions options, ILoggerFactory? loggerFactory = null)
        : this(httpClient, options, loggerFactory, ownsHttpClient: false)
    {
    }

    private CloudflareTransport(HttpClient httpClient, CloudflareClientOptions options, ILoggerFactory? loggerFactory, bool ownsHttpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<CloudflareTransport>();
        _serializerOptions = options.JsonSerializerOptions ?? CloudflareJson.Default;
        _ownsHttpClient = ownsHttpClient;

        // Applied lazily so an HttpClient injected by IHttpClientFactory keeps its own configuration.
        if (options.Timeout > TimeSpan.Zero && ownsHttpClient)
        {
            _httpClient.Timeout = options.Timeout;
        }
    }

    /// <inheritdoc />
    public CloudflareClientOptions Options { get; }

    /// <inheritdoc />
    public async Task<CloudflareResponse<T>> SendAsync<T>(CloudflareRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var raw = await ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        return ParseEnvelope<T>(raw, request);
    }

    /// <inheritdoc />
    public async Task<CloudflareRawResponse> SendRawAsync(CloudflareRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var raw = await ExecuteAsync(request, cancellationToken).ConfigureAwait(false);

        if (!raw.IsSuccessStatusCode && !request.ToleratedStatusCodes.Contains(raw.StatusCode))
        {
            ThrowApiException(raw, request);
        }

        return raw;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    // ------------------------------------------------------------------
    // Execution
    // ------------------------------------------------------------------

    private async Task<CloudflareRawResponse> ExecuteAsync(CloudflareRequest request, CancellationToken cancellationToken)
    {
        Options.Validate();

        var uri = CloudflareUrl.Build(Options.BaseAddress, request.Path, request.Query.ToString());
        var maxAttempts = Math.Max(0, Options.MaxRetries) + 1;
        Exception? lastException = null;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            HttpResponseMessage? response = null;
            try
            {
                using var message = CreateMessage(request, uri);

                _logger.LogDebug("CF.Net {Method} {Uri} (attempt {Attempt}/{Attempts})", request.Method.Method, uri, attempt + 1, maxAttempts);

                response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);

                Options.InspectResponse?.Invoke(response);

                if (IsRetryable(response) && attempt < maxAttempts - 1)
                {
                    var delay = ComputeDelay(response, attempt);
                    _logger.LogWarning(
                        "CF.Net received {StatusCode} from {Uri}; retrying in {Delay}ms (attempt {Attempt}/{Attempts})",
                        (int)response.StatusCode, uri, (int)delay.TotalMilliseconds, attempt + 1, maxAttempts);

                    response.Dispose();
                    await DelayAsync(delay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                return await ReadRawAsync(response, uri).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                response?.Dispose();
                throw;
            }
            catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                // HttpClient surfaces its own Timeout as a TaskCanceledException.
                response?.Dispose();
                lastException = exception;
            }
            catch (HttpRequestException exception)
            {
                response?.Dispose();
                lastException = exception;
            }

            if (attempt < maxAttempts - 1)
            {
                var delay = ComputeDelay(response: null, attempt);
                _logger.LogWarning(
                    lastException,
                    "CF.Net request to {Uri} failed; retrying in {Delay}ms (attempt {Attempt}/{Attempts})",
                    uri, (int)delay.TotalMilliseconds, attempt + 1, maxAttempts);

                await DelayAsync(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new CloudflareTransportException(
            $"Cloudflare request to '{uri}' failed after {maxAttempts} attempt(s). See the inner exception for the underlying cause.",
            uri,
            lastException)
        {
            IsTimeout = lastException is TaskCanceledException,
            Attempts = maxAttempts,
        };
    }

    private HttpRequestMessage CreateMessage(CloudflareRequest request, Uri uri)
    {
        var message = new HttpRequestMessage(request.Method, uri);

        // A fresh HttpContent per attempt: content is disposed by the send, so it cannot be reused.
        if (request.Body is not null)
        {
            message.Content = request.Body.CreateContent(_serializerOptions);
        }

        ApplyAuthentication(message);
        ApplyUserAgent(message);

        foreach (var header in request.Headers)
        {
            if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                message.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        Options.ConfigureRequest?.Invoke(message);
        return message;
    }

    private void ApplyAuthentication(HttpRequestMessage message)
    {
        switch (Options.AuthenticationMode)
        {
            case CloudflareAuthenticationMode.ApiToken:
                message.Headers.TryAddWithoutValidation(
                    CloudflareDefaults.Headers.Authorization,
                    "Bearer " + Options.ApiToken!.Trim());
                break;

            case CloudflareAuthenticationMode.GlobalApiKey:
                message.Headers.TryAddWithoutValidation(CloudflareDefaults.Headers.AuthKey, Options.GlobalApiKey!.Trim());
                message.Headers.TryAddWithoutValidation(CloudflareDefaults.Headers.AuthEmail, Options.ApiEmail!.Trim());
                break;

            default:
                throw new CloudflareConfigurationException(
                    "No Cloudflare credentials are configured. Provide an ApiToken, or both GlobalApiKey and ApiEmail.");
        }
    }

    private void ApplyUserAgent(HttpRequestMessage message)
    {
        if (string.IsNullOrWhiteSpace(Options.UserAgent))
        {
            return;
        }

        if (message.Headers.UserAgent.Count == 0)
        {
            message.Headers.TryAddWithoutValidation("User-Agent", Options.UserAgent);
        }
    }

    private static async Task<CloudflareRawResponse> ReadRawAsync(HttpResponseMessage response, Uri uri)
    {
        using (response)
        {
            var content = response.Content is null
                ? Array.Empty<byte>()
                : await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            CopyHeaders(response.Headers, headers);
            if (response.Content is not null)
            {
                CopyHeaders(response.Content.Headers, headers);
            }

            return new CloudflareRawResponse
            {
                StatusCode = response.StatusCode,
                RequestUri = uri,
                Content = content,
                ContentType = response.Content?.Headers.ContentType?.ToString(),
                RayId = FirstHeader(response.Headers, CloudflareDefaults.Headers.CfRay),
                Headers = headers,
            };
        }
    }

    private static void CopyHeaders(HttpHeaders source, Dictionary<string, string> target)
    {
        foreach (var header in source)
        {
            target[header.Key] = string.Join(", ", header.Value);
        }
    }

    private static string? FirstHeader(HttpHeaders headers, string name)
    {
        return headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
    }

    private static bool IsRetryable(HttpResponseMessage response)
    {
        var code = (int)response.StatusCode;

        // 429 Too Many Requests, plus anything server side. 408 Request Timeout is also transient.
        return code == 429 || code == 408 || code >= 500;
    }

    private TimeSpan ComputeDelay(HttpResponseMessage? response, int attempt)
    {
        if (response?.Headers.RetryAfter is { } retryAfter)
        {
            if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
            {
                return Clamp(delta);
            }

            if (retryAfter.Date is { } date)
            {
                var until = date - DateTimeOffset.UtcNow;
                return Clamp(until > TimeSpan.Zero ? until : TimeSpan.Zero);
            }
        }

        var backoff = Options.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt);
        if (Options.UseRetryJitter && backoff > 0)
        {
            backoff += backoff * 0.25 * Random.Shared.NextDouble();
        }

        return Clamp(TimeSpan.FromMilliseconds(backoff));
    }

    private TimeSpan Clamp(TimeSpan delay)
    {
        if (Options.MaxRetryDelay > TimeSpan.Zero && delay > Options.MaxRetryDelay)
        {
            return Options.MaxRetryDelay;
        }

        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    private Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (DelayOverride is { } custom)
        {
            return custom(delay, cancellationToken);
        }

        return delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, cancellationToken);
    }

    // ------------------------------------------------------------------
    // Parsing
    // ------------------------------------------------------------------

    private CloudflareResponse<T> ParseEnvelope<T>(CloudflareRawResponse raw, CloudflareRequest request)
    {
        var tolerated = request.ToleratedStatusCodes.Contains(raw.StatusCode);
        var body = raw.Body;

        Envelope<T>? envelope = null;
        JsonException? parseFailure = null;

        if (body.Length > 0 && LooksLikeJson(body))
        {
            try
            {
                envelope = JsonSerializer.Deserialize<Envelope<T>>(body, _serializerOptions);
            }
            catch (JsonException exception)
            {
                parseFailure = exception;
            }
        }

        var success = envelope?.Success ?? raw.IsSuccessStatusCode;
        IReadOnlyList<CloudflareErrorInfo> errors =
            envelope?.Errors is { } errorList ? errorList : Array.Empty<CloudflareErrorInfo>();
        var messages = NormaliseMessages(envelope?.Messages);
        var headers = raw.Headers;

        var response = new CloudflareResponse<T>
        {
            Success = success,
            // A null-conditional operator cannot be used here: forming T? from a member that is
            // already T? makes the compiler try to nullable-annotate an unconstrained type parameter.
            Result = envelope is null ? default : envelope.Result,
            Errors = errors,
            Messages = messages,
            ResultInfo = envelope?.ResultInfo,
            StatusCode = raw.StatusCode,
            RequestUri = raw.RequestUri,
            RawBody = body,
            RayId = raw.RayId,
            ResponseHeaders = headers,
        };

        if (tolerated)
        {
            return response;
        }

        var failed = !success || !raw.IsSuccessStatusCode;
        if (failed && Options.ThrowOnApiError)
        {
            if (!raw.IsSuccessStatusCode)
            {
                ThrowApiException(raw, request, errors, messages);
            }

            throw BuildApiException(
                DescribeFailure(request, raw.StatusCode, errors, messages),
                raw.StatusCode,
                raw.RequestUri,
                errors,
                messages,
                body,
                raw.RayId,
                parseFailure);
        }

        if (parseFailure is not null)
        {
            _logger.LogWarning(
                parseFailure,
                "CF.Net could not parse the response body of {Uri} into the expected shape; the raw body is available on the response.",
                raw.RequestUri);
        }

        return response;
    }

    private void ThrowApiException(CloudflareRawResponse raw, CloudflareRequest request)
    {
        IReadOnlyList<CloudflareErrorInfo> errors = Array.Empty<CloudflareErrorInfo>();
        IReadOnlyList<string> messages = Array.Empty<string>();

        var body = raw.Body;
        if (body.Length > 0 && LooksLikeJson(body))
        {
            try
            {
                var envelope = JsonSerializer.Deserialize<Envelope<JsonElement>>(body, _serializerOptions);
                if (envelope?.Errors is { } errorList)
                {
                    errors = errorList;
                }

                messages = NormaliseMessages(envelope?.Messages);
            }
            catch (JsonException)
            {
                // A non-envelope error body (an HTML 502 page, for example) is handled below.
            }
        }

        ThrowApiException(raw, request, errors, messages);
    }

    private void ThrowApiException(
        CloudflareRawResponse raw,
        CloudflareRequest request,
        IReadOnlyList<CloudflareErrorInfo> errors,
        IReadOnlyList<string> messages)
    {
        throw BuildApiException(
            DescribeFailure(request, raw.StatusCode, errors, messages),
            raw.StatusCode,
            raw.RequestUri,
            errors,
            messages,
            raw.Body,
            raw.RayId,
            innerException: null);
    }

    private static CloudflareApiException BuildApiException(
        string message,
        HttpStatusCode? statusCode,
        Uri? requestUri,
        IReadOnlyList<CloudflareErrorInfo> errors,
        IReadOnlyList<string> messages,
        string? responseBody,
        string? rayId,
        Exception? innerException)
    {
        return new CloudflareApiException(message, statusCode, requestUri, errors, messages, responseBody, rayId, innerException);
    }

    private static string DescribeFailure(
        CloudflareRequest request,
        HttpStatusCode statusCode,
        IReadOnlyList<CloudflareErrorInfo> errors,
        IReadOnlyList<string> messages)
    {
        var builder = new StringBuilder();
        builder.Append("Cloudflare API call '").Append(request.OperationName ?? request.Method.Method).Append(' ').Append(request.Path);
        builder.Append("' failed with ").Append((int)statusCode).Append(' ').Append(statusCode).Append('.');

        foreach (var error in errors)
        {
            builder.Append(" [").Append(error.Code).Append("] ").Append(error.Message);
        }

        foreach (var message in messages)
        {
            builder.Append(' ').Append(message);
        }

        if (errors.Count == 0 && messages.Count == 0)
        {
            builder.Append(" The response did not contain a structured error description.");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Cloudflare sends <c>messages</c> either as <c>["text"]</c> or as
    /// <c>[{"code":1,"message":"text"}]</c>, depending on the endpoint. Both are flattened to strings.
    /// </summary>
    private static IReadOnlyList<string> NormaliseMessages(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Array } array)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            switch (item.ValueKind)
            {
                case JsonValueKind.String:
                {
                    var text = item.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        result.Add(text);
                    }

                    break;
                }

                case JsonValueKind.Object when item.TryGetProperty("message", out var messageProperty):
                {
                    var text = messageProperty.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        result.Add(text);
                    }

                    break;
                }

                case JsonValueKind.Object:
                    result.Add(item.GetRawText());
                    break;

                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    result.Add(item.GetRawText());
                    break;
            }
        }

        return result;
    }

    private static bool LooksLikeJson(string body)
    {
        foreach (var character in body)
        {
            if (char.IsWhiteSpace(character))
            {
                continue;
            }

            return character is '{' or '[';
        }

        return false;
    }

    private static HttpClient CreateHttpClient(CloudflareClientOptions options)
    {
        var handler = new SocketsHttpHandler
        {
            // Cloudflare keeps connections warm for a long time; pool aggressively but recycle hourly.
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 20,
            AutomaticDecompression = DecompressionMethods.All,
        };

        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = options.Timeout > TimeSpan.Zero ? options.Timeout : System.Threading.Timeout.InfiniteTimeSpan,
        };
    }

    /// <summary>
    /// Mirrors the outer Cloudflare envelope. Kept private so consumers bind against
    /// <see cref="CloudflareResponse{T}"/> instead.
    /// </summary>
    private sealed class Envelope<T>
    {
        public bool? Success { get; set; }

        public T? Result { get; set; }

        public List<CloudflareErrorInfo>? Errors { get; set; }

        /// <summary>Polymorphic on the wire; see <see cref="NormaliseMessages"/>.</summary>
        public JsonElement? Messages { get; set; }

        public CloudflareResultInfo? ResultInfo { get; set; }
    }
}
