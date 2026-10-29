using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Executor.Domain.Policy;

namespace Sentinel.Executor.Application.BusinessTasks.Queries.Controls;

public sealed record GetControlsQuery : IQuery<ExecutorControls>;
