using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Sentinel.Testing.Composition;

/// <summary>
/// A service host composed exactly as its <c>Program</c> composes it, with the host's own
/// appsettings plus <paramref name="settings"/>, built but never started: no hosted service, silo,
/// workflow worker or consumer runs, so the composition can be inspected without the databases,
/// brokers and peers it would connect to (ADR-020 composition tests). The test project must reference
/// <c>Microsoft.AspNetCore.Mvc.Testing</c> itself, which locates the host's content root.
/// </summary>
public sealed class HostComposition<TProgram> : IDisposable
    where TProgram : class
{
    private readonly Factory _factory;

    public HostComposition(IReadOnlyDictionary<string, string?> settings, string environment = "Development")
    {
        ArgumentNullException.ThrowIfNull(settings);

        _factory = new Factory(settings, environment);
        Provider = _factory.Services;
        Services = _factory.Captured ?? throw new InvalidOperationException("The host's service collection was not captured.");
    }

    /// <summary>Every registration the host made, in order.</summary>
    public IServiceCollection Services { get; }

    /// <summary>The host's root provider, built with the host's own validation settings.</summary>
    public IServiceProvider Provider { get; }

    public void Dispose() => _factory.Dispose();

    private sealed class Factory(IReadOnlyDictionary<string, string?> settings, string environment) : WebApplicationFactory<TProgram>
    {
        public IServiceCollection? Captured { get; private set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            foreach ((string key, string? value) in settings)
            {
                builder.UseSetting(key, value);
            }

            // Runs after the host's own registrations. The copy is what the host registered; the
            // hosted services then leave the live collection, because Program starts the host itself
            // when it reaches RunAsync, and a started worker would reach for its database or broker.
            builder.ConfigureTestServices(services =>
            {
                IServiceCollection registered = new ServiceCollection();
                foreach (ServiceDescriptor descriptor in services)
                {
                    registered.Add(descriptor);
                }

                Captured = registered;
                foreach (ServiceDescriptor hosted in services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService)).ToList())
                {
                    services.Remove(hosted);
                }
            });
        }

        // Built, not started.
        protected override IHost CreateHost(IHostBuilder builder) => builder.Build();
    }
}
