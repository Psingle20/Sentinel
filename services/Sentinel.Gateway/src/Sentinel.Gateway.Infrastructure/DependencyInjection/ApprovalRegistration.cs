using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.Gateway.Application.Abstractions.Persistence;
using Sentinel.Gateway.Application.Abstractions.Security;
using Sentinel.Gateway.Application.Options;
using Sentinel.Gateway.Infrastructure.Persistence;
using Sentinel.Gateway.Infrastructure.Security;

namespace Sentinel.Gateway.Infrastructure.DependencyInjection;

internal static class ApprovalRegistration
{
    /// <summary>Approval capture (ADR-006): its options, the signer and the approval store.</summary>
    public static IServiceCollection AddApprovals(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ApprovalOptions>().Bind(configuration.GetSection(ApprovalOptions.Section));
        services.AddOptions<SigningOptions>().Bind(configuration.GetSection(SigningOptions.Section));
        services.AddSingleton<IApprovalSigner, LocalApprovalSigner>();
        services.AddScoped<IApprovalStore, PostgresApprovalStore>();
        return services;
    }
}