using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Incidents;

/// <summary>Where a postmortem fact came from: an event of the stream, evidence, an observation, an investigation or an execution.</summary>
public sealed record SourceRef(string Kind, string Ref);
