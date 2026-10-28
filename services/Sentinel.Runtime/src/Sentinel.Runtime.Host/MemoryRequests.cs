namespace Sentinel.Runtime.Host;

/// <summary>An operator places or lifts a legal hold on an incident's memory.</summary>
public sealed record LegalHoldRequest(bool Hold, string Actor);

/// <summary>An operator deletes one incident's memory for privacy.</summary>
public sealed record MemoryDeletionRequest(string Actor, string Reason);

/// <summary>An operator runs the retention purge; a dry run unless <see cref="DryRun"/> is explicitly false.</summary>
public sealed record MemoryPurgeRequest(string Actor, bool DryRun = true, int? Limit = null);
