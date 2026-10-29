using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.Traffic;

namespace Reference.Scenarios.Application.BusinessTasks.Commands.Runs;

public sealed record SetTrafficCommand(double RequestsPerSecond, bool Paused) : ICommand<TrafficSettings>;
