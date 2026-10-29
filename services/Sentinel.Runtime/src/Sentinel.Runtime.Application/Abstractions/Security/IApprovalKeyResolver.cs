using System.Security.Cryptography;

namespace Sentinel.Runtime.Application.Abstractions.Security;

/// <summary>The gateway's published approval verification keys (ADR-006), by key ID; null for a key that is not published or not valid now.</summary>
public interface IApprovalKeyResolver
{
    Task<ECDsa?> ResolveAsync(string keyId, CancellationToken cancellationToken);
}
