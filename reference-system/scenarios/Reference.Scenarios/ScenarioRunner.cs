using System.Globalization;

namespace Reference.Scenarios;

public sealed record StepExecution(int AtSeconds, string Action, string Service, string? Version, string Role, DateTimeOffset? ExecutedAt, string? Error);

/// <summary>
/// A run's progress. <see cref="ActivatedAt"/> is the time of the first step: every offset in the
/// manifest is relative to it.
/// </summary>
public sealed record ScenarioRunView(
    string RunId,
    string ScenarioId,
    string Phase,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<StepExecution> Steps,
    string? Error);

public sealed record StartRunRequest(string ScenarioId);

public enum StartRunOutcome
{
    Started,
    UnknownScenario,
    RunActive
}

/// <summary>
/// Runs one scenario at a time: reset to baseline, warm up with healthy traffic, then execute
/// each step at its offset. Resetting is idempotent, so a run always starts from the same state.
/// </summary>
public sealed class ScenarioRunner(
    ScenarioCatalog catalog,
    IServiceScopeFactory scopes,
    TimeProvider time,
    IHostApplicationLifetime lifetime,
    ILogger<ScenarioRunner> logger)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, ScenarioRunView> _runs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CancellationTokenSource> _cancellations = new(StringComparer.Ordinal);
    private string? _currentRunId;

    public ScenarioRunView? Current
    {
        get
        {
            lock (_gate)
            {
                return _currentRunId is null ? null : _runs[_currentRunId];
            }
        }
    }

    public ScenarioRunView? Find(string runId)
    {
        lock (_gate)
        {
            return _runs.GetValueOrDefault(runId);
        }
    }

    public (StartRunOutcome Outcome, ScenarioRunView? Run) Start(string scenarioId)
    {
        if (!catalog.Manifests.TryGetValue(scenarioId, out ScenarioManifest? manifest))
        {
            return (StartRunOutcome.UnknownScenario, null);
        }

        ScenarioRunView run;
        lock (_gate)
        {
            if (_currentRunId is not null && _runs[_currentRunId].CompletedAt is null)
            {
                return (StartRunOutcome.RunActive, _runs[_currentRunId]);
            }

            DateTimeOffset now = time.GetUtcNow();
            string runId = $"{manifest.Id}-{now.ToString("yyyyMMddTHHmmss", CultureInfo.InvariantCulture)}";
            run = new ScenarioRunView(
                runId,
                manifest.Id,
                "resetting",
                now,
                null,
                null,
                [.. manifest.Steps.Select(step => new StepExecution(step.AtSeconds, step.Action, step.Service, step.Version, step.Role, null, null))],
                null);
            _runs[runId] = run;
            _currentRunId = runId;
            _cancellations[runId] = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
        }

        _ = Task.Run(() => ExecuteAsync(manifest, run.RunId), CancellationToken.None);
        return (StartRunOutcome.Started, run);
    }

    /// <summary>Stops a run between steps. Already-executed steps are not undone; reset for that.</summary>
    public bool Stop(string runId)
    {
        lock (_gate)
        {
            if (!_cancellations.TryGetValue(runId, out CancellationTokenSource? cancellation))
            {
                return _runs.ContainsKey(runId);
            }

            cancellation.Cancel();
            return true;
        }
    }

    private async Task ExecuteAsync(ScenarioManifest manifest, string runId)
    {
        CancellationToken stopping;
        lock (_gate)
        {
            stopping = _cancellations[runId].Token;
        }

        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            ReferenceControlClient control = scope.ServiceProvider.GetRequiredService<ReferenceControlClient>();

            await control.ResetAsync(stopping);
            Update(runId, run => run with { Phase = "warmup" });
            logger.LogInformation("Run {RunId}: reset done, warming up for {Seconds}s", runId, manifest.WarmupSeconds);
            await Task.Delay(TimeSpan.FromSeconds(manifest.WarmupSeconds), time, stopping);

            DateTimeOffset activatedAt = time.GetUtcNow();
            Update(runId, run => run with { Phase = "running", ActivatedAt = activatedAt });

            for (int index = 0; index < manifest.Steps.Count; index++)
            {
                ScenarioStep step = manifest.Steps[index];
                TimeSpan wait = activatedAt + TimeSpan.FromSeconds(step.AtSeconds) - time.GetUtcNow();
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, time, stopping);
                }

                string? error = await ExecuteStepAsync(control, step, runId, stopping);
                int stepIndex = index;
                DateTimeOffset executedAt = time.GetUtcNow();
                Update(runId, run => run with
                {
                    Steps = [.. run.Steps.Select((existing, position) => position == stepIndex ? existing with { ExecutedAt = executedAt, Error = error } : existing)]
                });
            }

            TimeSpan remaining = activatedAt + TimeSpan.FromSeconds(manifest.DurationSeconds) - time.GetUtcNow();
            Update(runId, run => run with { Phase = "observing" });
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, time, stopping);
            }

            Update(runId, run => run with { Phase = "completed", CompletedAt = time.GetUtcNow() });
            logger.LogInformation("Run {RunId} completed", runId);
        }
        catch (OperationCanceledException exception) when (stopping.IsCancellationRequested)
        {
            logger.LogInformation(exception, "Run {RunId} stopped", runId);
            Update(runId, run => run with { Phase = "stopped", CompletedAt = time.GetUtcNow() });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Run {RunId} failed", runId);
            Update(runId, run => run with { Phase = "failed", CompletedAt = time.GetUtcNow(), Error = exception.Message });
        }
        finally
        {
            lock (_gate)
            {
                if (_cancellations.Remove(runId, out CancellationTokenSource? cancellation))
                {
                    cancellation.Dispose();
                }
            }
        }
    }

    private async Task<string?> ExecuteStepAsync(ReferenceControlClient control, ScenarioStep step, string runId, CancellationToken cancellationToken)
    {
        logger.LogInformation("Run {RunId}: {Action} {Service} {Version} at +{At}s", runId, step.Action, step.Service, step.Version, step.AtSeconds);
        try
        {
            switch (step.Action)
            {
                case "deploy":
                case "configChange":
                    await control.DeployAsync(step, runId, cancellationToken);
                    break;
                case "startBatch":
                    await control.StartBatchAsync(step, cancellationToken);
                    break;
                case "stopBatch":
                    await control.StopBatchAsync(cancellationToken);
                    break;
                default:
                    return $"Unknown action {step.Action}.";
            }

            return null;
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "Run {RunId}: step {Action} {Service} failed", runId, step.Action, step.Service);
            return exception.Message;
        }
    }

    private void Update(string runId, Func<ScenarioRunView, ScenarioRunView> change)
    {
        lock (_gate)
        {
            _runs[runId] = change(_runs[runId]);
        }
    }
}
