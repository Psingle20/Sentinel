namespace Sentinel.Gateway.Api.Contracts;

/// <summary>The plan version and content hash the approver reviewed.</summary>
public sealed record ApprovePlanBody(int PlanVersion, string PlanHash);