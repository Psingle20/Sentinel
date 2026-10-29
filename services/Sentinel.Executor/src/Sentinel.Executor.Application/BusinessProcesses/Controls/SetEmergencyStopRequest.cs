using CleanArchitecture.BuildingBlocks.Messaging;

namespace Sentinel.Executor.Application.BusinessProcesses.Controls;

/// <summary>An operator engages or releases the emergency stop, which denies every action (ADR-006).</summary>
public sealed record SetEmergencyStopRequest(bool Engaged, string Actor) : IRequest;
