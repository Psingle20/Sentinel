using CleanArchitecture.BuildingBlocks;
using Reference.DeployController.Domain.Revisions.Events;

namespace Reference.DeployController.Domain.Revisions;

/// <summary>
/// Every deployment of the managed services, in the order they started. One rollout per service at a
/// time; a request whose idempotency key was used before gets the original deployment back. The ledger
/// decides; switching a service's revision and waiting for a rollout happen outside it.
/// </summary>
public sealed class DeploymentLedger : Entity
{
    private readonly Dictionary<string, ManagedRevisions> _services = new(StringComparer.Ordinal);
    private readonly List<DeploymentRecord> _history = [];
    private readonly Dictionary<string, string> _byIdempotencyKey = new(StringComparer.Ordinal);

    public DeploymentLedger(IEnumerable<ManagedRevisions> services)
    {
        ArgumentNullException.ThrowIfNull(services);

        foreach (ManagedRevisions service in services)
        {
            _services[service.Service] = service;
        }
    }

    /// <summary>The managed services, in configuration order.</summary>
    public IReadOnlyList<ManagedRevisions> Services => [.. _services.Values];

    public ManagedRevisions? Service(string service) => _services.GetValueOrDefault(service);

    /// <summary>The deployment an idempotency key already started, if any.</summary>
    public DeploymentRecord? Replay(string? idempotencyKey) =>
        idempotencyKey is not null && _byIdempotencyKey.TryGetValue(idempotencyKey, out string? deploymentId) ? Find(deploymentId) : null;

    public IReadOnlyList<DeploymentRecord> Query(DateTimeOffset? since, DateTimeOffset? until, string? service) =>
        [.. _history.Where(record =>
            (since is null || (record.CompletedAt ?? record.StartedAt) >= since || record.StartedAt >= since)
            && (until is null || record.StartedAt < until)
            && (service is null || record.Service == service))];

    public DeploymentRecord? Find(string deploymentId) => _history.Find(record => record.DeploymentId == deploymentId);

    public DeploymentRecord? Active(string service) => _history.FindLast(record => record.Service == service && record.Status == DeploymentStatus.InProgress);

    /// <summary>
    /// Starts a deployment of <paramref name="service"/> away from <paramref name="serving"/>. A
    /// configuration change keeps the serving revision and completes at once; anything else must name a
    /// managed revision and rolls out while no other rollout of the service is in progress.
    /// </summary>
    public Result<DeploymentRecord> Start(
        string service,
        DeploymentKind kind,
        string requestedVersion,
        string serving,
        string actor,
        string? reason,
        string? configurationDiff,
        string? idempotencyKey,
        DateTimeOffset now)
    {
        ManagedRevisions managed = _services.GetValueOrDefault(service) ?? throw new ArgumentException($"{service} is not a managed service.", nameof(service));
        string version = kind == DeploymentKind.ConfigChange ? serving : requestedVersion;
        if (!managed.Revisions.Contains(version))
        {
            return Result.Failure<DeploymentRecord>(DeploymentErrors.UnknownRevision(requestedVersion, service));
        }

        if (Active(service) is not null)
        {
            return Result.Failure<DeploymentRecord>(DeploymentErrors.Conflict);
        }

        bool immediate = kind == DeploymentKind.ConfigChange;
        var record = new DeploymentRecord
        {
            DeploymentId = Guid.CreateVersion7(now).ToString(),
            Service = service,
            Version = version,
            PreviousVersion = serving,
            Kind = kind,
            Actor = actor,
            Reason = reason,
            ConfigurationDiff = configurationDiff,
            StartedAt = now,
            CompletedAt = immediate ? now : null,
            Status = immediate ? DeploymentStatus.Completed : DeploymentStatus.InProgress,
            IdempotencyKey = idempotencyKey
        };
        _history.Add(record);
        if (idempotencyKey is not null)
        {
            _byIdempotencyKey[idempotencyKey] = record.DeploymentId;
        }

        Raise(new DeploymentStarted(record));
        return Result.Success(record);
    }

    /// <summary>Finishes a rollout in progress: completed when <paramref name="error"/> is null, failed otherwise.</summary>
    public DeploymentRecord? Finish(string deploymentId, string? error, DateTimeOffset now)
    {
        int index = _history.FindIndex(record => record.DeploymentId == deploymentId);
        DeploymentStatus status = error is null ? DeploymentStatus.Completed : DeploymentStatus.Failed;
        if (index < 0 || !DeploymentStatusTransitions.CanMove(_history[index].Status, status))
        {
            return null;
        }

        DeploymentRecord finished = _history[index] with { Status = status, CompletedAt = now, Error = error };
        _history[index] = finished;
        Raise(new DeploymentFinished(finished));
        return finished;
    }

    /// <summary>Records <paramref name="service"/>'s return to its baseline; nothing when it already serves it.</summary>
    public DeploymentRecord? Reset(string service, string? serving, DateTimeOffset now)
    {
        ManagedRevisions managed = _services.GetValueOrDefault(service) ?? throw new ArgumentException($"{service} is not a managed service.", nameof(service));
        if (serving == managed.Baseline)
        {
            return null;
        }

        var record = new DeploymentRecord
        {
            DeploymentId = Guid.CreateVersion7(now).ToString(),
            Service = service,
            Version = managed.Baseline,
            PreviousVersion = serving,
            Kind = DeploymentKind.Reset,
            Actor = "reference-reset",
            Reason = "Scenario reset to baseline",
            StartedAt = now,
            CompletedAt = now,
            Status = DeploymentStatus.Completed
        };
        _history.Add(record);
        Raise(new ServiceReset(record));
        return record;
    }
}
