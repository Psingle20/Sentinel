namespace Sentinel.AgentWorker.Application.Abstractions.Providers;

/// <summary>
/// Chooses the model for an investigator role (Phase 04 model routing): Muse Spark by default once
/// configured, a more capable or self-hosted Meta model for selected roles, without domain changes.
/// </summary>
public interface IModelRouter
{
    /// <summary>
    /// The providers a role can be routed to in this deployment, by their configuration names:
    /// <c>test-double</c> always, <c>meta</c> once it is configured.
    /// </summary>
    IReadOnlyCollection<string> Configured { get; }

    IModelProvider For(string role);
}
