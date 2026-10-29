using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.BusinessProcesses.Incidents;

/// <summary>An incident's run record from Runtime.</summary>
public sealed record GetRunRecordRequest(IncidentId IncidentId) : IRequest<IncidentRunRecord?>;
