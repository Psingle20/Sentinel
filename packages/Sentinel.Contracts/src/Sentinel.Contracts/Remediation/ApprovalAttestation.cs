using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Remediation;

/// <summary>
/// Fixed values of the attestations the Gateway signs (ADR-006): compact ES256 JWS tokens. An
/// approval attestation binds one decision to one plan version; an operator attestation binds one
/// control-plane action to its content. Issuer and audiences are durable identities and did not
/// change with the service rename (ADR-020).
/// </summary>
public static class ApprovalAttestation
{
    public const string Algorithm = "ES256";
    public const string TokenType = "sentinel-approval+jws";
    public const string OperatorTokenType = "sentinel-operator+jws";
    public const string Issuer = "sentinel-api";

    /// <summary>The audience of an approval: Executor verifies it before any side effect.</summary>
    public const string Audience = "sentinel-executor";

    /// <summary>The audience of a rejection, a revocation and every decision Runtime records.</summary>
    public const string RuntimeAudience = "sentinel-runtime";

    public const string ExecutePlanScope = "execute-plan";
    public const string RejectPlanScope = "reject-plan";
    public const string RevokeApprovalScope = "revoke-approval";

    /// <summary>Operator scopes: what a control-plane attestation permits at the owning service.</summary>
    public const string PublishPolicyScope = "publish-policy";
    public const string SetControlScope = "set-control";

    /// <summary>The header an operator attestation travels in, from the Gateway to the owning service.</summary>
    public const string OperatorHeader = "X-Sentinel-Operator-Attestation";

    /// <summary>Where the Gateway publishes its verification keys.</summary>
    public const string KeysPath = "/.well-known/sentinel/approval-keys";
}
