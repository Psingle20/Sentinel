using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Application.BusinessTasks.Queries.Memory;

public sealed record GetIncidentMemoryQuery(IncidentId IncidentId) : IQuery<IncidentMemoryView?>;
