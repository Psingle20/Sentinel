using System.Collections.Concurrent;
using Reference.Common.Control;

namespace Reference.DeployController;

public enum DeploymentKind
{
    Deploy,
    Rollback,
    ConfigChange,
    Reset
}

public enum DeploymentStatus
{
    InProgress,
    Completed,
    Failed
}

/// <summary>
/// One deployment. The new revision serves only from <see cref="CompletedAt"/>: the reference
/// rollout switches all traffic at completion, so "started" and "serving" are different times.
/// </summary>
public sealed record DeploymentRecord
{
    public required string DeploymentId { get; init; }

    public required string Service { get; init; }

    public required string Version { get; init; }

    public string? PreviousVersion { get; init; }

    public required DeploymentKind Kind { get; init; }

    public required string Actor { get; init; }

    public string? Reason { get; init; }

    public string? ConfigurationDiff { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public required DeploymentStatus Status { get; init; }

    public string? IdempotencyKey { get; init; }

    public string? Error { get; init; }
}

public sealed record DeployRequest(
    string Version,
    DeploymentKind Kind = DeploymentKind.Deploy,
    string? Actor = null,
    string? Reason = null,
    int? RolloutSeconds = null,
    string? ConfigurationDiff = null);

public sealed record ServiceView(
    string Name,
    string? ServingRevision,
    string BaselineRevision,
    IReadOnlyCollection<string> Revisions,
    DeploymentRecord? ActiveDeployment,
    string? Error);

public sealed class ManagedService
{
    public string Baseline { get; set; } = string.Empty;

    public IReadOnlyList<string> Revisions { get; set; } = [];
}

public sealed class DeployControllerOptions
{
    public int RolloutSeconds { get; set; } = 20;

    public Dictionary<string, ManagedService> Services { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Bearer token for deployments and rollbacks: held by the scenario runner and Sentinel.Executor.</summary>
    public string? DeployToken { get; set; }

    /// <summary>Bearer token for reading services and deployments: held by Sentinel.Ingestion.</summary>
    public string? ReadToken { get; set; }
}

/// <summary>Reads and switches a reference service's revision through its control endpoint.</summary>
public sealed class RevisionClient(HttpClient http, IConfiguration configuration)
{
    public async Task<string?> GetServingRevisionAsync(string service, CancellationToken cancellationToken)
    {
        RevisionView? view = await http.GetFromJsonAsync<RevisionView>(new Uri(BaseAddress(service), "/_reference/revision"), cancellationToken);
        return view?.Revision;
    }

