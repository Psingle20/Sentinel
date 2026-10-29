namespace Sentinel.Contracts.Remediation;

/// <summary>A token split into its protected header, its claims, the bytes the signature covers and the signature; nothing here is verified yet.</summary>
public sealed record DecodedAttestation<TClaims>(ApprovalAttestationHeader Header, TClaims Claims, byte[] SignedInput, byte[] Signature)
    where TClaims : class;
