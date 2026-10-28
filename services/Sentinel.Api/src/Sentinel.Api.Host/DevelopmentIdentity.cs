using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Sentinel.Api.Host;

/// <summary>The plan version and content hash the approver reviewed.</summary>
public sealed record ApprovePlanRequest(int PlanVersion, string PlanHash);

public sealed record RejectPlanRequest(int PlanVersion, string Reason);

public static class Roles
{
    public const string Responder = "responder";
    public const string Approver = "approver";
}

/// <summary>Configured as <c>Api:DevIdentity</c>. Users are synthetic addresses on the reserved <c>example.test</c> domain.</summary>
public sealed class DevelopmentIdentityOptions
{
    public const string Section = "Api:DevIdentity";

    public bool Enabled { get; set; }

    public string DefaultUser { get; set; } = string.Empty;

    public IDictionary<string, string[]> Users { get; } = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// The walking skeleton's local development identities (Phase 01A): the caller names a configured
/// user in <c>X-Sentinel-User</c>, or gets the default one. It exists only in the Development
/// environment; anywhere else no scheme is registered and every API call is refused. Phase 08
/// replaces it with real identity.
/// </summary>
public sealed class DevelopmentIdentityHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<DevelopmentIdentityOptions> identities) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Development";
    public const string UserHeader = "X-Sentinel-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        DevelopmentIdentityOptions settings = identities.Value;
        string user = Request.Headers.TryGetValue(UserHeader, out Microsoft.Extensions.Primitives.StringValues requested) && !string.IsNullOrWhiteSpace(requested)
            ? requested.ToString()
            : settings.DefaultUser;

        if (string.IsNullOrWhiteSpace(user) || !settings.Users.TryGetValue(user, out string[]? roles))
        {
            return Task.FromResult(AuthenticateResult.Fail($"{user} is not a configured development identity."));
        }

        Claim[] claims = [new(ClaimTypes.NameIdentifier, user), new(ClaimTypes.Name, user), .. roles.Select(role => new Claim(ClaimTypes.Role, role))];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
