using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Abstractions.Persistence;
using Sentinel.Gateway.Application.Analytics;

namespace Sentinel.Gateway.Application.BusinessTasks.Commands.Analytics;

/// <summary>Counts an event per day and approved property values; anything outside the catalog is refused, never stored.</summary>
public sealed class RecordAnalyticsEventCommandHandler(IAnalyticsStore store, TimeProvider time) : ICommandHandler<RecordAnalyticsEventCommand>
{
    public async Task<Result> Handle(RecordAnalyticsEventCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<string> dimensions = AnalyticsCatalog.Dimensions(command.Event.Name, command.Event.Properties);
        if (dimensions.IsFailure)
        {
            return Result.Failure(dimensions.Error);
        }

        await store.IncrementAsync(DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime), command.Event.Name, dimensions.Value, cancellationToken);
        return Result.Success();
    }
}
