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

internal sealed class RecordingWorkflows : IIncidentWorkflows
{
    public ConcurrentQueue<(IncidentId Incident, int Cycle)> Started { get; } = new();

    public ConcurrentQueue<(IncidentId Incident, IncidentSignalKind Kind)> Signals { get; } = new();

    public Task EnsureStartedAsync(IncidentSnapshot incident, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incident);

        if (!Started.Contains((incident.IncidentId, incident.Cycle)))
        {
            Started.Enqueue((incident.IncidentId, incident.Cycle));
        }

        return Task.CompletedTask;
    }

    public Task SignalAsync(IncidentSnapshot incident, IncidentSignal signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(signal);

        Signals.Enqueue((incident.IncidentId, signal.Kind));
        return Task.CompletedTask;
    }
}
