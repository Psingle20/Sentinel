using System.Security.Cryptography;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Domain.Capabilities;
using Sentinel.Executor.Domain.Executions;
using Sentinel.Executor.Domain.Policy;

namespace Sentinel.Executor.Application.Abstractions.Providers;

/// <summary>Runtime's start confirmation. Throws when Runtime cannot be reached, which fails the execution closed.</summary>
public interface IStartConfirmation
{
    Task<RemediationStartResponse> ConfirmAsync(RemediationStartRequest request, CancellationToken cancellationToken);
}
