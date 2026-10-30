using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.Abstractions;

namespace Reference.Scenarios.Application.BusinessTasks.Queries.Environments;

public sealed record GetEnvironmentQuery : IQuery<EnvironmentState>;
