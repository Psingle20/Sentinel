using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.Memory;

namespace Sentinel.Runtime.Application.BusinessProcesses.Memory;

/// <summary>An operator runs the retention purge, a dry run unless asked otherwise.</summary>
public sealed record PurgeIncidentMemoryRequest(bool DryRun, string Actor, int Limit) : IRequest<PurgeReport>;
