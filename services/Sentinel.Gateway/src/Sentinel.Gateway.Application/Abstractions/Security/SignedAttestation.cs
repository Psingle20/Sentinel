namespace Sentinel.Gateway.Application.Abstractions.Security;

/// <summary>A signed approval attestation and the ID of the key that signed it.</summary>
public sealed record SignedAttestation(string Token, string KeyId);
