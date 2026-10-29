using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Contracts.Incidents;

/// <summary>
/// One incident's complete record for evaluation: what a run produced, exported by Runtime
/// (Phase 01A) and read by the Phase 07 evaluation harness. Schema 2 adds the version manifest and
/// the incident-memory record; schema-1 records stay readable with both absent.
/// </summary>
public sealed record IncidentRunRecord(int SchemaVersion, IncidentDetail Incident, DateTimeOffset ExportedAt, VersionManifest? Versions = null, IncidentMemoryView? Memory = null)
{
    public const int CurrentSchemaVersion = 2;
}
