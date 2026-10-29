using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Application.BusinessProcesses.Memory;

/// <summary>An operator places or lifts a legal hold on an incident's memory; none when it is not remembered or was deleted.</summary>
public sealed record SetLegalHoldRequest(IncidentId IncidentId, bool Hold, string Actor) : IRequest<IncidentMemoryView?>;
