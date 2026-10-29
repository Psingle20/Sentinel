using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.DeployController.Domain.Revisions;

namespace Reference.DeployController.Application.BusinessProcesses.Deployments;

/// <summary>Returns every managed service to its baseline revision and clears every injected fault.</summary>
public sealed record ResetServicesRequest : IRequest<IReadOnlyList<DeploymentRecord>>;
