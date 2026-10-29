using Sentinel.Ingestion.Application;

namespace Sentinel.Ingestion.Host;

/// <summary>A new version of Ingestion's source configuration, the version it replaces and who publishes it (Phase 08).</summary>
public sealed record PublishSourceConfigurationRequest(SourceConfigurationDocument Document, string ExpectedVersion, string Author);
