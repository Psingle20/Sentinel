namespace Sentinel.AgentWorker.Application.Abstractions.Providers;

/// <summary>
/// A Sentinel-owned model interface (ADR-015). Implementations translate to one provider's wire
/// format; they never run tools, validate findings or see Sentinel's domain.
/// </summary>
public interface IModelProvider
{
    string Provider { get; }

    string Model { get; }

    string? ModelVersion { get; }

    /// <summary>True when the same request always gets the same response, so a replay may claim determinism (Phase 07).</summary>
    bool Deterministic => false;

    /// <summary>This model's list price, when the deployment configured one; null falls back to <c>Model:Pricing</c>.</summary>
    ModelPrice? Price => null;

    Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken);
}
