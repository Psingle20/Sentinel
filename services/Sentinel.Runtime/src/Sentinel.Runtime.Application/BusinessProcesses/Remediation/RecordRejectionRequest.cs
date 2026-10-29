using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Application.BusinessProcesses.Remediation;

/// <summary>The Gateway relayed an approver's rejection of a plan version.</summary>
public sealed record RecordRejectionRequest(IncidentId IncidentId, PlanId PlanId, int PlanVersion, string RejectedBy, string Reason) : IRequest;
