using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Application.BusinessProcesses.Incidents;

/// <summary>A level-1 domain replay of an incident (Phase 07), read-only and safe at any time; none for an unknown incident.</summary>
public sealed record ReplayDomainRequest(IncidentId IncidentId) : IRequest<DomainReplayResult?>;
