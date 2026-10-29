using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.BusinessProcesses.Incidents;

/// <summary>The most recent incidents in the caller's tenants.</summary>
public sealed record ListIncidentsRequest(int Limit, IReadOnlyList<TenantId> Tenants) : IRequest<IReadOnlyList<IncidentSummary>>;
