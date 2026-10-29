using CleanArchitecture.BuildingBlocks.Messaging;

namespace Reference.DeployController.Application.BusinessTasks.Commands.Deployments;

public sealed record CompleteRolloutCommand(string DeploymentId) : ICommand;
