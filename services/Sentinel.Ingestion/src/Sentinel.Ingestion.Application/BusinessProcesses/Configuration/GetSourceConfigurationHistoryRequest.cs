using CleanArchitecture.BuildingBlocks.Messaging;
using CleanArchitecture.BuildingBlocks.Configuration;

namespace Sentinel.Ingestion.Application.BusinessProcesses.Configuration;

/// <summary>Every published version of Ingestion's control-plane document.</summary>
public sealed record GetSourceConfigurationHistoryRequest : IRequest<IReadOnlyList<DocumentVersion>>;
