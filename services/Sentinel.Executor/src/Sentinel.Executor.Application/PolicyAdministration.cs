using CleanArchitecture.BuildingBlocks;
using Sentinel.Executor.Domain.Policy;

namespace Sentinel.Executor.Application;

public sealed record PublishPolicy(PolicyDocument Document, string Author, string ExpectedVersion);

public static class PolicyErrors
{
    public static Error Invalid(IReadOnlyList<string> problems) => Error.Problem("Policy.Invalid", string.Join(" ", problems));

    public static readonly Error VersionConflict = Error.Conflict("Policy.VersionConflict", "The policy changed since it was read; reload it and apply the change again.");

    public static readonly Error VersionUnchanged = Error.Problem("Policy.VersionUnchanged", "A published policy needs a new version.");
}

/// <summary>
/// Publishes a new policy version (Phase 08 control plane): validated, checked against the version
/// the operator started from, stored with its author, and effective for the next evaluation.
/// </summary>
public sealed class PolicyAdministration(IPolicyStore policies, TimeProvider time)
{
    public async Task<Result<PolicyDocument>> PublishAsync(PublishPolicy command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        IReadOnlyList<string> problems = PolicyValidator.Problems(command.Document);
        if (problems.Count > 0)
        {
            return Result.Failure<PolicyDocument>(PolicyErrors.Invalid(problems));
        }

        PolicyDocument current = await policies.CurrentAsync(cancellationToken);
        if (current.Version != command.ExpectedVersion)
        {
            return Result.Failure<PolicyDocument>(PolicyErrors.VersionConflict);
        }

        if (command.Document.Version == current.Version)
        {
            return Result.Failure<PolicyDocument>(PolicyErrors.VersionUnchanged);
        }

        return await policies.AppendAsync(command.Document, command.Author, time.GetUtcNow(), cancellationToken)
            ? Result.Success(command.Document)
            : Result.Failure<PolicyDocument>(PolicyErrors.VersionConflict);
    }
}
