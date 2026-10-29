using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Remediation;

/// <summary>The JWS protected header.</summary>
public sealed record ApprovalAttestationHeader(
    [property: JsonPropertyName("alg")] string Algorithm,
    [property: JsonPropertyName("kid")] string KeyId,
    [property: JsonPropertyName("typ")] string Type);