    public async Task SwitchAsync(string service, string revision, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(BaseAddress(service), "/_reference/revision"))
        {
            Content = JsonContent.Create(new SwitchRevisionRequest(revision))
        };
        request.Headers.Add(FailureControl.TokenHeader, configuration["Reference:ControlToken"]);
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private Uri BaseAddress(string service) =>
        new(configuration[$"services:{service}:http:0"]
            ?? throw new InvalidOperationException($"The address of {service} is not configured."));
}

public enum DeployOutcome
{
    Accepted,
    Replayed,
    UnknownService,
    UnknownRevision,
    Conflict
}

public sealed record DeployResult(DeployOutcome Outcome, DeploymentRecord? Deployment);

/// <summary>
/// The deployment ledger and rollout engine. One rollout per service at a time; a request whose
/// Idempotency-Key was already used returns the original deployment instead of starting another.
/// </summary>
public sealed class DeploymentLedger(
    DeployControllerOptionsAccessor options,
    IServiceScopeFactory scopes,
    TimeProvider time,
    IHostApplicationLifetime lifetime,
    ILogger<DeploymentLedger> logger)
{
    private readonly Lock _gate = new();
    private readonly List<DeploymentRecord> _history = [];
    private readonly ConcurrentDictionary<string, string> _byIdempotencyKey = new(StringComparer.Ordinal);

    public DeployControllerOptions Options => options.Value;

    public IReadOnlyList<DeploymentRecord> Query(DateTimeOffset? since, DateTimeOffset? until, string? service)
    {
        lock (_gate)
        {
            return [.. _history.Where(record =>
                (since is null || (record.CompletedAt ?? record.StartedAt) >= since || record.StartedAt >= since)
                && (until is null || record.StartedAt < until)
                && (service is null || record.Service == service))];
        }
    }

    public DeploymentRecord? Find(string deploymentId)
    {
        lock (_gate)
        {
            return _history.Find(record => record.DeploymentId == deploymentId);
        }
    }

    public DeploymentRecord? Active(string service)
    {
        lock (_gate)
        {
            return _history.FindLast(record => record.Service == service && record.Status == DeploymentStatus.InProgress);
        }
    }

    public async Task<DeployResult> StartAsync(string service, DeployRequest request, string? idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (idempotencyKey is not null && _byIdempotencyKey.TryGetValue(idempotencyKey, out string? existingId))
        {
            return new DeployResult(DeployOutcome.Replayed, Find(existingId));
        }

        if (!options.Value.Services.TryGetValue(service, out ManagedService? managed))
        {
            return new DeployResult(DeployOutcome.UnknownService, null);
        }

        string serving = await ServingRevisionAsync(service, cancellationToken) ?? managed.Baseline;
        string version = request.Kind == DeploymentKind.ConfigChange ? serving : request.Version;
        if (!managed.Revisions.Contains(version))
        {
            return new DeployResult(DeployOutcome.UnknownRevision, null);
        }

        DeploymentRecord record;
        lock (_gate)
        {
            if (idempotencyKey is not null && _byIdempotencyKey.TryGetValue(idempotencyKey, out existingId))
            {
                return new DeployResult(DeployOutcome.Replayed, _history.Find(existing => existing.DeploymentId == existingId));
            }

            DeploymentRecord? active = _history.FindLast(existing => existing.Service == service && existing.Status == DeploymentStatus.InProgress);
            if (active is not null)
            {
                return new DeployResult(DeployOutcome.Conflict, active);
            }

            DateTimeOffset now = time.GetUtcNow();
            bool immediate = request.Kind == DeploymentKind.ConfigChange;
            record = new DeploymentRecord
            {
                DeploymentId = Guid.CreateVersion7(now).ToString(),
                Service = service,
                Version = version,
                PreviousVersion = serving,
                Kind = request.Kind,
                Actor = request.Actor ?? "unknown",
                Reason = request.Reason,
                ConfigurationDiff = request.ConfigurationDiff,
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
        }

        logger.LogInformation("{Kind} of {Service} {Version} (from {Previous}) started by {Actor}", record.Kind, service, version, serving, record.Actor);
        if (record.Status == DeploymentStatus.InProgress)
        {
            var rollout = TimeSpan.FromSeconds(request.RolloutSeconds ?? options.Value.RolloutSeconds);
            _ = Task.Run(() => CompleteRolloutAsync(record, rollout), CancellationToken.None);
        }

        return new DeployResult(DeployOutcome.Accepted, record);
    }

    /// <summary>Returns every service to its baseline revision immediately; services already there are untouched.</summary>
    public async Task<IReadOnlyList<DeploymentRecord>> ResetAsync(CancellationToken cancellationToken)
    {
        var resets = new List<DeploymentRecord>();
        foreach ((string service, ManagedService managed) in options.Value.Services)
        {
            string? serving = await ServingRevisionAsync(service, cancellationToken);
            if (serving == managed.Baseline)
            {
                continue;
            }

            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<RevisionClient>().SwitchAsync(service, managed.Baseline, cancellationToken);
            DateTimeOffset now = time.GetUtcNow();
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
            lock (_gate)
            {
                _history.Add(record);
            }

            resets.Add(record);
        }

        return resets;
    }

    public async Task<string?> ServingRevisionAsync(string service, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<RevisionClient>().GetServingRevisionAsync(service, cancellationToken);
    }

    private async Task CompleteRolloutAsync(DeploymentRecord record, TimeSpan rollout)
    {
        CancellationToken stopping = lifetime.ApplicationStopping;
        DeploymentRecord finished;
        try
        {
            await Task.Delay(rollout, time, stopping);
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<RevisionClient>().SwitchAsync(record.Service, record.Version, stopping);
            finished = record with { Status = DeploymentStatus.Completed, CompletedAt = time.GetUtcNow() };
            logger.LogInformation("{Kind} of {Service} {Version} completed", record.Kind, record.Service, record.Version);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !stopping.IsCancellationRequested)
        {
            finished = record with { Status = DeploymentStatus.Failed, CompletedAt = time.GetUtcNow(), Error = exception.Message };
            logger.LogError(exception, "{Kind} of {Service} {Version} failed", record.Kind, record.Service, record.Version);
        }

        lock (_gate)
        {
            int index = _history.FindIndex(existing => existing.DeploymentId == record.DeploymentId);
            _history[index] = finished;
        }
    }
}

/// <summary>Holds validated options; the ledger is a singleton and options do not change at runtime.</summary>
public sealed class DeployControllerOptionsAccessor(DeployControllerOptions value)
{
    public DeployControllerOptions Value { get; } = value;
}
