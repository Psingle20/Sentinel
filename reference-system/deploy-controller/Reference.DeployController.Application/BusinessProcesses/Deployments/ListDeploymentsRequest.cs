using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.DeployController.Domain.Revisions;

namespace Reference.DeployController.Application.BusinessProcesses.Deployments;

/// <summary>The deployments started or finished in a window, optionally of one service.</summary>
public sealed record ListDeploymentsRequest(DateTimeOffset? Since, DateTimeOffset? Until, string? Service) : IRequest<IReadOnlyList<DeploymentRecord>>;
