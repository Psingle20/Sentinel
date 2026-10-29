using System.Collections.Concurrent;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Application.Memory;
using Sentinel.Runtime.Domain.Correlation;
using Sentinel.Runtime.Domain.Incidents;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Application.Abstractions.Persistence;
using Sentinel.Runtime.Application.Abstractions.Providers;
using Sentinel.Runtime.Application.Incidents.InProcess;
using Sentinel.Runtime.Application.Options;

namespace Sentinel.Runtime.Tests;

internal sealed class StubPolicy : IPolicyEvaluator
{
    public int Calls { get; private set; }

    public PolicyEvaluationRequest? Last { get; private set; }

    public Task<PolicyEvaluation> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Calls++;
        Last = request;
        return Task.FromResult(request.Plan.Actions[0] is PauseBatchAction
            ? new PolicyEvaluation(PolicyDecision.SuggestionOnly, "policy-test", ["pause-batch is suggestion-only"], IncidentScenario.Start)
            : new PolicyEvaluation(PolicyDecision.ApprovalRequired, "policy-test", ["rollback needs approval"], IncidentScenario.Start));
    }
}
