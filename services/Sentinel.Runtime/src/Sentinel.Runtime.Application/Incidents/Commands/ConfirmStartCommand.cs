using System.Text.Json.Serialization;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.Incidents.Commands;

public sealed record ConfirmStartCommand(TenantId TenantId, EnvironmentId EnvironmentId, PlanId PlanId, int PlanVersion, long FencingToken) : IncidentCommand;
