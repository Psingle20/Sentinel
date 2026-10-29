using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Contracts.Incidents;

/// <summary>One deterministic check Runtime applied before deciding, with its result.</summary>
public sealed record DecisionCheck(string Name, bool Passed, string Detail);
