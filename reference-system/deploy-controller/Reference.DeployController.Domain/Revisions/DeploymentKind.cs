namespace Reference.DeployController.Domain.Revisions;

public enum DeploymentKind
{
    Deploy,
    Rollback,
    ConfigChange,
    Reset
}
