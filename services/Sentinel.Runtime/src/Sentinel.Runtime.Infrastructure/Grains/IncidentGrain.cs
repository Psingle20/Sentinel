using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Application.Abstractions.Persistence;
using Sentinel.Runtime.Application.Incidents.Commands;

namespace Sentinel.Runtime.Infrastructure.Grains;

/// <summary>
/// One activation per incident serializes its commands. The grain keeps the rehydrated aggregate
/// between commands, but holds nothing that cannot be rebuilt from the stream: after any failure it
/// drops the copy and reads the stream again, and the store's optimistic append rejects a write
/// from a stale activation that survived a split cluster.
/// </summary>
internal sealed class IncidentGrain(IServiceScopeFactory scopes, ILogger<IncidentGrain> logger) : Grain, IIncidentGrain
{
    private const int Attempts = 3;

    private LoadedIncident? _loaded;

    private IncidentId Id => IncidentId.From(this.GetPrimaryKey());

    public async Task<CommandOutcome> Send(IncidentCommand command)
    {
        int attempt = 0;
        while (true)
        {
            attempt++;
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            try
            {
                (Result<CommandReply> reply, LoadedIncident? loaded) = await scope.ServiceProvider.GetRequiredService<IncidentCommandExecutor>()
                    .ExecuteAsync(Id, command, _loaded, CancellationToken.None);
                _loaded = loaded;
                return CommandOutcome.From(reply);
            }
            catch (IncidentConcurrencyException exception) when (attempt < Attempts)
            {
                // Another writer appended first: re-read the stream and apply the command again.
                logger.LogWarning(exception, "Incident {IncidentId} moved on under this activation; retrying {Command}", Id, command.GetType().Name);
                _loaded = null;
            }
            catch
            {
                _loaded = null;
                throw;
            }
        }
    }

    public async Task<IncidentSnapshot?> Get()
    {
        if (_loaded is null)
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            _loaded = await scope.ServiceProvider.GetRequiredService<IncidentCommandExecutor>().LoadAsync(Id, CancellationToken.None);
        }

        return _loaded is null ? null : IncidentSnapshot.Of(_loaded.Incident);
    }
}
