using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.AgentWorker.Application.Abstractions.Providers;
using Sentinel.AgentWorker.Application.Capture;
using Sentinel.AgentWorker.Application.Options;
using Sentinel.AgentWorker.Infrastructure.Models;

namespace Sentinel.AgentWorker.Infrastructure.DependencyInjection;

internal static class ModelRegistration
{
    /// <summary>The client every Meta profile shares; model calls outlast the standard resilience timeouts.</summary>
    public const string MetaClient = "meta-model-api";

    /// <summary>
    /// The model providers and the router between them. The test double is always available; each Meta profile is a
    /// provider of its own, registered only when the Meta connection is configured or a role routes to one (ADR-015).
    /// <c>Model:Provider</c> picks the default, <c>Model:Routing:&lt;role&gt;</c> overrides it per role (Phase 04), and the
    /// control plane can move roles between the providers registered here (Phase 08).
    /// </summary>
    public static IServiceCollection AddAgentWorkerModels(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TestDoubleOptions>().Bind(configuration.GetSection("Model:TestDouble"));
        services.AddSingleton<IModelProvider, DeterministicModelProvider>();
        string provider = configuration["Model:Provider"] ?? ModelRouter.TestDouble;
        IEnumerable<string> routed = configuration.GetSection("Model:Routing").GetChildren().Select(route => route.Value ?? string.Empty)
            .Prepend(provider)
            .Append(configuration["Model:Override"] ?? string.Empty);
        MetaModelOptions meta = configuration.GetSection("Model:Meta").Get<MetaModelOptions>() ?? new MetaModelOptions();
        bool metaRouted = routed.Any(name => name.StartsWith(MetaModelProvider.ProviderName, StringComparison.Ordinal));
        if (metaRouted || meta.IsValid())
        {
            services.AddOptions<MetaModelOptions>()
                .Bind(configuration.GetSection("Model:Meta"))
                .Validate(options => options.IsValid(), "Model:Meta needs ChatCompletionsUri, ApiKey (from secret configuration) and at least one valid profile named meta or meta-<name>.")
                .ValidateOnStart();

            // The harness decides about retries itself, so the default handlers are removed for this client.
#pragma warning disable EXTEXP0001
            services.AddHttpClient(MetaClient, client => client.Timeout = Timeout.InfiniteTimeSpan).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
            foreach (string name in meta.EffectiveProfiles().Keys)
            {
                services.AddTransient<IModelProvider>(resolver =>
                {
                    MetaModelOptions connection = resolver.GetRequiredService<IOptions<MetaModelOptions>>().Value;
                    return new MetaModelProvider(
                        resolver.GetRequiredService<IHttpClientFactory>().CreateClient(MetaClient),
                        name,
                        connection.EffectiveProfiles()[name],
                        connection,
                        resolver.GetRequiredService<ILogger<MetaModelProvider>>());
                });
            }
        }
        else if (provider != ModelRouter.TestDouble)
        {
            throw new InvalidOperationException($"Model:Provider '{provider}' is not supported; use {ModelRouter.TestDouble} or a Meta profile.");
        }

        services.AddScoped<IModelRouter>(services => new ModelRouter(services.GetServices<IModelProvider>(), services.GetRequiredService<IOptions<ModelRoutingOptions>>(), services.GetRequiredService<InvestigationCapture>()));
        return services;
    }
}
