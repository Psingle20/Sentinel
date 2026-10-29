using System.Text.Json.Serialization;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Investigations;

public enum EvidenceDirection
{
    Supports,
    Contradicts,
    Neutral
}
