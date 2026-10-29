using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Contracts.Incidents;

/// <summary>An edge such as <c>supports</c>, <c>contradicts</c>, <c>blames</c>, <c>depends-on</c> or <c>affects</c>.</summary>
public sealed record CausalEdge(string From, string To, string Relation, double? Weight = null);
