using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using Reference.Common.Revisions;

namespace Reference.Common.Control;

public static class FailureControlExtensions
{
    public static IServiceCollection AddFailureControl(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddSingleton(FailureControl.From(configuration, environment));
        return services;
    }

    /// <summary>
    /// Maps <c>GET /_reference/revision</c> and, when failure controls are enabled, the guarded
    /// <c>/_reference</c> group with <c>PUT /_reference/revision</c>. Returns the guarded group, or
    /// null when controls are disabled, so services can add their own control endpoints.
    /// </summary>
    public static RouteGroupBuilder? MapReferenceControl(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/_reference/revision", (RevisionState state) =>
            new RevisionView(state.ServiceName, state.Current, state.ActiveSince, state.KnownRevisions));

        FailureControl control = app.Services.GetRequiredService<FailureControl>();
        if (!control.Enabled)
        {
            return null;
        }

        RouteGroupBuilder group = app.MapGroup("/_reference");
        group.AddEndpointFilter(async (context, next) =>
            control.IsAuthorized(context.HttpContext.Request) ? await next(context) : Results.Unauthorized());

        group.MapPut("/revision", async (SwitchRevisionRequest request, RevisionState state) =>
            await state.SwitchAsync(request.Revision)
                ? Results.Ok(new RevisionView(state.ServiceName, state.Current, state.ActiveSince, state.KnownRevisions))
                : Results.BadRequest($"{request.Revision} is not a revision of {state.ServiceName}."));

        return group;
    }
}
