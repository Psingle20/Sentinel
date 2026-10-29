using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Sentinel.Gateway.Api;

public static class IdentityExtensions
{
    /// <summary>
    /// The development identity in Development, where it is enabled; otherwise JWT bearer tokens from
    /// the configured authority. With neither, every API call is refused. Controllers authorize by
    /// <see cref="Roles"/>.
    /// </summary>
    public static IServiceCollection AddSentinelIdentity(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.Configure<DevelopmentIdentityOptions>(configuration.GetSection(DevelopmentIdentityOptions.Section));
        IdentityOptions identity = configuration.GetSection(IdentityOptions.Section).Get<IdentityOptions>() ?? new();
        bool development = environment.IsDevelopment() && configuration.GetValue<bool>($"{DevelopmentIdentityOptions.Section}:Enabled");

        AuthenticationBuilder authentication = services.AddAuthentication(development ? DevelopmentIdentityHandler.SchemeName : JwtBearerDefaults.AuthenticationScheme);
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

        services.AddAuthorization();
        return services;
    }
}