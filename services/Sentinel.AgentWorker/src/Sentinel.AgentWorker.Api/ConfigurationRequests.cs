using Sentinel.AgentWorker.Application.Models;

namespace Sentinel.AgentWorker.Api;

/// <summary>A new version of AgentWorker's model configuration, the version it replaces and who publishes it (Phase 08).</summary>
public sealed record PublishModelConfigurationRequest(ModelConfigurationDocument Document, string ExpectedVersion, string Author);
