using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Remediation;

/// <summary>One verification key in JWK form; Executor resolves it by <see cref="KeyId"/>.</summary>
public sealed record ApprovalVerificationKey(
    [property: JsonPropertyName("kid")] string KeyId,
    [property: JsonPropertyName("kty")] string KeyType,
    [property: JsonPropertyName("crv")] string Curve,
    [property: JsonPropertyName("x")] string X,
    [property: JsonPropertyName("y")] string Y,
    [property: JsonPropertyName("alg")] string Algorithm,
    [property: JsonPropertyName("use")] string Use,
    [property: JsonPropertyName("nbf")] long NotBefore,
    [property: JsonPropertyName("exp")] long? Expires);
