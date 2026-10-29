namespace Sentinel.Gateway.Api;

/// <summary>
/// Configured as <c>Gateway:Identity</c> (Phase 08): an OpenID Connect authority whose access tokens the Gateway
/// accepts, and the token claims that carry roles and tenants.
/// </summary>
public sealed class IdentityOptions
{
    public const string Section = "Gateway:Identity";

    public string? Authority { get; set; }

    public string? Audience { get; set; }

    public string RoleClaim { get; set; } = "roles";

    public string TenantClaim { get; set; } = "sentinel_tenant";

    public bool RequireHttpsMetadata { get; set; } = true;
}