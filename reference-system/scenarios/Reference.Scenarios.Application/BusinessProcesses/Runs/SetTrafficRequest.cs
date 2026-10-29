using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.Traffic;

namespace Reference.Scenarios.Application.BusinessProcesses.Runs;

/// <summary>Changes the rate of the seeded checkout traffic, or pauses it.</summary>
public sealed record SetTrafficRequest(double RequestsPerSecond, bool Paused) : IRequest<TrafficSettings>;
