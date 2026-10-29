namespace Sentinel.Contracts.Remediation;

/// <summary>
/// Runtime's authoritative answer to a revocation (ADR-006): accepted, and the attestation can never
/// take effect; or refused with the reason, which is final, for example because execution started.
/// </summary>
public sealed record RevocationResponse(bool Accepted, string? Reason);
