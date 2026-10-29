using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Runtime.Application.BusinessTasks.Commands.Remediation;

public sealed record RecordRevocationCommand(IncidentId IncidentId, PlanId PlanId, int PlanVersion, string RevokedBy, string Reason, string? Attestation) : ICommand<RevocationResponse>;
