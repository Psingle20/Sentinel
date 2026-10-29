using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Abstractions.Persistence;

namespace Sentinel.Gateway.Application.BusinessTasks.Queries.ControlPlane;

public sealed record GetAuditLogQuery(int Limit) : IQuery<IReadOnlyList<AuditEntry>>;
