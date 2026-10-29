namespace Sentinel.AgentWorker.Application.Abstractions.Persistence;

/// <summary>The estimated model spend recorded with AgentWorker's executions, for the spend caps.</summary>
public interface IModelSpend
{
    /// <summary>
    /// The estimated cost of every execution started at or after <paramref name="since"/>, in US dollars; with
    /// <paramref name="model"/>, of that model's executions only.
    /// </summary>
    Task<double> SpentSinceAsync(DateTimeOffset since, string? model, CancellationToken cancellationToken);
}
