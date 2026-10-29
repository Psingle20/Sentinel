using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.Executor.Application.Abstractions.Providers;
using Sentinel.Executor.Application.Abstractions.Security;
using Sentinel.Executor.Infrastructure.Clients;

namespace Sentinel.Executor.Infrastructure.DependencyInjection;

internal static class ClientRegistration
{
    /// <summary>The Gateway's published approval keys and Runtime's start confirmation, by service-discovery name (ADR-010).</summary>
    public static IServiceCollection AddExecutorClients(this IServiceCollection services, IConfiguration configuration)
    {
        Uri gateway = ConfigurationValues.RequiredUri(configuration, "Executor:Services:Gateway");
        Uri runtime = ConfigurationValues.RequiredUri(configuration, "Executor:Services:Runtime");

        // A singleton, so verified keys stay cached across requests.
        services.AddHttpClient(ApiApprovalKeys.ClientName, client => client.BaseAddress = gateway);
        services.AddSingleton<IApprovalKeyResolver, ApiApprovalKeys>();

        services.AddHttpClient<IStartConfirmation, RuntimeStartConfirmation>(client => client.BaseAddress = runtime);
        return services;
    }
}
