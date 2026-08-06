using Licensr.Sdk.Http;

namespace Licensr.Sdk.Tests;

/// <summary>Scripted <see cref="IHttpTransport"/> for exercising <see cref="LicensrClient"/>'s retry/error-mapping logic without a real network — mirrors the fake <c>fetchImpl</c> used in <c>sdks/js</c>'s own client tests.</summary>
internal sealed class FakeHttpTransport : IHttpTransport
{
    private readonly Queue<Func<TransportRequest, TransportResponse>> _responses = new();

    internal readonly List<TransportRequest> Requests = new();

    internal FakeHttpTransport Enqueue(Func<TransportRequest, TransportResponse> respond)
    {
        _responses.Enqueue(respond);
        return this;
    }

    internal FakeHttpTransport EnqueueJson(int status, string json, IReadOnlyDictionary<string, string>? headers = null) =>
        Enqueue(_ => new TransportResponse { Status = status, Body = json, Headers = headers ?? new Dictionary<string, string>() });

    internal FakeHttpTransport EnqueueThrows(Exception exception) => Enqueue(_ => throw exception);

    public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("FakeHttpTransport ran out of scripted responses");
        }

        return Task.FromResult(_responses.Dequeue()(request));
    }
}
