namespace Sentinel.Executor.Api.Contracts;

/// <summary>An operator's change to a control: whether it is engaged, and who changed it.</summary>
public sealed record ControlChangeBody(bool Enabled, string Actor);
