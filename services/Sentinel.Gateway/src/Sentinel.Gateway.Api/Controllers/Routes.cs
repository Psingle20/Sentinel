namespace Sentinel.Gateway.Api.Controllers;

/// <summary>The Gateway's route roots: the versioned public API and its incident and admin areas.</summary>
public static class Routes
{
    public const string Root = "api/v1";

    public const string Incidents = Root + "/incidents";

    /// <summary>One incident; every route under it is answered only within the caller's tenants.</summary>
    public const string Incident = Incidents + "/{incidentId:guid}";

    public const string Admin = Root + "/admin";

    public static string IncidentLocation(Guid incidentId) => $"/{Incidents}/{incidentId}";
}