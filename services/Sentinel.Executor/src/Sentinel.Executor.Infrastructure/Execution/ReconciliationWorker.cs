using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sentinel.Executor.Application.BusinessProcesses.Execution;

namespace Sentinel.Executor.Infrastructure.Execution;

/// <summary>Finishes requests that stopped part-way, for example because Executor was killed mid-execution; each in its own scope.</summary>
internal sealed class ReconciliationWorker(IServiceScopeFactory scopes, TimeProvider time, ILogger<ReconciliationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                IReadOnlyList<string> unfinished;
                await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
                {
                    unfinished = (await Requests(scope).HandleRequestAsync(new ListUnfinishedExecutionsRequest(), stoppingToken)).Value;
                }

                foreach (string idempotencyKey in unfinished)
                {
                    await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                    Result reconciled = await Requests(scope).HandleRequestAsync(new ReconcileExecutionRequest(idempotencyKey), stoppingToken);
                    if (reconciled.IsFailure)
                    {
                        logger.LogError("Reconciling {Key} failed: {Reason}", idempotencyKey, reconciled.Error.Description);
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Reconciliation failed; it is retried on the next poll");
            }

            await Task.Delay(PollInterval, time, stoppingToken);
        }
    }

    private static IRequestDispatcher Requests(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
}
