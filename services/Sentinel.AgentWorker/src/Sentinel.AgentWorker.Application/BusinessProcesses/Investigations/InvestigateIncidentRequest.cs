using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Investigations;

namespace Sentinel.AgentWorker.Application.BusinessProcesses.Investigations;

/// <summary>Runtime's request to investigate an incident in one role, as it arrived on the stream.</summary>
public sealed record InvestigateIncidentRequest(InvestigateIncident Message) : IRequest;
