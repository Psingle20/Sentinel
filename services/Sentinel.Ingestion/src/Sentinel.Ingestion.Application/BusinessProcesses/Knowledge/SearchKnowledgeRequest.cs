using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Queries;

namespace Sentinel.Ingestion.Application.BusinessProcesses.Knowledge;

/// <summary>A runbook and documentation search for AgentWorker's knowledge tool.</summary>
public sealed record SearchKnowledgeRequest(KnowledgeQuery Query) : IRequest<KnowledgeResult>;
