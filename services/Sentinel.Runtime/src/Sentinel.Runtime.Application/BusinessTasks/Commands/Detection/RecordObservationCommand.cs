using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Observations;

namespace Sentinel.Runtime.Application.BusinessTasks.Commands.Detection;

public sealed record RecordObservationCommand(Observation Observation) : ICommand;
