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

/// <summary>
/// Guards the endpoints that change behaviour: revision switches, batch control and reset. They
/// exist only in the Development or Test environment with <c>Reference:FailureControls:Enabled</c>
/// set, and every call must present the control token (Phase 01 safety and isolation).
/// </summary>
public sealed class FailureControl
{
    public const string TokenHeader = "X-Reference-Control-Token";

    private readonly byte[] _token;

    private FailureControl(bool enabled, string? token)
    {
        Enabled = enabled;
        _token = Encoding.UTF8.GetBytes(token ?? string.Empty);
    }

    public bool Enabled { get; }

    public static FailureControl From(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        bool allowedEnvironment = environment.IsDevelopment() || environment.IsEnvironment("Test");
        bool requested = configuration.GetValue<bool>("Reference:FailureControls:Enabled");
        string? token = configuration["Reference:ControlToken"];

        if (requested && allowedEnvironment && string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Reference:ControlToken is required when failure controls are enabled.");
        }

        return new FailureControl(requested && allowedEnvironment, token);
    }

    public bool IsAuthorized(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Enabled || !request.Headers.TryGetValue(TokenHeader, out StringValues presented))
        {
            return false;
        }

        byte[] candidate = Encoding.UTF8.GetBytes(presented.ToString());
        return CryptographicOperations.FixedTimeEquals(candidate, _token);
    }
}

public sealed record RevisionView(string Service, string Revision, DateTimeOffset ActiveSince, IReadOnlyCollection<string> KnownRevisions);

public sealed record SwitchRevisionRequest(string Revision);

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
