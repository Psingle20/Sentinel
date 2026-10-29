using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.AgentWorker.Application.BusinessProcesses.Replays;

/// <summary>A replay of an execution from its captured inputs (Phase 07, levels 2 and 3); nothing when the execution is unknown.</summary>
public sealed record ReplayInvestigationRequest(AgentExecutionId ExecutionId, InvestigationReplayRequest Replay) : IRequest<InvestigationReplayResult?>;
