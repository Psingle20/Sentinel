namespace Reference.DeployController.Application.BusinessProcesses.Deployments;

public enum DeployOutcome
{
    Accepted,
    Replayed,
    UnknownService,
    UnknownRevision,
    Conflict
}
