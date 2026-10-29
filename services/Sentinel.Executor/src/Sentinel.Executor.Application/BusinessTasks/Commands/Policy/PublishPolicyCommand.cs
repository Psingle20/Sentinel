using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Executor.Domain.Policy;

namespace Sentinel.Executor.Application.BusinessTasks.Commands.Policy;

public sealed record PublishPolicyCommand(PolicyDocument Document, string Author, string ExpectedVersion) : ICommand<PolicyDocument>;
