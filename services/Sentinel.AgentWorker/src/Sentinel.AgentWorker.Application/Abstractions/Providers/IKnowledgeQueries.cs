using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sentinel.Contracts;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;

namespace Sentinel.AgentWorker.Application.Abstractions.Providers;

/// <summary>Ingestion's knowledge search, as AgentWorker sees it (ADR-007).</summary>
public interface IKnowledgeQueries
{
    Task<KnowledgeResult> SearchAsync(KnowledgeQuery query, CancellationToken cancellationToken);
}
