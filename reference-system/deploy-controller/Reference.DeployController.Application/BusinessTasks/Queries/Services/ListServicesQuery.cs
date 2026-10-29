using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.DeployController.Application.BusinessProcesses.Services;

namespace Reference.DeployController.Application.BusinessTasks.Queries.Services;

public sealed record ListServicesQuery : IQuery<IReadOnlyList<ServiceView>>;
