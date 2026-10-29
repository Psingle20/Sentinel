using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Executor.Domain.Capabilities;

/// <summary>
/// A scoped, single-use authority to perform one action on one resource (Phase 06). Executor issues
/// it only after Runtime confirmed the start, and the adapter refuses to act without a capability
/// that authorizes exactly its action, resource and scope before it expires. Being single-use, a
/// capability can never authorize a repeat of the action.
/// </summary>
public sealed record Capability
{
    public required Guid CapabilityId { get; init; }

    public required Guid RequestId { get; init; }

    public required string Action { get; init; }

    /// <summary>The target resource, for example a service name.</summary>
    public required string Resource { get; init; }

    /// <summary><c>tenant/environment</c>: a capability never crosses its scope.</summary>
    public required string Scope { get; init; }

    /// <summary>Action-specific limits, for example how many revisions a rollback may move.</summary>
    public IReadOnlyDictionary<string, string> Limits { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public required DateTimeOffset IssuedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public static Capability Issue(Guid requestId, string action, string resource, TenantId tenantId, EnvironmentId environmentId, IReadOnlyDictionary<string, string> limits, DateTimeOffset now, TimeSpan lifetime) => new()
    {
        CapabilityId = Guid.CreateVersion7(now),
        RequestId = requestId,
        Action = action,
        Resource = resource,
        Scope = ScopeOf(tenantId, environmentId),
        Limits = limits,
        IssuedAt = now,
        ExpiresAt = now + lifetime
    };

    public static string ScopeOf(TenantId tenantId, EnvironmentId environmentId) => $"{tenantId}/{environmentId}";

    /// <summary>Whether this capability authorizes <paramref name="action"/> on <paramref name="resource"/> in <paramref name="scope"/> now.</summary>
    public Result Authorizes(string action, string resource, string scope, DateTimeOffset now)
    {
        if (action != Action || resource != Resource || scope != Scope)
        {
            return Result.Failure(Error.Problem("Capability.Mismatch", $"Capability {CapabilityId} authorizes {Action} on {Resource} in {Scope}, not {action} on {resource} in {scope}."));
        }

        return now < ExpiresAt
            ? Result.Success()
            : Result.Failure(Error.Problem("Capability.Expired", $"Capability {CapabilityId} expired at {ExpiresAt:O}."));
    }
}
