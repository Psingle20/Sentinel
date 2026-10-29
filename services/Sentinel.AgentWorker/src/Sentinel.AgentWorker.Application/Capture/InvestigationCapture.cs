using System.Security.Cryptography;
using System.Text.Json;
using Sentinel.AgentWorker.Domain.Executions;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Queries;

namespace Sentinel.AgentWorker.Application.Capture;

/// <summary>
/// The provider-response capture of one investigation scope (Phase 07). While recording, every
/// response the tools receive is kept in order. While replaying, responses are served only from a
/// captured set, matched by the canonical hash of the request, and the live provider is never
/// called. Level-3 reevaluation can also override the model profile and prompt version.
/// </summary>
public sealed class InvestigationCapture
{
    private readonly Dictionary<string, Queue<CapturedResponse>> _replay = new(StringComparer.Ordinal);
    private List<CapturedResponse>? _recording;

    public bool Replaying { get; private set; }

    /// <summary>Requests the replay needed but the execution never captured.</summary>
    public int Misses { get; private set; }

    /// <summary>Level 3: the model profile to route every role to instead of its configured route.</summary>
    public string? ModelProfile { get; set; }

    /// <summary>Level 3: the prompt version to investigate with instead of the configured one.</summary>
    public string? PromptVersion { get; set; }

    public static string Hash<T>(T request) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, SentinelJson.Options)));

    public void BeginRecording() => _recording = [];

    /// <summary>What the last recording captured, kept after it ended so it can be stored with the execution.</summary>
    public IReadOnlyList<CapturedResponse> LastRecording { get; private set; } = [];

    public IReadOnlyList<CapturedResponse> EndRecording()
    {
        LastRecording = _recording ?? [];
        _recording = null;
        return LastRecording;
    }

    public void ServeFrom(IReadOnlyList<CapturedResponse> captured)
    {
        ArgumentNullException.ThrowIfNull(captured);

        Replaying = true;
        foreach (CapturedResponse response in captured.OrderBy(response => response.Sequence))
        {
            if (!_replay.TryGetValue(response.RequestHash, out Queue<CapturedResponse>? queue))
            {
                queue = new Queue<CapturedResponse>();
                _replay[response.RequestHash] = queue;
            }

            queue.Enqueue(response);
        }
    }

    public async Task<TResult> ThroughAsync<TRequest, TResult>(string kind, TRequest request, Func<Task<TResult>> live)
    {
        ArgumentNullException.ThrowIfNull(live);

        string hash = Hash(request);
        if (Replaying)
        {
            if (!_replay.TryGetValue(hash, out Queue<CapturedResponse>? queue) || queue.Count == 0)
            {
                Misses++;
                throw new NotCapturedException(kind, hash);
            }

            // The same request asked again gets its next captured answer, and the last one after that.
            CapturedResponse served = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
            return JsonSerializer.Deserialize<TResult>(served.Response, SentinelJson.Options)!;
        }

        TResult result = await live();
        _recording?.Add(new CapturedResponse(
            _recording.Count,
            kind,
            hash,
            JsonSerializer.Serialize(request, SentinelJson.Options),
            JsonSerializer.Serialize(result, SentinelJson.Options)));
        return result;
    }
}
