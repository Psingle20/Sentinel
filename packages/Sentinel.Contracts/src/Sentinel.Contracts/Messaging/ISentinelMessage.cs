using System.Text.Json;
using System.Text.Json.Serialization;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Messaging;

/// <summary>
/// Implemented by every integration message. The static members are the registration a
/// service passes to <c>Produces</c> or <c>Consumes</c> (ADR-012); the instance members carry
/// the identity every Sentinel message must have.
/// </summary>
public interface ISentinelMessage
{
    static abstract string MessageType { get; }

    static abstract int SchemaVersion { get; }

    static abstract string Stream { get; }

    TenantId TenantId { get; }

    EnvironmentId EnvironmentId { get; }
}
