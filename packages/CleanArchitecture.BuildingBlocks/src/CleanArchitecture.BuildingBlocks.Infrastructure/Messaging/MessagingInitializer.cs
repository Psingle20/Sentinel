using Microsoft.Extensions.Hosting;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

internal sealed class MessagingInitializer(
    IEnumerable<IMessagingStoreInitializer> initializers,
    MessagingStartupGate gate) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            foreach (IMessagingStoreInitializer initializer in initializers)
            {
                await initializer.InitializeAsync(cancellationToken);
            }

            gate.Open();
        }
        catch (Exception exception)
        {
            gate.Fail(exception);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
