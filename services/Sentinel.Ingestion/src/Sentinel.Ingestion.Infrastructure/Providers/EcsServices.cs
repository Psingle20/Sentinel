using Amazon.ECS;
using Amazon.ECS.Model;

namespace Sentinel.Ingestion.Infrastructure.Providers;

/// <summary>The services of one ECS cluster, every page of them, described ten at a time as the API allows.</summary>
internal static class EcsServices
{
    public static async Task<List<Service>> ListAsync(IAmazonECS ecs, string cluster, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ecs);

        var arns = new List<string>();
        string? next = null;
        do
        {
            ListServicesResponse page = await ecs.ListServicesAsync(new ListServicesRequest { Cluster = cluster, NextToken = next }, cancellationToken);
            arns.AddRange(page.ServiceArns ?? []);
            next = page.NextToken;
        }
        while (!string.IsNullOrEmpty(next));

        var services = new List<Service>();
        foreach (string[] batch in arns.Chunk(10))
        {
            services.AddRange((await ecs.DescribeServicesAsync(new DescribeServicesRequest { Cluster = cluster, Services = [.. batch] }, cancellationToken)).Services ?? []);
        }

        return services;
    }
}
