using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Remediation;

/// <summary>Fixed values of the approval attestation: a compact ES256 JWS signed by Api (ADR-006).</summary>
public static class ApprovalAttestation
{
    public const string Algorithm = "ES256";
    public const string TokenType = "sentinel-approval+jws";
    public const string Issuer = "sentinel-api";
    public const string Audience = "sentinel-executor";
    public const string ExecutePlanScope = "execute-plan";

    /// <summary>Where Api publishes its verification keys.</summary>
    public const string KeysPath = "/.well-known/sentinel/approval-keys";
}
