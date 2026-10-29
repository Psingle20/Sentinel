using Sentinel.AgentWorker.Application.ControlPlane;

namespace Sentinel.AgentWorker.Api.Contracts;

/// <summary>A new version of AgentWorker's model configuration, the version it replaces and who publishes it (Phase 08).</summary>
public sealed record PublishModelConfigurationBody(ModelConfigurationDocument Document, string ExpectedVersion, string Author);
