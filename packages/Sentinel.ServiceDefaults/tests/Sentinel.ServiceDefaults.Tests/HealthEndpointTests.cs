using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shouldly;

namespace Sentinel.ServiceDefaults.Tests;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task Liveness_and_readiness_report_healthy_without_dependency_checks()
    {
        await using WebApplication app = await StartAppAsync(_ => { });
        using HttpClient client = app.GetTestClient();

        (await GetStatusAsync(client, ServiceDefaultsExtensions.LivenessPath)).ShouldBe(HttpStatusCode.OK);
        (await GetStatusAsync(client, ServiceDefaultsExtensions.ReadinessPath)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_unhealthy_dependency_fails_readiness_but_not_liveness()
    {
        await using WebApplication app = await StartAppAsync(services => services
            .AddHealthChecks()
            .AddCheck("dependency", () => HealthCheckResult.Unhealthy(), [ServiceDefaultsExtensions.ReadyTag]));
        using HttpClient client = app.GetTestClient();

        (await GetStatusAsync(client, ServiceDefaultsExtensions.LivenessPath)).ShouldBe(HttpStatusCode.OK);
        (await GetStatusAsync(client, ServiceDefaultsExtensions.ReadinessPath)).ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    private static async Task<WebApplication> StartAppAsync(Action<IServiceCollection> configureServices)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.AddServiceDefaults();
        configureServices(builder.Services);

        WebApplication app = builder.Build();
        app.MapDefaultEndpoints();
        await app.StartAsync();

        return app;
    }

    private static async Task<HttpStatusCode> GetStatusAsync(HttpClient client, string path)
    {
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));
        return response.StatusCode;
    }
}
