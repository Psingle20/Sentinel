using System.Text.Json.Serialization;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Remediation;

/// <summary>Synchronous start confirmation, Executor to Runtime, immediately before any side effect (ADR-006).</summary>
public sealed record RemediationStartRequest(TenantId TenantId, EnvironmentId EnvironmentId, IncidentId IncidentId, PlanId PlanId, int PlanVersion, long FencingToken);
