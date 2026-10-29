using CleanArchitecture.BuildingBlocks.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Configuration;

/// <summary>
/// The current published version of a control-plane document, refreshed from its
/// <see cref="IVersionedDocumentStore{TDocument}"/>. Until a version is published the service runs
/// on its deployment configuration; afterwards the published document is applied over it.
/// </summary>
public sealed class ManagedDocument<TDocument>
    where TDocument : class
{
    private readonly Lock _gate = new();
    private CancellationTokenSource _changed = new();

    /// <summary>The newest published version this instance has seen, or <see langword="null"/>.</summary>
    public PublishedDocument<TDocument>? Current { get; private set; }

    /// <summary>Fires once when <see cref="Current"/> changes to another version.</summary>
    public IChangeToken GetChangeToken()
    {
        lock (_gate)
        {
            return new CancellationChangeToken(_changed.Token);
        }
    }

    /// <summary>Makes <paramref name="published"/> current; a publish calls this so the change applies without waiting for a refresh.</summary>
    public void Set(PublishedDocument<TDocument>? published)
    {
        CancellationTokenSource previous;
        lock (_gate)
        {
            if (string.Equals(Current?.Version.Version, published?.Version.Version, StringComparison.Ordinal))
            {
                return;
            }

            Current = published;
            previous = _changed;
            _changed = new CancellationTokenSource();
        }

        previous.Cancel();
        previous.Dispose();
    }
}

/// <summary>Registration of control-plane documents applied over a service's options.</summary>
public static class ManagedDocumentServiceCollectionExtensions
{
    /// <summary>
    /// Applies the current <typeparamref name="TDocument"/> over <typeparamref name="TOptions"/> after
    /// the deployment configuration is bound, and recomputes the options whenever a new version is
    /// published. <see cref="IOptions{TOptions}"/> then returns the current value instead of the
    /// value at first use, so consumers that read <c>Value</c> per operation see a change at once.
    /// Requires an <see cref="IVersionedDocumentStore{TDocument}"/> registration.
    /// </summary>
    public static IServiceCollection AddManagedDocument<TOptions, TDocument>(
        this IServiceCollection services,
        Action<TOptions, TDocument> apply,
        TimeSpan? refreshInterval = null)
        where TOptions : class
        where TDocument : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(apply);

        // One document can shape several options types; it is still loaded and refreshed once.
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(ManagedDocument<TDocument>)))
        {
            services.AddSingleton<ManagedDocument<TDocument>>();
            services.AddHostedService(provider => new ManagedDocumentRefresher<TDocument>(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<ManagedDocument<TDocument>>(),
                refreshInterval ?? TimeSpan.FromSeconds(10),
                provider.GetRequiredService<ILogger<ManagedDocumentRefresher<TDocument>>>()));
        }

        services.AddOptions<TOptions>();
        services.AddSingleton<IPostConfigureOptions<TOptions>>(provider => new ManagedDocumentApplier<TOptions, TDocument>(provider.GetRequiredService<ManagedDocument<TDocument>>(), apply));
        services.AddSingleton<IOptionsChangeTokenSource<TOptions>>(provider => new ManagedDocumentChangeTokenSource<TOptions, TDocument>(provider.GetRequiredService<ManagedDocument<TDocument>>()));
        services.Replace(ServiceDescriptor.Singleton<IOptions<TOptions>>(provider => new CurrentOptions<TOptions>(provider.GetRequiredService<IOptionsMonitor<TOptions>>())));
        return services;
    }
}

internal sealed class ManagedDocumentApplier<TOptions, TDocument>(ManagedDocument<TDocument> document, Action<TOptions, TDocument> apply) : IPostConfigureOptions<TOptions>
    where TOptions : class
    where TDocument : class
{
    public void PostConfigure(string? name, TOptions options)
    {
        if (name == Options.DefaultName && document.Current is { } current)
        {
            apply(options, current.Document);
        }
    }
}

internal sealed class ManagedDocumentChangeTokenSource<TOptions, TDocument>(ManagedDocument<TDocument> document) : IOptionsChangeTokenSource<TOptions>
    where TDocument : class
{
    public string? Name => Options.DefaultName;

    public IChangeToken GetChangeToken() => document.GetChangeToken();
}

internal sealed class CurrentOptions<TOptions>(IOptionsMonitor<TOptions> monitor) : IOptions<TOptions>
    where TOptions : class
{
    public TOptions Value => monitor.CurrentValue;
}

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
