using System.Text.Json.Serialization;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Investigations;

public sealed record ModelMetadata(string Provider, string Model, string? ModelVersion, string PromptVersion);
