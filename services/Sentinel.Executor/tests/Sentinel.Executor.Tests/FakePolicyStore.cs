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

/// <summary>The reference policy as the host ships it, replaceable per test.</summary>
internal sealed class FakePolicyStore : IPolicyStore
{
    public PolicyDocument Current { get; set; } = Reference();

    public List<PolicyVersionInfo> Published { get; } = [];

    public Task<PolicyDocument> CurrentAsync(CancellationToken cancellationToken) => Task.FromResult(Current);

    public Task<IReadOnlyList<PolicyVersionInfo>> HistoryAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PolicyVersionInfo>>(Published);

    public Task<bool> AppendAsync(PolicyDocument document, string author, DateTimeOffset publishedAt, CancellationToken cancellationToken)
    {
        if (Published.Exists(version => version.Version == document.Version))
        {
            return Task.FromResult(false);
        }

        Published.Add(new PolicyVersionInfo(document.Version, author, publishedAt, "control-plane"));
        Current = document;
        return Task.FromResult(true);
    }

    public static PolicyDocument Reference() => new()
    {
        Version = "policy-test",
        DefaultMode = AutonomyMode.ApprovalRequired,
        Rules =
        [
            new PolicyRule { Id = "rollback.reference", Action = "rollback", Environments = ["reference"], Targets = ["checkout-service", "order-service"], MaxRisk = "medium", MinConfidence = 0.5, MaxBlastRadius = 2, Grants = AutonomyMode.ApprovalRequired },
            new PolicyRule { Id = "pause-batch.reference", Action = "pause-batch", Environments = ["reference"], MaxRisk = "low", Grants = AutonomyMode.SuggestionOnly }
        ]
    };
}
