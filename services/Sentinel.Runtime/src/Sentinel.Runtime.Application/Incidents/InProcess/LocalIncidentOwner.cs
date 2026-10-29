using System.Collections.Concurrent;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Domain.Incidents;
using ContractRecoveryEvaluated = Sentinel.Contracts.Incidents.RecoveryEvaluated;
using DomainRecoveryEvaluated = Sentinel.Runtime.Domain.Incidents.Events.RecoveryEvaluated;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Application.Abstractions.Persistence;
using Sentinel.Runtime.Application.Incidents.Commands;

namespace Sentinel.Runtime.Application.Incidents.InProcess;

/// <summary>The in-process <see cref="IIncidentOwner"/>: a lock per incident, a fresh read per command, one retry on a concurrency conflict.</summary>
public sealed class LocalIncidentOwner(IServiceScopeFactory scopes, IncidentLocks locks) : IIncidentOwner
{
    public async Task<Result<CommandReply>> SendAsync(IncidentId incidentId, IncidentCommand command, CancellationToken cancellationToken)
    {
        using IDisposable held = await locks.AcquireAsync($"incident:{incidentId}", cancellationToken);
        int attempt = 0;
        while (true)
        {
            attempt++;
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            try
            {
                return (await scope.ServiceProvider.GetRequiredService<IncidentCommandExecutor>().ExecuteAsync(incidentId, command, null, cancellationToken)).Reply;
            }
            catch (IncidentConcurrencyException) when (attempt < 3)
            {
                // Another writer appended first; the command is data, so it is applied again to a fresh read.
            }
        }
    }

    public async Task<IncidentSnapshot?> GetAsync(IncidentId incidentId, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        LoadedIncident? loaded = await scope.ServiceProvider.GetRequiredService<IncidentCommandExecutor>().LoadAsync(incidentId, cancellationToken);
        return loaded is null ? null : IncidentSnapshot.Of(loaded.Incident);
    }
}
