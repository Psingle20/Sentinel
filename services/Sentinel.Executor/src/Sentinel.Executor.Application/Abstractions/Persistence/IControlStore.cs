using System.Security.Cryptography;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Domain.Capabilities;
using Sentinel.Executor.Domain.Executions;
using Sentinel.Executor.Domain.Policy;

namespace Sentinel.Executor.Application.Abstractions.Persistence;

/// <summary>Operator controls: the emergency stop and per-action disable switches, persisted apart from any model path.</summary>
public interface IControlStore
{
    Task<ExecutorControls> GetAsync(CancellationToken cancellationToken);

    Task SetEmergencyStopAsync(bool engaged, string actor, CancellationToken cancellationToken);

    Task SetActionDisabledAsync(string actionKind, bool disabled, string actor, CancellationToken cancellationToken);
}
