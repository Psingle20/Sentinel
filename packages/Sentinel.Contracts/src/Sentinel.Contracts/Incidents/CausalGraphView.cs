using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Contracts.Incidents;

/// <summary>The incident's causal graph (Phase 04), derived from the stream: changes, symptoms, services, evidence and hypotheses.</summary>
public sealed record CausalGraphView(IReadOnlyList<CausalNode> Nodes, IReadOnlyList<CausalEdge> Edges);
