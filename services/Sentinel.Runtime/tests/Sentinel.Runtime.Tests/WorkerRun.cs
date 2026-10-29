using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Domain.Incidents;
using Sentinel.Runtime.Infrastructure.Workflows;
using Shouldly;
using Temporalio.Client;
using Temporalio.Worker;
using Temporalio.Workflows;
using ContractDirection = Sentinel.Contracts.Investigations.EvidenceDirection;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.BusinessProcesses.Remediation;
using Sentinel.Runtime.Application.Detection;
using Sentinel.Runtime.Application.Options;
using Sentinel.Runtime.Domain.Incidents.Events;
using Sentinel.Runtime.Application.Projections;

namespace Sentinel.Runtime.Tests;

/// <summary>A Temporal worker running in the background until disposed, as a crashed or stopped Runtime would stop polling.</summary>
internal sealed class WorkerRun(TemporalWorker worker) : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private Task? _running;

    public WorkerRun Start()
    {
        _running = worker.ExecuteAsync(_stop.Token);
        return this;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        try
        {
            await _running!;
        }
        catch (OperationCanceledException)
        {
            // A stopped worker ends by cancellation.
        }

        worker.Dispose();
        _stop.Dispose();
    }
}
