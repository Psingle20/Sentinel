using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.Traffic;

namespace Reference.Scenarios.Application.BusinessProcesses.Traffic;

/// <summary>The traffic the reference system runs under now.</summary>
public sealed record GetTrafficRequest : IRequest<TrafficSettings>;
