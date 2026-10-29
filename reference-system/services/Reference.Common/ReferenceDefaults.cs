using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reference.Common.Control;
using Reference.Common.Redis;
using Reference.Common.Revisions;
using Reference.Common.Telemetry;

namespace Reference.Common;

public static class ReferenceDefaults
{
    /// <summary>
    /// Telemetry, the serving revision, failure controls, the reference Redis endpoint and a
    /// health check: what every reference service needs. <c>Reference:InitialRevision</c>
    /// overrides the starting revision.
    /// </summary>
    public static WebApplicationBuilder AddReferenceDefaults(
        this WebApplicationBuilder builder,
        string serviceName,
        string baselineRevision,
        IReadOnlyCollection<string> revisions,
        params string[] telemetrySources)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddReferenceTelemetry(serviceName, telemetrySources);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(provider => new RevisionState(
            serviceName,
            builder.Configuration["Reference:InitialRevision"] ?? baselineRevision,
            revisions,
            provider.GetRequiredService<TimeProvider>()));
        builder.Services.AddFailureControl(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(_ => RedisEndpoint.Parse(builder.Configuration["Reference:Redis"]));
        builder.Services.AddSingleton(_ => new SeededJitter(builder.Configuration.GetValue("Reference:Seed", 1)));
        builder.Services.AddHealthChecks();
        return builder;
    }

    public static WebApplication MapReferenceHealth(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapHealthChecks("/health");
        return app;
    }
}
