using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Ingestion.Application.BusinessProcesses.Sources;

namespace Sentinel.Ingestion.Application.BusinessProcesses.Sources;

/// <summary>Every source's health and watermark, for operators.</summary>
public sealed record ListSourcesRequest : IRequest<IReadOnlyList<SourceSummary>>;
