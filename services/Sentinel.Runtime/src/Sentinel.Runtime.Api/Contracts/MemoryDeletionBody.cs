namespace Sentinel.Runtime.Api.Contracts;

/// <summary>An operator deletes one incident's memory for privacy.</summary>
public sealed record MemoryDeletionBody(string Actor, string Reason);