using Reference.DeployController.Domain.Revisions;

namespace Reference.DeployController.Application.BusinessProcesses.Services;

/// <summary>A managed service as the controller sees it; <see cref="Error"/> says why its serving revision is unknown.</summary>
public sealed record ServiceView(
    string Name,
    string? ServingRevision,
    string BaselineRevision,
    IReadOnlyCollection<string> Revisions,
    DeploymentRecord? ActiveDeployment,
    string? Error);
