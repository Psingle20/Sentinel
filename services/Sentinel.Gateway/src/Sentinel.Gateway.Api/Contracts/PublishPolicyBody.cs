using System.Text.Json;

namespace Sentinel.Gateway.Api.Contracts;

/// <summary>A new policy document and the version the operator started from; Executor validates it.</summary>
public sealed record PublishPolicyBody(JsonElement Document, string ExpectedVersion);