using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Incidents;

/// <summary>One statement of a postmortem and the history it is derived from.</summary>
public sealed record PostmortemFact(string Text, IReadOnlyList<SourceRef> Sources, DateTimeOffset? At = null);
