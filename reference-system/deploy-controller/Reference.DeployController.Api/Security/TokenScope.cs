namespace Reference.DeployController.Api.Security;

public enum TokenScope
{
    /// <summary>Reading services and deployments: either token.</summary>
    Read,

    /// <summary>Deployments and rollbacks: the deploy token only.</summary>
    Deploy
}
