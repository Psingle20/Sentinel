namespace Reference.DeployController.Domain.Revisions;

/// <summary>A service the controller deploys: the revision it returns to on reset and every revision it may serve.</summary>
public sealed record ManagedRevisions(string Service, string Baseline, IReadOnlyList<string> Revisions);
