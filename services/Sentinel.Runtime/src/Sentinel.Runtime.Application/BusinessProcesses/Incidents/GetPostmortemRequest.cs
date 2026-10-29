using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Application.BusinessProcesses.Incidents;

/// <summary>The postmortem generated from an incident's stream (Phase 07); none for an unknown incident.</summary>
public sealed record GetPostmortemRequest(IncidentId IncidentId) : IRequest<Postmortem?>;
