using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.DeployController.Domain.Revisions;

namespace Reference.DeployController.Application.BusinessTasks.Commands.Deployments;

public sealed record ResetServicesCommand : ICommand<IReadOnlyList<DeploymentRecord>>;
