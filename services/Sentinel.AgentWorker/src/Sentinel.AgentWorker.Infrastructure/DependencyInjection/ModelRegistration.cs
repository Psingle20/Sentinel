using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sentinel.AgentWorker.Application.Abstractions.Providers;
using Sentinel.AgentWorker.Application.Capture;
using Sentinel.AgentWorker.Application.Options;
using Sentinel.AgentWorker.Infrastructure.Models;

namespace Sentinel.AgentWorker.Infrastructure.DependencyInjection;

internal static class ModelRegistration
{
    /// <summary>
    /// The model providers and the router between them. The test double is always available; the Meta
    /// provider only when configured (ADR-015). <c>Model:Provider</c> picks the default,
    /// <c>Model:Routing:&lt;role&gt;</c> overrides it per role (Phase 04), and the control plane can move
    /// roles between the providers registered here (Phase 08).
    /// </summary>
    public static IServiceCollection AddAgentWorkerModels(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TestDoubleOptions>().Bind(configuration.GetSection("Model:TestDouble"));
        services.AddSingleton<IModelProvider, DeterministicModelProvider>();
        string provider = configuration["Model:Provider"] ?? ModelRouter.TestDouble;
        bool metaRouted = provider == MetaModelProvider.ProviderName
            || configuration.GetSection("Model:Routing").GetChildren().Any(route => route.Value == MetaModelProvider.ProviderName);
        bool metaConfigured = configuration.GetSection("Model:Meta").Get<MetaModelOptions>()?.IsValid() == true;
        if (metaRouted || metaConfigured)
        {
            services.AddOptions<MetaModelOptions>()
                .Bind(configuration.GetSection("Model:Meta"))
                .Validate(options => options.IsValid(), "Model:Meta needs ChatCompletionsUri, Model and ApiKey (from secret configuration).")
                .ValidateOnStart();

            // Model calls run longer than the standard resilience timeouts allow, and the harness
            // decides about retries itself, so the default handlers are removed for this client.
#pragma warning disable EXTEXP0001
            services.AddHttpClient<MetaModelProvider>(client => client.Timeout = Timeout.InfiniteTimeSpan)
                .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
            services.AddTransient<IModelProvider>(services => services.GetRequiredService<MetaModelProvider>());
        }
        else if (provider != ModelRouter.TestDouble)
        {
            throw new InvalidOperationException($"Model:Provider '{provider}' is not supported; use {ModelRouter.TestDouble} or {MetaModelProvider.ProviderName}.");
        }

        services.AddScoped<IModelRouter>(services => new ModelRouter(services.GetServices<IModelProvider>(), services.GetRequiredService<IOptions<ModelRoutingOptions>>(), services.GetRequiredService<InvestigationCapture>()));
        return services;
    }
}
