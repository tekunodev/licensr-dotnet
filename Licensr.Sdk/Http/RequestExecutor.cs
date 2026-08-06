using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Licensr.Sdk.Http;

/// <summary>Extra attempts after the first, on network errors / 429 / 5xx. Defaults: 2 retries, 300ms base, 5000ms cap.</summary>
public sealed class RetryOptions
{
    public int MaxRetries { get; init; } = 2;

    public int BaseDelayMs { get; init; } = 300;

    public int MaxDelayMs { get; init; } = 5000;
}

/// <summary>
/// Performs one logical API call, retrying network errors / 429 / 5xx with
/// exponential backoff + jitter, and honoring a <c>Retry-After</c> header
/// when present. Mirrors <c>sdks/js/src/http.ts</c>'s <c>requestJson</c>.
/// </summary>
internal sealed class RequestExecutor
{
    /// <summary>
    /// SDKs already compute a stable per-machine HWID for activation — send
    /// it here for free so the server buckets rate limits per
    /// *installation* rather than per API key (see
    /// <c>backend/services/rate_limit_key.py</c>).
    /// </summary>
    internal const string DeviceIdHeader = "X-Licensr-Device-Id";

    private static readonly Random JitterRandom = new();

    private readonly IHttpTransport _transport;
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly string? _origin;
    private readonly string? _deviceId;
    private readonly int _timeoutMs;
    private readonly RetryOptions _retry;

    internal RequestExecutor(LicensrClientConfig config)
    {
        _transport = config.Transport;
        _baseUrl = config.BaseUrl.EndsWith('/') ? config.BaseUrl : config.BaseUrl + "/";
        _apiKey = config.ApiKey;
        _origin = config.Origin;
        _deviceId = config.DeviceId;
        _timeoutMs = config.TimeoutMs;
        _retry = config.Retry;
    }

    internal Task<TResponse> GetAsync<TResponse>(
        string path,
        IReadOnlyDictionary<string, string?> query,
        JsonTypeInfo<TResponse> responseTypeInfo,
        Action<int, int>? onRetry,
        CancellationToken cancellationToken) =>
        ExecuteAsync(TransportMethod.Get, BuildUrl(path, query), body: null, responseTypeInfo, onRetry, cancellationToken);

    internal Task<TResponse> PostAsync<TBody, TResponse>(
        string path,
        TBody body,
        JsonTypeInfo<TBody> bodyTypeInfo,
        JsonTypeInfo<TResponse> responseTypeInfo,
        Action<int, int>? onRetry,
        CancellationToken cancellationToken)
    {
        var bodyJson = JsonSerializer.Serialize(body, bodyTypeInfo);
        return ExecuteAsync(TransportMethod.Post, BuildUrl(path, query: null), bodyJson, responseTypeInfo, onRetry, cancellationToken);
    }

    private async Task<TResponse> ExecuteAsync<TResponse>(
        TransportMethod method,
        string url,
        string? body,
        JsonTypeInfo<TResponse> responseTypeInfo,
        Action<int, int>? onRetry,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt += 1)
        {
            var headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {_apiKey}" };
            if (body is not null)
            {
                headers["Content-Type"] = "application/json";
            }

            if (!string.IsNullOrEmpty(_deviceId))
            {
                headers[DeviceIdHeader] = _deviceId!;
            }

            if (!string.IsNullOrEmpty(_origin))
            {
                headers["Origin"] = _origin!;
            }

            TransportResponse response;
            try
            {
                response = await _transport
                    .SendAsync(
                        new TransportRequest { Method = method, Url = url, Headers = headers, Body = body, TimeoutMs = _timeoutMs },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception cause) when (cause is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                if (attempt < _retry.MaxRetries)
                {
                    var delayMs = JitterDelayMs(attempt, _retry.BaseDelayMs, _retry.MaxDelayMs);
                    onRetry?.Invoke(attempt + 1, delayMs);
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw new LicensrNetworkException($"Request to {url} failed after {attempt + 1} attempt(s)", cause);
            }

            if (response.Status is >= 200 and < 300)
            {
                if (response.Status == 204 || response.Body.Length == 0)
                {
                    return default!;
                }

                return JsonSerializer.Deserialize(response.Body, responseTypeInfo)!;
            }

            var retriable = response.Status == 429 || response.Status >= 500;
            if (retriable && attempt < _retry.MaxRetries)
            {
                var retryAfterSeconds = ParseRetryAfterSeconds(response.Headers);
                var delayMs = retryAfterSeconds is { } seconds
                    ? seconds * 1000
                    : JitterDelayMs(attempt, _retry.BaseDelayMs, _retry.MaxDelayMs);
                onRetry?.Invoke(attempt + 1, delayMs);
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var (code, message) = ParseErrorDetail(response);
            throw new LicensrApiException(response.Status, code, message, ParseRetryAfterSeconds(response.Headers));
        }
    }

    private string BuildUrl(string path, IReadOnlyDictionary<string, string?>? query)
    {
        var builder = new StringBuilder(_baseUrl).Append(path);
        if (query is null || query.Count == 0)
        {
            return builder.ToString();
        }

        builder.Append('?');
        var first = true;
        foreach (var (key, value) in query)
        {
            if (value is null)
            {
                continue;
            }

            if (!first)
            {
                builder.Append('&');
            }

            first = false;
            builder.Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
        }

        return builder.ToString();
    }

    /// <summary>Exponential backoff with full jitter (AWS-style): <c>random(0, min(max, base * 2^attempt))</c>.</summary>
    private static int JitterDelayMs(int attempt, int baseDelayMs, int maxDelayMs)
    {
        var cap = Math.Min(maxDelayMs, baseDelayMs * Math.Pow(2, attempt));
        lock (JitterRandom)
        {
            return (int)(JitterRandom.NextDouble() * cap);
        }
    }

    private static int? ParseRetryAfterSeconds(IReadOnlyDictionary<string, string> headers)
    {
        if (!headers.TryGetValue("Retry-After", out var header))
        {
            return null;
        }

        return int.TryParse(header, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0
            ? seconds
            : null;
    }

    private static (string Code, string Message) ParseErrorDetail(TransportResponse response)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize(response.Body, LicensrJsonContext.Default.ErrorEnvelopeWire);
            var detail = envelope?.Detail;

            // `{"detail": {"error": "...", "message": "..."}}` — every
            // hand-written HTTPException in the license/billing routers.
            if (detail is { ValueKind: JsonValueKind.Object } obj && obj.TryGetProperty("error", out _))
            {
                var parsed = obj.Deserialize(LicensrJsonContext.Default.ErrorDetailWire);
                return (parsed?.Error ?? "unknown_error", parsed?.Message ?? $"HTTP {response.Status}");
            }

            // `{"detail": [{"msg": "...", "loc": [...]}, ...]}` — FastAPI's
            // own 422 shape for request validation errors.
            if (detail is { ValueKind: JsonValueKind.Array } array && array.GetArrayLength() > 0)
            {
                var items = array.Deserialize(LicensrJsonContext.Default.ListValidationErrorItemWire);
                return ("validation_error", items?.FirstOrDefault()?.Msg ?? "Request validation failed");
            }
        }
        catch (JsonException)
        {
            // Falls through to the generic message below.
        }

        return ("unknown_error", $"HTTP {response.Status}");
    }
}
