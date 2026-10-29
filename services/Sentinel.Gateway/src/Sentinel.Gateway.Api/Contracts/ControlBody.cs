namespace Sentinel.Gateway.Api.Contracts;

/// <summary>Engages or releases an operator control.</summary>
public sealed record ControlBody(bool Enabled);
