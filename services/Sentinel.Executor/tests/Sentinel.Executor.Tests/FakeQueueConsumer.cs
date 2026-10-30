using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Sentinel.Executor.Tests;

/// <summary>
/// The reference queue consumer's status and control endpoints: one message is redelivered until a quarantine moves
/// it, and every control request's body and token are recorded.
/// </summary>
internal sealed class FakeQueueConsumer : HttpMessageHandler
{
    private readonly List<(string Path, string? Token, JsonNode? Body)> _controls = [];
    private bool _quarantined;

    public int Receives { get; init; } = 12;

    public IReadOnlyList<(string Path, string? Token, JsonNode? Body)> Controls => [.. _controls];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Get && path == "/api/queue")
        {
            object[] redelivered = _quarantined ? [] : [new { id = "ord-poison-1", receives = Receives, state = "waiting" }];
            return Json(new { queue = "orders", waiting = 3, inFlight = 1, quarantined = _quarantined ? 1 : 0, oldestMessageAgeSeconds = _quarantined ? 0.4 : 95.0, maxReceiveCount = _quarantined ? 1 : Receives, redelivered });
        }

        string? token = request.Headers.TryGetValues("X-Reference-Control-Token", out IEnumerable<string>? values) ? values.Single() : null;
        JsonNode? body = request.Content is null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
        _controls.Add((path, token, body));
        switch (path)
        {
            case "/_reference/queue/quarantine":
                _quarantined = true;
                return Json(new { messages = new[] { "ord-poison-1" } });
            case "/_reference/queue/release":
                _quarantined = false;
                return Json(new { messages = new[] { "ord-poison-1" } });
            default:
                return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
}
