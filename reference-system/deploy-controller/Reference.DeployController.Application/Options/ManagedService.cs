namespace Reference.DeployController.Application.Options;

public sealed class ManagedService
{
    public string Baseline { get; set; } = string.Empty;

    public IReadOnlyList<string> Revisions { get; set; } = [];
}
