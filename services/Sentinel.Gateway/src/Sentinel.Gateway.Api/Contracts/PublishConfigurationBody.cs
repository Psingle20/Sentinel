using System.Text.Json;

namespace Sentinel.Gateway.Api.Contracts;

/// <summary>A new version of a configuration area's document and the version it replaces (Phase 08).</summary>
public sealed record PublishConfigurationBody(JsonElement Document, string ExpectedVersion);
