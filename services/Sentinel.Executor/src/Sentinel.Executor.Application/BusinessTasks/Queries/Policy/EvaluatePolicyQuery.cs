using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Executor.Application.BusinessTasks.Queries.Policy;

public sealed record EvaluatePolicyQuery(PolicyEvaluationRequest Evaluation) : IQuery<PolicyEvaluation>;
