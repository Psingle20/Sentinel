using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.BusinessProcesses.Postmortems;

/// <summary>An incident's postmortem: Runtime's generated facts with the responders' narrative.</summary>
public sealed record GetPostmortemRequest(IncidentId IncidentId) : IRequest<Postmortem?>;
