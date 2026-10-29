namespace Reference.DeployController.Application.Options;

/// <summary>Configured as <c>DeployController</c>.</summary>
public sealed class DeployControllerOptions
{
    public const string Section = "DeployController";

    public int RolloutSeconds { get; set; } = 20;

    public Dictionary<string, ManagedService> Services { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Bearer token for deployments and rollbacks: held by the scenario runner and Sentinel.Executor.</summary>
    public string? DeployToken { get; set; }

    /// <summary>Bearer token for reading services and deployments: held by Sentinel.Ingestion.</summary>
    public string? ReadToken { get; set; }
}
