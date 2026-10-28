namespace Sentinel.Executor.Host;

/// <summary>An operator's change to a control: whether it is engaged, and who changed it.</summary>
public sealed record ControlChange(bool Enabled, string Actor);
