using System.Globalization;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Domain.Capabilities;
using Sentinel.Executor.Domain.Executions;
using Sentinel.Executor.Domain.Policy;
using DomainVerdict = Sentinel.Executor.Domain.Policy.PolicyVerdict;
using Sentinel.Executor.Application.Abstractions.Persistence;
using Sentinel.Executor.Application.Options;

namespace Sentinel.Executor.Application.Services;

/// <summary>
/// The only policy engine (ADR-006): advisory at plan time for Runtime, authoritative at execution
/// time. It evaluates the versioned policy document with the operator controls; a plan's verdict is
/// the most restrictive verdict of its actions, and every evaluation records its rule trace.
/// </summary>
public sealed class PolicyService(IPolicyStore policies, IControlStore controls, IOptions<ExecutorOptions> options, TimeProvider time)
{
    public async Task<PolicyEvaluation> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        PolicyDocument policy = await policies.CurrentAsync(cancellationToken);
        DateTimeOffset now = time.GetUtcNow();
        if (!RemediationPlanHash.Matches(request.Plan))
        {
            return new PolicyEvaluation(PolicyDecision.Deny, policy.Version, ["plan.hash: the hash does not match the plan content"], now);
        }

        ExecutorControls current = await controls.GetAsync(cancellationToken);
        if (options.Value.EmergencyStop)
        {
            current = current with { EmergencyStop = true };
        }

        int blastRadius = request.Plan.BlastRadius?.Count ?? request.Plan.Actions.Select(action => action.TargetServiceId).Distinct().Count();
        PolicyOutcome[] outcomes = [.. request.Plan.Actions.Select(action => PolicyEngine.Evaluate(
            policy,
            new PolicyInput(
                request.TenantId.Value,
                request.EnvironmentId.Value,
                ActionKind(action),
                action.TargetServiceId.Value,
                request.Plan.Risk.ToString().ToLowerInvariant(),
                request.Confidence,
                request.Severity?.ToString().ToLowerInvariant(),
                blastRadius,
                now),
            current))];

        DomainVerdict verdict = outcomes.Length == 0
            ? DomainVerdict.Deny
            : outcomes.Select(outcome => outcome.Verdict).Aggregate(PolicyEngine.MostRestrictive);
        return new PolicyEvaluation(
            verdict switch
            {
                DomainVerdict.Allow => PolicyDecision.Allow,
                DomainVerdict.ApprovalRequired => PolicyDecision.ApprovalRequired,
                DomainVerdict.SuggestionOnly => PolicyDecision.SuggestionOnly,
                _ => PolicyDecision.Deny
            },
            policy.Version,
            outcomes.Length == 0 ? ["plan: no actions"] : [.. outcomes.SelectMany(outcome => outcome.RuleTrace)],
            now);
    }

    public static string ActionKind(RemediationAction action) => action switch
    {
        RollbackAction => "rollback",
        PauseBatchAction => "pause-batch",
        QuarantineMessageAction => "quarantine-message",
        RestoreRouteAction => "restore-route",
        _ => action.GetType().Name
    };
}
