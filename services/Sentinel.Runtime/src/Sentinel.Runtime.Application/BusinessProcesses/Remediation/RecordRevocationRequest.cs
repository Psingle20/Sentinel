using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Application.BusinessProcesses.Remediation;

/// <summary>The Gateway relayed the withdrawal of an approval before execution started (Phase 06).</summary>
public sealed record RecordRevocationRequest(IncidentId IncidentId, PlanId PlanId, int PlanVersion, string RevokedBy, string Reason) : IRequest;
