using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Serialization;
using Sentinel.Contracts.Messaging;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Infrastructure.Grains;

namespace Sentinel.Runtime.Infrastructure.DependencyInjection;

public static class ActorRegistration
{
    /// <summary>
    /// Hosts Runtime's grains (ADR-004). Membership is kept in the messaging Redis, so Runtime
    /// instances find each other without another component; <c>Runtime:Orleans:Clustering=localhost</c>
    /// runs a single development silo without it. Grain-call payloads use the contract JSON rules.
    /// </summary>
    public static IHostApplicationBuilder AddRuntimeActors(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        IConfiguration configuration = builder.Configuration;
        builder.UseOrleans(silo =>
        {
            if (configuration["Runtime:Orleans:Clustering"] == "localhost")
            {
                silo.UseLocalhostClustering();
            }
            else
            {
                silo.UseRedisClustering(configuration.GetConnectionString("messaging")
                    ?? throw new InvalidOperationException("The messaging connection string is not configured."));
                silo.ConfigureEndpoints(
                    IPAddress.Loopback,
                    configuration.GetValue("Runtime:Orleans:SiloPort", 11111),
                    configuration.GetValue("Runtime:Orleans:GatewayPort", 30000));
            }

            silo.Configure<ClusterOptions>(options =>
            {
                options.ClusterId = configuration["Runtime:Orleans:ClusterId"] ?? "sentinel-runtime";
                options.ServiceId = "sentinel-runtime";
            });
            silo.Configure<SiloMessagingOptions>(options => options.ResponseTimeout = TimeSpan.FromMinutes(2));
            silo.Services.AddRuntimeGrainSerialization();
        });
        return builder;
    }

    /// <summary>Grain-call payloads are Sentinel types, serialized with the contract JSON rules (ADR-012).</summary>
    public static IServiceCollection AddRuntimeGrainSerialization(this IServiceCollection services) =>
        services.AddSerializer(serializer => serializer.AddJsonSerializer(
            isSupported: type => type.Namespace?.StartsWith("Sentinel.", StringComparison.Ordinal) == true,
            jsonSerializerOptions: SentinelJson.Options));

    /// <summary>The single writers and hot caches are grains (ADR-004).</summary>
    internal static IServiceCollection AddRuntimeGrainPorts(this IServiceCollection services)
    {
        services.AddSingleton<IIncidentOwner, GrainIncidentOwner>();
        services.AddSingleton<ICorrelationPartitions, GrainCorrelationPartitions>();
        services.AddSingleton<IServiceCatalog, GrainServiceCatalog>();
        return services;
    }
}
