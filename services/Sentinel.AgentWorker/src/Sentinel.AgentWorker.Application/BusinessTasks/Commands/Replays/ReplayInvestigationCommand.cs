using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.AgentWorker.Application.BusinessTasks.Commands.Replays;

public sealed record ReplayInvestigationCommand(AgentExecutionId ExecutionId, InvestigationReplayRequest Replay) : ICommand<InvestigationReplayResult?>;
