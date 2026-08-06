using System.Net.Http;
using System.Text;

namespace Licensr.Sdk.Http;

/// <summary>
/// Default <see cref="IHttpTransport"/>, backed by a single shared
/// <see cref="System.Net.Http.HttpClient"/> (the documented best practice —
/// creating one per request exhausts sockets under load). Supported by
/// IL2CPP on every Unity platform except WebGL as of Unity 2021.2+ with the
/// .NET Standard 2.1 API compatibility level.
/// </summary>
public sealed class HttpClientTransport : IHttpTransport
{
    private static readonly System.Net.Http.HttpClient SharedClient = new();

    public async Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(
            request.Method == TransportMethod.Get ? System.Net.Http.HttpMethod.Get : System.Net.Http.HttpMethod.Post,
            request.Url);

        if (request.Body is not null)
        {
            message.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");
        }

        foreach (var (key, value) in request.Headers)
        {
            // Content-Type belongs on message.Content, not message.Headers —
            // HttpRequestMessage throws InvalidOperationException otherwise.
            if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            message.Headers.TryAddWithoutValidation(key, value);
        }

        using var timeoutCts = request.TimeoutMs > 0 ? new CancellationTokenSource(request.TimeoutMs) : null;
        using var linkedCts = timeoutCts is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        using var response = await SharedClient
            .SendAsync(message, (linkedCts ?? timeoutCts)?.Token ?? cancellationToken)
            .ConfigureAwait(false);

        // HttpContent.ReadAsStringAsync(CancellationToken) is net5.0+ only;
        // the parameterless overload exists on every TFM this SDK targets.
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers)
        {
            headers[header.Key] = string.Join(", ", header.Value);
        }

        foreach (var header in response.Content.Headers)
        {
            headers[header.Key] = string.Join(", ", header.Value);
        }

        return new TransportResponse
        {
            Status = (int)response.StatusCode,
            Body = body,
            Headers = headers,
        };
    }
}
