using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Integration;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application;
using Sentinel.Executor.Domain.Capabilities;
using Sentinel.Executor.Domain.Executions;
using Sentinel.Executor.Domain.Policy;
using Sentinel.Executor.Application.Abstractions.Persistence;
using Sentinel.Executor.Application.Abstractions.Providers;
using Sentinel.Executor.Application.Abstractions.Remediation;
using Sentinel.Executor.Application.Abstractions.Security;

namespace Sentinel.Executor.Tests;

internal sealed class FakeControlStore : IControlStore
{
    public ExecutorControls Controls { get; set; } = ExecutorControls.None;

    public Task<ExecutorControls> GetAsync(CancellationToken cancellationToken) => Task.FromResult(Controls);

    public Task SetEmergencyStopAsync(bool engaged, string actor, CancellationToken cancellationToken)
    {
        Controls = Controls with { EmergencyStop = engaged };
        return Task.CompletedTask;
    }

    public Task SetActionDisabledAsync(string actionKind, bool disabled, string actor, CancellationToken cancellationToken)
    {
        HashSet<string> set = [.. Controls.DisabledActions];
        if (disabled)
        {
            set.Add(actionKind);
        }
        else
        {
            set.Remove(actionKind);
        }

        Controls = Controls with { DisabledActions = set };
        return Task.CompletedTask;
    }
}
