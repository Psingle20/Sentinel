using System.Security.Cryptography;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Domain.Capabilities;
using Sentinel.Executor.Domain.Executions;
using Sentinel.Executor.Domain.Policy;

namespace Sentinel.Executor.Application.Abstractions.Security;

/// <summary>Resolves Api's approval verification keys by key ID (ADR-006). Executor holds no signing key.</summary>
public interface IApprovalKeyResolver
{
    Task<ECDsa?> ResolveAsync(string keyId, CancellationToken cancellationToken);
}
