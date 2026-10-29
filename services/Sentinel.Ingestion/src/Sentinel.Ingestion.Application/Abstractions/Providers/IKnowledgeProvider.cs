using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Domain.Observations;

namespace Sentinel.Ingestion.Application.Abstractions.Providers;

/// <summary>A knowledge provider, such as a runbook repository (ADR-014). Owned by Ingestion like every provider.</summary>
public interface IKnowledgeProvider
{
    string Name { get; }

    Task<IReadOnlyList<KnowledgeEntry>> LoadAsync(CancellationToken cancellationToken);
}
