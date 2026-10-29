namespace Sentinel.Executor.Api.Contracts;

/// <summary>An operator's change to a control: whether it is engaged. <see cref="Actor"/> is informational: the operator recorded is the one the attestation names.</summary>
public sealed record ControlChangeBody(bool Enabled, string Actor);
