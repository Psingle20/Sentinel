using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Application.BusinessProcesses.Incidents;

/// <summary>The authoritative state beside the projection's version, which makes projection lag visible (Phase 03).</summary>
public sealed record GetIncidentStateRequest(IncidentId IncidentId) : IRequest<IncidentStateView>;
