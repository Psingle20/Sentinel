using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.Incidents;

namespace Sentinel.Runtime.Infrastructure.Grains;

/// <summary>A command's result in a form that crosses the grain boundary.</summary>
public sealed record CommandOutcome(CommandReply? Reply, string? ErrorCode, string? ErrorDescription, ErrorType ErrorType)
{
    public static CommandOutcome From(Result<CommandReply> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess
            ? new CommandOutcome(result.Value, null, null, ErrorType.Failure)
            : new CommandOutcome(null, result.Error.Code, result.Error.Description, result.Error.Type);
    }

    public Result<CommandReply> ToResult() => Reply is { } reply
        ? Result.Success(reply)
        : Result.Failure<CommandReply>(ErrorType switch
        {
            ErrorType.NotFound => Error.NotFound(ErrorCode!, ErrorDescription!),
            ErrorType.Conflict => Error.Conflict(ErrorCode!, ErrorDescription!),
            ErrorType.Problem or ErrorType.Validation => Error.Problem(ErrorCode!, ErrorDescription!),
            _ => Error.Failure(ErrorCode!, ErrorDescription!)
        });
}

/// <summary>The only writer of one incident's stream (ADR-004), keyed by the incident ID.</summary>
public interface IIncidentGrain : IGrainWithGuidKey
{
    Task<CommandOutcome> Send(IncidentCommand command);

    Task<IncidentSnapshot?> Get();
}

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

/// <summary>Correlation for one tenant and environment, keyed <c>tenant/environment</c>.</summary>
public interface ICorrelationGrain : IGrainWithStringKey
{
    Task Observe(Observation observation);
}

/// <summary>
/// Serializes detection and correlation for its partition, so two samples of the same failure can
/// never race to open two incidents. Its state lives in Runtime's correlation tables.
/// </summary>
internal sealed class CorrelationGrain(IServiceScopeFactory scopes) : Grain, ICorrelationGrain
{
    public async Task Observe(Observation observation)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DetectionService>().HandleAsync(observation, CancellationToken.None);
    }
}

/// <summary>Hot context for one service, keyed <c>tenant/environment/service</c>.</summary>
public interface IServiceGrain : IGrainWithStringKey
{
    Task<ServiceContext> Get();

    Task ObserveDependency(ServiceId callee);

    Task ObserveDependent(ServiceId caller);
}

/// <summary>
/// Caches a service's catalog facts and its observed dependency edges. Never authoritative: on
/// activation it is rebuilt from configuration and Runtime's correlation store, and only edges it
/// has not seen recently are written back.
/// </summary>
internal sealed class ServiceGrain(IServiceScopeFactory scopes, ServiceTopologyCache topologies, TimeProvider time, IOptions<RuntimeOptions> options) : Grain, IServiceGrain
{
    private static readonly TimeSpan PersistEvery = TimeSpan.FromMinutes(1);

    private readonly HashSet<ServiceId> _dependencies = [];
    private readonly HashSet<ServiceId> _dependents = [];
    private readonly Dictionary<ServiceId, DateTimeOffset> _persisted = [];
    private TenantId _tenant;
    private EnvironmentId _environment;
    private ServiceId _service;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        string[] key = this.GetPrimaryKeyString().Split('/');
        _tenant = TenantId.From(key[0]);
        _environment = EnvironmentId.From(key[1]);
        _service = ServiceId.From(key[2]);

        RuntimeOptions settings = options.Value;
        foreach ((string caller, string[] callees) in settings.Dependencies)
        {
            foreach (string callee in callees)
            {
                if (caller == _service.Value)
                {
                    _dependencies.Add(ServiceId.From(callee));
                }
                else if (callee == _service.Value)
                {
                    _dependents.Add(ServiceId.From(caller));
                }
            }
        }

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        foreach ((ServiceId caller, ServiceId callee) in await scope.ServiceProvider.GetRequiredService<ICorrelationStore>()
            .GetEdgesAsync(_tenant, _environment, time.GetUtcNow() - settings.Correlation.EdgeRetention, cancellationToken))
        {
            if (caller == _service)
            {
                _dependencies.Add(callee);
            }
            else if (callee == _service)
            {
                _dependents.Add(caller);
            }
        }

        await base.OnActivateAsync(cancellationToken);
    }

    public Task<ServiceContext> Get() =>
        Task.FromResult(new ServiceContext(_service, options.Value.Owners.TryGetValue(_service.Value, out string? owner) ? owner : null, [.. _dependencies], [.. _dependents]));

    public async Task ObserveDependency(ServiceId callee)
    {
        DateTimeOffset now = time.GetUtcNow();
        bool known = _dependencies.Contains(callee);
        if (known && _persisted.TryGetValue(callee, out DateTimeOffset persisted) && now - persisted < PersistEvery)
        {
            return;
        }

        await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ICorrelationStore>().SaveEdgeAsync(_tenant, _environment, _service, callee, now, CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<CleanArchitecture.BuildingBlocks.Integration.IUnitOfWork>().CommitAsync(CancellationToken.None);
        }

        _persisted[callee] = now;
        if (_dependencies.Add(callee))
        {
            topologies.Invalidate(_tenant, _environment);
        }
    }

    public Task ObserveDependent(ServiceId caller)
    {
        _dependents.Add(caller);
        return Task.CompletedTask;
    }
}

/// <summary><see cref="IIncidentOwner"/> backed by IncidentGrain.</summary>
internal sealed class GrainIncidentOwner(IGrainFactory grains) : IIncidentOwner
{
    public async Task<Result<CommandReply>> SendAsync(IncidentId incidentId, IncidentCommand command, CancellationToken cancellationToken) =>
        (await grains.GetGrain<IIncidentGrain>(incidentId.Value).Send(command)).ToResult();

    public Task<IncidentSnapshot?> GetAsync(IncidentId incidentId, CancellationToken cancellationToken) =>
        grains.GetGrain<IIncidentGrain>(incidentId.Value).Get();
}

/// <summary><see cref="ICorrelationPartitions"/> backed by CorrelationGrain.</summary>
internal sealed class GrainCorrelationPartitions(IGrainFactory grains) : ICorrelationPartitions
{
    public Task ObserveAsync(Observation observation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observation);

        return grains.GetGrain<ICorrelationGrain>($"{observation.TenantId}/{observation.EnvironmentId}").Observe(observation);
    }
}

/// <summary><see cref="IServiceCatalog"/> backed by ServiceGrain.</summary>
internal sealed class GrainServiceCatalog(IGrainFactory grains) : IServiceCatalog
{
    public Task<ServiceContext> GetAsync(TenantId tenantId, EnvironmentId environmentId, ServiceId serviceId, CancellationToken cancellationToken) =>
        grains.GetGrain<IServiceGrain>(Key(tenantId, environmentId, serviceId)).Get();

    public async Task ObserveEdgeAsync(TenantId tenantId, EnvironmentId environmentId, ServiceId caller, ServiceId callee, CancellationToken cancellationToken)
    {
        await grains.GetGrain<IServiceGrain>(Key(tenantId, environmentId, caller)).ObserveDependency(callee);
        await grains.GetGrain<IServiceGrain>(Key(tenantId, environmentId, callee)).ObserveDependent(caller);
    }

    private static string Key(TenantId tenantId, EnvironmentId environmentId, ServiceId serviceId) => $"{tenantId}/{environmentId}/{serviceId}";
}
