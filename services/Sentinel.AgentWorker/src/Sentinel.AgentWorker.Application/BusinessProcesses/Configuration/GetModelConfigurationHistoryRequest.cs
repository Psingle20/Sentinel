using CleanArchitecture.BuildingBlocks.Messaging;
using CleanArchitecture.BuildingBlocks.Configuration;

namespace Sentinel.AgentWorker.Application.BusinessProcesses.Configuration;

/// <summary>Every published version of AgentWorker's control-plane document.</summary>
public sealed record GetModelConfigurationHistoryRequest : IRequest<IReadOnlyList<DocumentVersion>>;
