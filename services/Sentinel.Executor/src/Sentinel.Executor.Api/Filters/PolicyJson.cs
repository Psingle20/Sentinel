using System.Text.Json;
using Sentinel.Contracts.Messaging;
using Sentinel.Executor.Domain.Policy;

namespace Sentinel.Executor.Api.Filters;

/// <summary>
/// The policy document as the Gateway hashed it: the JSON it relayed, re-read as an element. Executor
/// binds the typed <see cref="PolicyDocument"/> and serializes it back with the same options, so a
/// document that round-trips unchanged hashes the same on both sides.
/// </summary>
internal static class PolicyJson
{
    public static JsonElement ToElement(PolicyDocument document)
    {
        using var parsed = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(document, SentinelJson.Options));
        return parsed.RootElement.Clone();
    }
}
