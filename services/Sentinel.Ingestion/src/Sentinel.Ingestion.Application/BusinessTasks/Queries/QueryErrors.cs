using CleanArchitecture.BuildingBlocks;

namespace Sentinel.Ingestion.Application.BusinessTasks.Queries;

/// <summary>Why Ingestion refuses an observation query or a knowledge search.</summary>
public static class QueryErrors
{
    public static readonly Error UnknownScope = Error.NotFound("Observations.UnknownScope", "No observations are ingested for this tenant and environment.");

    public static readonly Error InvalidRange = Error.Problem("Observations.InvalidRange", "The range must end after it starts.");

    /// <summary>The query bulkhead is full. The Api answers it with 503, so the caller retries shortly.</summary>
    public static readonly Error BulkheadFull = Error.Failure("Ingestion.QueryBulkheadFull", "Ingestion is serving its maximum number of queries; retry shortly.");
}
