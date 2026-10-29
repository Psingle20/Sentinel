using CleanArchitecture.BuildingBlocks.Messaging;

namespace Reference.Scenarios.Application.BusinessProcesses.Runs;

/// <summary>Returns the reference system to its baseline: revisions reset, faults cleared, batch stopped.</summary>
public sealed record ResetReferenceRequest : IRequest;
