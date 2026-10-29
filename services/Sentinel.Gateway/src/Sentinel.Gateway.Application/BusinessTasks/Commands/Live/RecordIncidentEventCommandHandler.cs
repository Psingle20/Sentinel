using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.Extensions.Options;
using Sentinel.Gateway.Application.Abstractions.Persistence;
using Sentinel.Gateway.Application.Live;
using Sentinel.Gateway.Application.Options;

namespace Sentinel.Gateway.Application.BusinessTasks.Commands.Live;

/// <summary>Records one lifecycle message as a live event and enqueues the notifications it routes to, in the inbox's transaction.</summary>
public sealed class RecordIncidentEventCommandHandler(ILiveEventStore events, INotificationStore notifications, IOptions<NotificationOptions> options, TimeProvider time) : ICommandHandler<RecordIncidentEventCommand>
{
    public async Task<Result> Handle(RecordIncidentEventCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await events.AppendAsync(command.Draft, cancellationToken);
        foreach (NotificationMessage message in NotificationRouter.Route(command.Draft, options.Value, () => Guid.CreateVersion7(time.GetUtcNow())))
        {
            await notifications.EnqueueAsync(message, cancellationToken);
        }

        return Result.Success();
    }
}
