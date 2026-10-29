using System.Text.Json.Serialization;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.Incidents.Commands;

public sealed record RevokeApprovalCommand(PlanId PlanId, int PlanVersion, string RevokedBy, string Reason) : IncidentCommand;
