using CleanArchitecture.BuildingBlocks.Messaging;

namespace Sentinel.Runtime.Application.BusinessTasks.Commands.Projections;

public sealed record RebuildProjectionsCommand : ICommand<int>;
