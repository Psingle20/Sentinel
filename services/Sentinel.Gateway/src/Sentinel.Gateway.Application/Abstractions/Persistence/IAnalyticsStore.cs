using CleanArchitecture.BuildingBlocks;
using Sentinel.Gateway.Application.Analytics;

namespace Sentinel.Gateway.Application.Abstractions.Persistence;

public interface IAnalyticsStore
{
    Task IncrementAsync(DateOnly day, string eventName, string dimensions, CancellationToken cancellationToken);

    Task<IReadOnlyList<AnalyticsCount>> SinceAsync(DateOnly from, CancellationToken cancellationToken);
}
