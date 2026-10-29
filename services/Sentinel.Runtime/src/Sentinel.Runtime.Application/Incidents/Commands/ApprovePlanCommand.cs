using System.Text.Json.Serialization;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.Incidents.Commands;

public sealed record ApprovePlanCommand(PlanId PlanId, int PlanVersion, string PlanHash, string ApproverId, string Attestation) : IncidentCommand;
