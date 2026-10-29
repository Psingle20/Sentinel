using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.DeployController.Application.BusinessProcesses.Services;

namespace Reference.DeployController.Application.BusinessProcesses.Services;

/// <summary>One managed service; nothing when the controller does not manage it.</summary>
public sealed record GetServiceRequest(string Name) : IRequest<ServiceView?>;
