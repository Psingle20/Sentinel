namespace Sentinel.Gateway.Api.Contracts;

/// <summary>A rejection of one plan version, with the reason.</summary>
public sealed record RejectPlanBody(int PlanVersion, string Reason);