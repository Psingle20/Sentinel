namespace Sentinel.Runtime.Api.Contracts;

/// <summary>An operator runs the retention purge; a dry run unless <see cref="DryRun"/> is explicitly false.</summary>
public sealed record MemoryPurgeBody(string Actor, bool DryRun = true, int? Limit = null);