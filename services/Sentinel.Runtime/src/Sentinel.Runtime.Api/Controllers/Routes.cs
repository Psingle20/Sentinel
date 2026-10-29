namespace Sentinel.Runtime.Api.Controllers;

/// <summary>Runtime's route roots: the versioned API its peers call and the operator tooling.</summary>
public static class Routes
{
    public const string Root = "v1";

    public const string Incident = Root + "/incidents/{incidentId:guid}";

    public const string Admin = "admin";
}
