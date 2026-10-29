using CleanArchitecture.BuildingBlocks;
using Sentinel.Executor.Domain.Policy;

namespace Sentinel.Executor.Application.BusinessTasks.Commands.Policy;

/// <summary>Why a policy version was not published, with stable codes.</summary>
public static class PolicyErrors
{
    public static Error Invalid(IReadOnlyList<string> problems) => Error.Problem("Policy.Invalid", string.Join(" ", problems));

    public static readonly Error VersionConflict = Error.Conflict("Policy.VersionConflict", "The policy changed since it was read; reload it and apply the change again.");

    public static readonly Error VersionUnchanged = Error.Problem("Policy.VersionUnchanged", "A published policy needs a new version.");
}
