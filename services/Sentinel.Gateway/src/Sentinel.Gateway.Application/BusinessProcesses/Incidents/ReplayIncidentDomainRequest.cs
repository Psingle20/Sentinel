using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.BusinessProcesses.Incidents;

/// <summary>A domain replay of an incident's recorded decisions, run by Runtime (Phase 07, level 1).</summary>
public sealed record ReplayIncidentDomainRequest(IncidentId IncidentId) : IRequest<DomainReplayResult?>;
