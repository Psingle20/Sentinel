namespace Sentinel.Executor.Application.Abstractions.Persistence;

/// <summary>One recorded change to an operator control, with the operator its attestation named.</summary>
public sealed record ControlChange(string Control, bool Value, string ChangedBy, DateTimeOffset ChangedAt);
