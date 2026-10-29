using CleanArchitecture.BuildingBlocks.Messaging;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Ingestion.Application.ControlPlane;

namespace Sentinel.Ingestion.Application.BusinessProcesses.Configuration;

/// <summary>Ingestion's current control-plane document (Phase 08 provider configuration).</summary>
public sealed record GetSourceConfigurationRequest : IRequest<DocumentState<SourceConfigurationDocument>>;
