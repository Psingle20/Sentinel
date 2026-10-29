using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Investigations;

namespace Sentinel.AgentWorker.Application.BusinessTasks.Commands.Investigations;

public sealed record InvestigateIncidentCommand(InvestigateIncident Message) : ICommand;
