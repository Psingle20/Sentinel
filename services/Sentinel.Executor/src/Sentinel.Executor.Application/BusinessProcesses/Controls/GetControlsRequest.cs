using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Executor.Domain.Policy;

namespace Sentinel.Executor.Application.BusinessProcesses.Controls;

/// <summary>The operator controls: the emergency stop and the disabled action kinds.</summary>
public sealed record GetControlsRequest : IRequest<ExecutorControls>;
