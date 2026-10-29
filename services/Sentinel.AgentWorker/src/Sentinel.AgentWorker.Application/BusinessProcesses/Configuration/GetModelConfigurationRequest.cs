using CleanArchitecture.BuildingBlocks.Messaging;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.AgentWorker.Application.ControlPlane;

namespace Sentinel.AgentWorker.Application.BusinessProcesses.Configuration;

/// <summary>AgentWorker's current control-plane document (Phase 08 model selection).</summary>
public sealed record GetModelConfigurationRequest : IRequest<DocumentState<ModelConfigurationDocument>>;
