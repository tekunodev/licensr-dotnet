namespace Licensr.Sdk.Http;

/// <summary>Deliberately not <see cref="System.Net.Http.HttpMethod"/> — avoids forcing every <see cref="IHttpTransport"/> implementation to reference <c>System.Net.Http</c>.</summary>
public enum TransportMethod
{
    Get,
    Post,
}

/// <summary>One HTTP request as <see cref="IHttpTransport"/> sees it — deliberately not <see cref="System.Net.Http.HttpRequestMessage"/>, so a transport doesn't have to depend on <c>System.Net.Http</c> (e.g. a Unity WebGL transport backed by <c>UnityWebRequest</c>).</summary>
public sealed class TransportRequest
{
    public required TransportMethod Method { get; init; }

    public required string Url { get; init; }

    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    /// <summary>UTF-8 JSON body, or <c>null</c> for a body-less request.</summary>
    public string? Body { get; init; }

    /// <summary>0 means no timeout.</summary>
    public int TimeoutMs { get; init; }
}

public sealed class TransportResponse
{
    public required int Status { get; init; }

    public required string Body { get; init; }

    /// <summary>Case-insensitive, e.g. for reading <c>Retry-After</c>.</summary>
    public required IReadOnlyDictionary<string, string> Headers { get; init; }
}

/// <summary>
/// Pluggable HTTP backend, mirroring <c>sdks/cpp/include/licensr/http_transport.h</c>'s
/// <c>HttpTransport</c>. The default (<see cref="HttpClientTransport"/>) uses
/// <see cref="System.Net.Http.HttpClient"/>, which IL2CPP supports on every
/// Unity platform except WebGL — bring your own (e.g. a
/// <c>UnityWebRequest</c>-backed transport) via
/// <see cref="LicensrClientConfig.Transport"/> there.
/// </summary>
public interface IHttpTransport
{
    Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken);
}
