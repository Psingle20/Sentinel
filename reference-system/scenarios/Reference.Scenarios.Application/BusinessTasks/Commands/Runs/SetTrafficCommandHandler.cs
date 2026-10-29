using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.Abstractions;
using Reference.Scenarios.Application.Traffic;

namespace Reference.Scenarios.Application.BusinessTasks.Commands.Runs;

/// <summary>Applies the new rate or pause and answers with the settings in force.</summary>
public sealed class SetTrafficCommandHandler(ITraffic traffic) : ICommandHandler<SetTrafficCommand, TrafficSettings>
{
    public Task<Result<TrafficSettings>> Handle(SetTrafficCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        traffic.Update(new TrafficSettings(command.RequestsPerSecond, command.Paused));
        return Task.FromResult(Result.Success(traffic.Settings));
    }
}
