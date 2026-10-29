using System.Security.Cryptography;
using System.Text.Json;
using Sentinel.AgentWorker.Domain.Executions;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Queries;
using Sentinel.AgentWorker.Application.Abstractions.Providers;

namespace Sentinel.AgentWorker.Application.Capture;

/// <summary>Observation queries through the scope's capture: recorded when live, served from the capture on replay.</summary>
public sealed class CapturingObservationQueries(IObservationQueries live, InvestigationCapture capture) : IObservationQueries
{
    public const string Kind = "observations";

    public Task<ObservationQueryResult> QueryAsync(ObservationQuery query, CancellationToken cancellationToken) =>
        capture.ThroughAsync(Kind, query, () => live.QueryAsync(query, cancellationToken));
}
