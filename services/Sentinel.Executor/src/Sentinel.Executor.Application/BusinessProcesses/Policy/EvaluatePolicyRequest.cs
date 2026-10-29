using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Executor.Application.BusinessProcesses.Policy;

/// <summary>Runtime asks for the advisory plan-time policy verdict on a plan (ADR-006).</summary>
public sealed record EvaluatePolicyRequest(PolicyEvaluationRequest Evaluation) : IRequest<PolicyEvaluation>;
