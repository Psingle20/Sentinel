using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Application.BusinessProcesses.Memory;

/// <summary>One incident's memory record (Phase 07); none when it was never remembered.</summary>
public sealed record GetIncidentMemoryRequest(IncidentId IncidentId) : IRequest<IncidentMemoryView?>;
