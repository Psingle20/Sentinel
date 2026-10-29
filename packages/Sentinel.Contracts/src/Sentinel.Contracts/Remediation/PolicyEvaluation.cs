using System.Text.Json.Serialization;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Remediation;

public sealed record PolicyEvaluation(PolicyDecision Decision, string PolicyVersion, IReadOnlyList<string> RuleTrace, DateTimeOffset EvaluatedAt);
