using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Sentinel.Api.Application.Approvals;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Api.Host;

public static class Roles
{
    /// <summary>Reads incidents of the caller's tenants and writes postmortem narratives.</summary>
    public const string Responder = "responder";

    /// <summary>Approves, rejects and revokes remediation plans.</summary>
    public const string Approver = "approver";

    /// <summary>Changes the control plane: policy, operator controls and sources.</summary>
    public const string Operator = "operator";
}

public static class SentinelClaims
{
    /// <summary>A tenant the caller may see; one claim per tenant.</summary>
    public const string Tenant = "sentinel:tenant";

    public static IReadOnlyList<TenantId> Tenants(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return [.. user.FindAll(Tenant).Select(claim => TenantId.From(claim.Value)).Distinct()];
    }

    public static bool CanAccess(this ClaimsPrincipal user, TenantId tenantId) => user.Tenants().Contains(tenantId);
}

/// <summary>
/// Configured as <c>Api:Identity</c> (Phase 08): an OpenID Connect authority whose access tokens Api
/// accepts, and the token claims that carry roles and tenants.
/// </summary>
public sealed class IdentityOptions
{
    public const string Section = "Api:Identity";

    public string? Authority { get; set; }

    public string? Audience { get; set; }

    public string RoleClaim { get; set; } = "roles";

    public string TenantClaim { get; set; } = "sentinel_tenant";

    public bool RequireHttpsMetadata { get; set; } = true;
}

public static class IdentityExtensions
{
    /// <summary>
    /// The development identity in Development, where it is enabled; otherwise JWT bearer tokens from
    /// the configured authority. With neither, every API call is refused.
    /// </summary>
    public static void AddSentinelIdentity(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.Configure<DevelopmentIdentityOptions>(builder.Configuration.GetSection(DevelopmentIdentityOptions.Section));
        IdentityOptions identity = builder.Configuration.GetSection(IdentityOptions.Section).Get<IdentityOptions>() ?? new();
        bool development = builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>($"{DevelopmentIdentityOptions.Section}:Enabled");

        AuthenticationBuilder authentication = builder.Services.AddAuthentication(development ? DevelopmentIdentityHandler.SchemeName : JwtBearerDefaults.AuthenticationScheme);
        if (development)
        {
            authentication.AddScheme<AuthenticationSchemeOptions, DevelopmentIdentityHandler>(DevelopmentIdentityHandler.SchemeName, null);
        }

        authentication.AddJwtBearer(options =>
        {
            options.Authority = identity.Authority;
            options.Audience = identity.Audience;
            options.RequireHttpsMetadata = identity.RequireHttpsMetadata;
            options.MapInboundClaims = false;
            options.TokenValidationParameters.NameClaimType = "sub";
            options.TokenValidationParameters.RoleClaimType = identity.RoleClaim;
            options.Events = new JwtBearerEvents
            {
                // Roles and tenants are normalized to the claim types every endpoint reads.
                OnTokenValidated = context =>
                {
                    if (context.Principal?.Identity is ClaimsIdentity claims)
                    {
                        string? subject = claims.FindFirst("sub")?.Value;
                        if (subject is not null && claims.FindFirst(ClaimTypes.NameIdentifier) is null)
                        {
                            claims.AddClaim(new Claim(ClaimTypes.NameIdentifier, subject));
                        }

                        claims.AddClaims([.. claims.FindAll(identity.RoleClaim).Select(role => new Claim(ClaimTypes.Role, role.Value))]);
                        claims.AddClaims([.. claims.FindAll(identity.TenantClaim).Select(tenant => new Claim(SentinelClaims.Tenant, tenant.Value))]);
                    }

                    return Task.CompletedTask;
                }
            };
        });

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(Roles.Responder, policy => policy.RequireRole(Roles.Responder))
            .AddPolicy(Roles.Approver, policy => policy.RequireRole(Roles.Approver))
            .AddPolicy(Roles.Operator, policy => policy.RequireRole(Roles.Operator));
    }
}

/// <summary>
/// Incident endpoints answer only for incidents of the caller's tenants. Another tenant's incident
/// is reported as not found, so its existence is not disclosed.
/// </summary>
public sealed class IncidentTenantFilter(IRuntimeIncidents incidents) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (context.HttpContext.Request.RouteValues.TryGetValue("incidentId", out object? value) && Guid.TryParse(value?.ToString(), out Guid incidentId))
        {
            Sentinel.Contracts.Incidents.IncidentDetail? incident = await incidents.GetAsync(IncidentId.From(incidentId), context.HttpContext.RequestAborted);
            if (incident is null || !context.HttpContext.User.CanAccess(incident.Summary.TenantId))
            {
                return Results.NotFound();
            }
        }

        return await next(context);
    }
}
