using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Contracts.Incidents;

public sealed record TimelineEntry(long StreamVersion, DateTimeOffset OccurredAt, string Kind, string Summary);
