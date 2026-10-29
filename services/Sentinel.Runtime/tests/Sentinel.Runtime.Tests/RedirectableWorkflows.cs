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

/// <summary>The production <see cref="TemporalIncidentWorkflows"/>, re-created when the harness moves to another server.</summary>
internal sealed class RedirectableWorkflows(Func<string> address, string taskQueue, IOptions<RuntimeOptions> runtime) : IIncidentWorkflows
{
    private readonly object _gate = new();
    private (string Address, TemporalIncidentWorkflows Workflows)? _current;

    public Task EnsureStartedAsync(IncidentSnapshot incident, CancellationToken cancellationToken) => Current().EnsureStartedAsync(incident, cancellationToken);

    public Task SignalAsync(IncidentSnapshot incident, IncidentSignal signal, CancellationToken cancellationToken) => Current().SignalAsync(incident, signal, cancellationToken);

    private TemporalIncidentWorkflows Current()
    {
        lock (_gate)
        {
            string target = address();
            if (_current is not { } current || current.Address != target)
            {
                IOptions<TemporalOptions> options = Options.Create(new TemporalOptions { Address = target, TaskQueue = taskQueue });
                var clients = new TemporalClientProvider(options, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
                _current = (target, new TemporalIncidentWorkflows(clients, options, runtime, Microsoft.Extensions.Logging.Abstractions.NullLogger<TemporalIncidentWorkflows>.Instance));
            }

            return _current.Value.Workflows;
        }
    }
}
