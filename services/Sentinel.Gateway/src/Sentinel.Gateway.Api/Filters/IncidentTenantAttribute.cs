using Microsoft.AspNetCore.Mvc;

namespace Sentinel.Gateway.Api.Filters;

/// <summary>Answers an incident route only for an incident of the caller's tenants; see <see cref="IncidentTenantFilter"/>.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class IncidentTenantAttribute() : TypeFilterAttribute(typeof(IncidentTenantFilter));
