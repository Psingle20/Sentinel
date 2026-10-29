using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.BusinessProcesses.Incidents;

/// <summary>One incident's detail from Runtime; nothing when Runtime has none.</summary>
public sealed record GetIncidentRequest(IncidentId IncidentId) : IRequest<IncidentDetail?>;
