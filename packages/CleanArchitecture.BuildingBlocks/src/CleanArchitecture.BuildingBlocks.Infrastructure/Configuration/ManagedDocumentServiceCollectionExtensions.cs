using CleanArchitecture.BuildingBlocks.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Configuration;

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
