using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sentinel.AgentWorker.Application.Abstractions.Providers;
using Sentinel.AgentWorker.Application.Capture;
using Sentinel.AgentWorker.Application.Options;
using Sentinel.AgentWorker.Application.Tools;
using Sentinel.AgentWorker.Infrastructure.Clients;

namespace Sentinel.AgentWorker.Infrastructure.DependencyInjection;

internal static class ClientRegistration
{
    /// <summary>
    /// Ingestion's query and knowledge contracts, the only way AgentWorker reads anything: it holds no
    /// provider credentials (ADR-007). Each read waits for complete data where it can and passes
    /// through the scope's capture, so it is recorded with the execution and served from that record
    /// when the execution is replayed (Phase 07).
    /// </summary>
    public static IServiceCollection AddAgentWorkerClients(this IServiceCollection services, IConfiguration configuration)
    {
        Uri ingestion = new(configuration["AgentWorker:Services:Ingestion"] ?? throw new InvalidOperationException("AgentWorker:Services:Ingestion is not configured."));
        services.AddHttpClient<IngestionQueryClient>(client => client.BaseAddress = ingestion);
        services.AddHttpClient<IngestionKnowledgeClient>(client => client.BaseAddress = ingestion);
        services.AddScoped<IObservationQueries>(provider => new CapturingObservationQueries(
            new PatientObservationQueries(
                provider.GetRequiredService<IngestionQueryClient>(),
                provider.GetRequiredService<IOptions<CompletenessOptions>>(),
                provider.GetRequiredService<TimeProvider>()),
            provider.GetRequiredService<InvestigationCapture>()));
        services.AddScoped<IKnowledgeQueries>(provider => new CapturingKnowledgeQueries(provider.GetRequiredService<IngestionKnowledgeClient>(), provider.GetRequiredService<InvestigationCapture>()));
        return services;
    }
}
