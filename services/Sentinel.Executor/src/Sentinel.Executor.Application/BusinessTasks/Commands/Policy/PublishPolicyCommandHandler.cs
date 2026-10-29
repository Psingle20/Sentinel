using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Executor.Application.Abstractions.Persistence;
using Sentinel.Executor.Domain.Policy;

namespace Sentinel.Executor.Application.BusinessTasks.Commands.Policy;

/// <summary>
/// Publishes a policy version (Phase 08): valid, a new version, over the version the operator
/// started from, stored with its author, and effective for the next evaluation.
/// </summary>
public sealed class PublishPolicyCommandHandler(IPolicyStore policies, TimeProvider time) : ICommandHandler<PublishPolicyCommand, PolicyDocument>
{
    public async Task<Result<PolicyDocument>> Handle(PublishPolicyCommand command, CancellationToken cancellationToken)
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

        // The check above answers most conflicts early; the store's compare-and-append decides a race between two publishers.
        return await policies.AppendAsync(command.Document, current.Version, command.Author, time.GetUtcNow(), cancellationToken)
            ? Result.Success(command.Document)
            : Result.Failure<PolicyDocument>(PolicyErrors.VersionConflict);
    }
}
