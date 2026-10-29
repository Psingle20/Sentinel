using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.Traffic;

namespace Reference.Scenarios.Application.BusinessTasks.Queries.Traffic;

public sealed record GetTrafficQuery : IQuery<TrafficSettings>;
