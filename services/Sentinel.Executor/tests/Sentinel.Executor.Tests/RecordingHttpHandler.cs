using System.Net;

namespace Sentinel.Executor.Tests;

/// <summary>Records where each request went and which credentials it carried, and answers every one with 503.</summary>
internal sealed class RecordingHttpHandler : HttpMessageHandler
{
    private readonly List<(string Host, string Path, string? Bearer, string? Control)> _requests = [];

    public IReadOnlyList<(string Host, string Path, string? Bearer, string? Control)> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? control = request.Headers.TryGetValues("X-Reference-Control-Token", out IEnumerable<string>? values) ? values.Single() : null;
        lock (_requests)
        {
            _requests.Add((request.RequestUri!.Host, request.RequestUri.AbsolutePath, request.Headers.Authorization?.Parameter, control));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
