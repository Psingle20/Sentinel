namespace Sentinel.ServiceDefaults;

/// <summary>
/// The names under which whose work it is appears on spans and in log scopes, the same on every service and on both
/// messages and requests, so traces and logs can be filtered by tenant, environment, service and incident.
/// </summary>
public static class SentinelTags
{
    public const string TenantId = "sentinel.tenant.id";
    public const string EnvironmentId = "sentinel.environment.id";
    public const string ServiceId = "sentinel.service.id";
    public const string IncidentId = "sentinel.incident.id";
}
