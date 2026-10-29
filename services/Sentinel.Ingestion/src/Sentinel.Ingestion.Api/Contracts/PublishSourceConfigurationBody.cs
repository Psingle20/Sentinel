using Sentinel.Ingestion.Application.ControlPlane;

namespace Sentinel.Ingestion.Api.Contracts;

/// <summary>A new version of Ingestion's source configuration, the version it replaces and who publishes it (Phase 08).</summary>
public sealed record PublishSourceConfigurationBody(SourceConfigurationDocument Document, string ExpectedVersion, string Author);
