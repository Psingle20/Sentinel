using Sentinel.Contracts.Incidents;

namespace Sentinel.Gateway.Application.Incidents;

/// <summary>One page of a long list (Phase 08 API principles: large timelines and evidence sets are paginated).</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, int Total, string? Next);
