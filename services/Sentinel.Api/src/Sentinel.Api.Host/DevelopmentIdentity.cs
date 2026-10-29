using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Sentinel.Api.Host;



/// <summary>Configured as <c>Api:DevIdentity</c>. Users are synthetic addresses on the reserved <c>example.test</c> domain.</summary>
public sealed class DevelopmentIdentityOptions
{
    public const string Section = "Api:DevIdentity";

    public bool Enabled { get; set; }

    public string DefaultUser { get; set; } = string.Empty;

    public IDictionary<string, string[]> Users { get; } = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The tenants every development identity may see.</summary>
    public IList<string> Tenants { get; } = [];
}

/// <summary>
/// The walking skeleton's local development identities (Phase 01A): the caller names a configured
/// user in <c>X-Sentinel-User</c>, or gets the default one. It exists only in the Development
/// environment; anywhere else Api accepts only JWT bearer tokens from the configured authority
/// (Phase 08, <see cref="IdentityOptions"/>).
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

        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, user),
            new(ClaimTypes.Name, user),
            .. roles.Select(role => new Claim(ClaimTypes.Role, role)),
            .. settings.Tenants.Select(tenant => new Claim(SentinelClaims.Tenant, tenant))
        ];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
