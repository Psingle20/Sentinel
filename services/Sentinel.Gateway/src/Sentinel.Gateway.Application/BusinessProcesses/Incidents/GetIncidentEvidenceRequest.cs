using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Gateway.Application.Incidents;

namespace Sentinel.Gateway.Application.BusinessProcesses.Incidents;

/// <summary>A page of an incident's accepted evidence, from an offset.</summary>
public sealed record GetIncidentEvidenceRequest(IncidentId IncidentId, int? Offset, int? Limit) : IRequest<Page<EvidenceView>?>;
