using CleanArchitecture.BuildingBlocks.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Configuration;

/// <summary>
/// Loads the current version at startup and then on an interval, so every instance converges on a
/// publish made through any of them. A failed load keeps the version already in effect.
/// </summary>
internal sealed partial class ManagedDocumentRefresher<TDocument>(
    IServiceScopeFactory scopes,
    ManagedDocument<TDocument> document,
    TimeSpan interval,
    ILogger<ManagedDocumentRefresher<TDocument>> logger) : BackgroundService
    where TDocument : class
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // Load before the host starts serving, so the first request already sees the published version.
        await RefreshAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshAsync(stoppingToken);
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            IVersionedDocumentStore<TDocument> store = scope.ServiceProvider.GetRequiredService<IVersionedDocumentStore<TDocument>>();
            PublishedDocument<TDocument>? current = await store.CurrentAsync(cancellationToken);
            string? before = document.Current?.Version.Version;
            document.Set(current);
            if (current is not null && !string.Equals(before, current.Version.Version, StringComparison.Ordinal))
            {
                LogApplied(typeof(TDocument).Name, current.Version.Version, current.Version.Author);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogRefreshFailed(exception, typeof(TDocument).Name, document.Current?.Version.Version ?? "the deployment configuration");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied control-plane {Document} version {Version}, published by {Author}")]
    private partial void LogApplied(string document, string version, string author);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The control-plane {Document} could not be refreshed; keeping {Version}")]
    private partial void LogRefreshFailed(Exception exception, string document, string version);
}
