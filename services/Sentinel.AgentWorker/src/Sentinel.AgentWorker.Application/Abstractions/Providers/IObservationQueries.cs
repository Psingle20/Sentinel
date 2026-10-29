using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Sentinel.Contracts;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;

namespace Sentinel.AgentWorker.Application.Abstractions.Providers;

/// <summary>Ingestion's observation query contract, as AgentWorker sees it.</summary>
public interface IObservationQueries
{
    Task<ObservationQueryResult> QueryAsync(ObservationQuery query, CancellationToken cancellationToken);
}
