using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Incidents;

/// <summary>The human-written part of a postmortem, stored and versioned by Api.</summary>
public sealed record PostmortemNarrative(string Markdown, int Version, string EditedBy, DateTimeOffset EditedAt);
