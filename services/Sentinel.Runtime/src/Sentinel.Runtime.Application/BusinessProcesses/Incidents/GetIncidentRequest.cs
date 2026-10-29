using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Application.BusinessProcesses.Incidents;

/// <summary>The Gateway reads one incident from the read model.</summary>
public sealed record GetIncidentRequest(IncidentId IncidentId) : IRequest<IncidentDetail>;
