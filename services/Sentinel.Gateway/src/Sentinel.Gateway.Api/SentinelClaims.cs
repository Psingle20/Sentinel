using System.Security.Claims;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Api;

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

    /// <summary>The authenticated caller's ID, which every decision and edit is recorded under.</summary>
    public static string UserId(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return user.FindFirstValue(ClaimTypes.NameIdentifier)!;
    }
}