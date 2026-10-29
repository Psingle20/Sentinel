using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Remediation;

public sealed record ApprovalKeySet([property: JsonPropertyName("keys")] IReadOnlyList<ApprovalVerificationKey> Keys);
