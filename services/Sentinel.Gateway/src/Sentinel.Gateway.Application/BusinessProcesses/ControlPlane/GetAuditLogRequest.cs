using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Abstractions.Persistence;

namespace Sentinel.Gateway.Application.BusinessProcesses.ControlPlane;

/// <summary>The most recent audited control-plane changes.</summary>
public sealed record GetAuditLogRequest(int Limit) : IRequest<IReadOnlyList<AuditEntry>>;
