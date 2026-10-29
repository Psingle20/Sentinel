using System.Text.Json.Serialization;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Remediation;

/// <summary>Synchronous plan-time policy evaluation, Runtime to Executor (ADR-006).</summary>
/// <summary>
/// What policy decides on (Phase 06): the plan and the incident facts it is conditioned on. Never
/// free text from a model. Confidence and severity come from Runtime's decision record.
/// </summary>
public sealed record PolicyEvaluationRequest(
    TenantId TenantId,
    EnvironmentId EnvironmentId,
    IncidentId IncidentId,
    RemediationPlan Plan,
    double? Confidence = null,
    Severity? Severity = null);
