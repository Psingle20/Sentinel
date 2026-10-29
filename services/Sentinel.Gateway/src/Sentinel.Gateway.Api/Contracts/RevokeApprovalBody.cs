namespace Sentinel.Gateway.Api.Contracts;

/// <summary>A revocation of the approval of one plan version, with the reason.</summary>
public sealed record RevokeApprovalBody(int PlanVersion, string Reason);
