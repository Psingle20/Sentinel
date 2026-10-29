using System.Security.Cryptography;
using System.Text.Json;
using Sentinel.AgentWorker.Domain.Executions;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Queries;
using Sentinel.AgentWorker.Application.Abstractions.Providers;

namespace Sentinel.AgentWorker.Application.Capture;

/// <summary>Knowledge searches through the scope's capture.</summary>
public sealed class CapturingKnowledgeQueries(IKnowledgeQueries live, InvestigationCapture capture) : IKnowledgeQueries
{
    public const string Kind = "knowledge";

    public Task<KnowledgeResult> SearchAsync(KnowledgeQuery query, CancellationToken cancellationToken) =>
        capture.ThroughAsync(Kind, query, () => live.SearchAsync(query, cancellationToken));
}
