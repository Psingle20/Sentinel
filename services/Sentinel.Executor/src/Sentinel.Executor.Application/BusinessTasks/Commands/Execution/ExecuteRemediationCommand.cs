using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Executor.Application.BusinessTasks.Commands.Execution;

public sealed record ExecuteRemediationCommand(ExecuteRemediation Message) : ICommand;