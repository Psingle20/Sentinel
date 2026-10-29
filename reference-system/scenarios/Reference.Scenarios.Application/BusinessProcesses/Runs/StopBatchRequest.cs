using CleanArchitecture.BuildingBlocks.Messaging;

namespace Reference.Scenarios.Application.BusinessProcesses.Runs;

/// <summary>Stops the reporting-worker's batch.</summary>
public sealed record StopBatchRequest : IRequest;
