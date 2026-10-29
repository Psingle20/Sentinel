using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Application.BusinessProcesses.Incidents;

/// <summary>The schema-2 run record of an incident (Phase 07); none for an unknown incident.</summary>
public sealed record GetRunRecordRequest(IncidentId IncidentId, string? Dataset) : IRequest<IncidentRunRecord?>;
