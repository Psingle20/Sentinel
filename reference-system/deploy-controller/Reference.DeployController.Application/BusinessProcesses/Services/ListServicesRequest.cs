using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.DeployController.Application.BusinessProcesses.Services;

namespace Reference.DeployController.Application.BusinessProcesses.Services;

/// <summary>Every managed service with the revision it serves and its rollout in progress.</summary>
public sealed record ListServicesRequest : IRequest<IReadOnlyList<ServiceView>>;
