using CleanArchitecture.BuildingBlocks.Messaging;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Ingestion.Application.ControlPlane;

namespace Sentinel.Ingestion.Application.BusinessProcesses.Configuration;

/// <summary>A new version of Ingestion's source configuration, which the Gateway authorized (Phase 08).</summary>
public sealed record PublishSourceConfigurationRequest(SourceConfigurationDocument Document, string ExpectedVersion, string Author) : IRequest<DocumentVersion>;
