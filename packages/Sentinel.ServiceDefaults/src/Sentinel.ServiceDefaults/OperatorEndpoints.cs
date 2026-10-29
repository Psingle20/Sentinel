using CleanArchitecture.BuildingBlocks.Infrastructure.Redis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Sentinel.ServiceDefaults;

/// <summary>
/// Operator controls every service exposes (Phase 02): list dead-lettered messages and replay one
/// after its cause is fixed. They are mapped only in the Development environment or when
/// <c>Sentinel:OperatorEndpoints:Enabled</c> is set; Phase 09 puts them behind operator authorization.
/// </summary>
public static class OperatorEndpoints
{
    public const string EnabledKey = "Sentinel:OperatorEndpoints:Enabled";

    private const string StreamPrefix = "sentinel.";

    /// <summary>Whether operator endpoints exist: in Development, or where <see cref="EnabledKey"/> is set.</summary>
    public static bool AreEnabled(IHostEnvironment environment, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);

        return environment.IsDevelopment() || configuration.GetValue<bool>(EnabledKey);
    }

    public static WebApplication MapOperatorEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (!AreEnabled(app.Environment, app.Configuration))
        {
            return app;
        }

        RouteGroupBuilder admin = app.MapGroup("/admin");
        admin.MapGet("/dead-letters", async (string stream, int? count, RedisDeadLetters deadLetters) =>
            IsSentinelStream(stream)
                ? Results.Ok(await deadLetters.ListAsync(stream, Math.Clamp(count ?? 50, 1, 500)))
                : Results.BadRequest($"Only {StreamPrefix}* streams are managed here."));
        admin.MapPost("/dead-letters/replay", async (ReplayDeadLetter request, RedisDeadLetters deadLetters) =>
        {
            if (!IsSentinelStream(request.Stream))
            {
                return Results.BadRequest($"Only {StreamPrefix}* streams are managed here.");
            }

            return await deadLetters.ReplayAsync(request.Stream, request.EntryId) ? Results.NoContent() : Results.NotFound();
        });
        return app;
    }

    private static bool IsSentinelStream(string? stream) =>
        stream is { Length: > 0 } && stream.StartsWith(StreamPrefix, StringComparison.Ordinal) && !stream.EndsWith(".dlq", StringComparison.Ordinal);
}
