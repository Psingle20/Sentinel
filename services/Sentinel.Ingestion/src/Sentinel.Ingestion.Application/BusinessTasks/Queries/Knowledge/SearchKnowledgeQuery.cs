using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Queries;

namespace Sentinel.Ingestion.Application.BusinessTasks.Queries.Knowledge;

public sealed record SearchKnowledgeQuery(KnowledgeQuery Query) : IQuery<KnowledgeResult>;
