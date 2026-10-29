using System.Text.Json.Serialization;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Remediation;

/// <summary>A condition the target must meet immediately before an action runs, checked through <c>Inspect</c>.</summary>
public sealed record PlanPrecondition(ServiceId ServiceId, string Condition, string Expected);
