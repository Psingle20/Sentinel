using Microsoft.Extensions.Hosting;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

/// <summary>
/// Opens once every <see cref="IMessagingStoreInitializer"/> has run. The dispatcher and
/// consumers wait on it, so they never poll storage that does not exist yet.
/// </summary>
internal sealed class MessagingStartupGate
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task WaitAsync(CancellationToken cancellationToken) => _ready.Task.WaitAsync(cancellationToken);

    internal void Open() => _ready.TrySetResult();

    internal void Fail(Exception exception) => _ready.TrySetException(exception);
}

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
