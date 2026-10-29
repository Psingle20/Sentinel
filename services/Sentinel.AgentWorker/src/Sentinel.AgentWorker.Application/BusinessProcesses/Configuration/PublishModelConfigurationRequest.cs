using CleanArchitecture.BuildingBlocks.Messaging;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.AgentWorker.Application.ControlPlane;

namespace Sentinel.AgentWorker.Application.BusinessProcesses.Configuration;

/// <summary>A new version of AgentWorker's model configuration, which the Gateway authorized (Phase 08).</summary>
public sealed record PublishModelConfigurationRequest(ModelConfigurationDocument Document, string ExpectedVersion, string Author) : IRequest<DocumentVersion>;
