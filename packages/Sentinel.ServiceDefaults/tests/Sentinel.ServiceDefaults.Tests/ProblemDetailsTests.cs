using System.Net;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Shouldly;

namespace Sentinel.ServiceDefaults.Tests;

/// <summary>Phase 08 API principles: stable problem details with correlation identifiers.</summary>
public sealed class ProblemDetailsTests
{
    private static async Task<WebApplication> StartAppAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.AddServiceDefaults();

        WebApplication app = builder.Build();
        app.MapDefaultEndpoints();
        app.MapGet("/conflict", () => Error.Conflict("Plan.Changed", "The plan changed.").ToProblem());
        app.MapGet("/throws", IResult () => throw new InvalidOperationException("internal detail that must not leak"));
        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task A_failed_result_carries_its_code_and_the_trace_it_belongs_to()
    {
        await using WebApplication app = await StartAppAsync();
        using HttpClient client = app.GetTestClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/conflict", UriKind.Relative));
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        problem.RootElement.GetProperty("code").GetString().ShouldBe("Plan.Changed");
        string traceId = problem.RootElement.GetProperty("traceId").GetString()!;
        traceId.ShouldNotBeNullOrWhiteSpace();
        response.Headers.GetValues(ServiceDefaultsExtensions.CorrelationHeader).ShouldBe([traceId]);
    }

    [Fact]
    public async Task An_unhandled_exception_becomes_a_problem_without_its_details()
    {
        await using WebApplication app = await StartAppAsync();
        using HttpClient client = app.GetTestClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/throws", UriKind.Relative));
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldContain("traceId");
        body.ShouldNotContain("internal detail that must not leak");
    }

    [Fact]
    public async Task A_bare_status_code_is_answered_with_problem_details()
    {
        await using WebApplication app = await StartAppAsync();
        using HttpClient client = app.GetTestClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/missing", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }
}
