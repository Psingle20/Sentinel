using System.Security.Cryptography;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Domain.Capabilities;
using Sentinel.Executor.Domain.Executions;
using Sentinel.Executor.Domain.Policy;

namespace Sentinel.Executor.Application.Abstractions.Persistence;

/// <summary>
/// The versioned remediation policy document (policy as code). The repository's policy file is the
/// seed; versions published through the control plane (Phase 08) take precedence, newest first.
/// </summary>
public interface IPolicyStore
{
    Task<PolicyDocument> CurrentAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PolicyVersionInfo>> HistoryAsync(CancellationToken cancellationToken);

    /// <summary>Stores a validated version. False when the version already exists.</summary>
    Task<bool> AppendAsync(PolicyDocument document, string author, DateTimeOffset publishedAt, CancellationToken cancellationToken);
}
