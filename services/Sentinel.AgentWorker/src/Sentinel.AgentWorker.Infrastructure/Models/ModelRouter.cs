using Microsoft.Extensions.Options;
using Sentinel.AgentWorker.Application.Abstractions.Providers;
using Sentinel.AgentWorker.Application.Capture;
using Sentinel.AgentWorker.Application.Options;

namespace Sentinel.AgentWorker.Infrastructure.Models;

/// <summary>
/// Picks a provider per role: <c>Model:Routing:&lt;role&gt;</c> when set, otherwise <c>Model:Provider</c>.
/// Configuration values are <c>test-double</c> or <c>meta</c>. A level-3 replay's model profile
/// overrides both for its own scope only.
/// </summary>
internal sealed class ModelRouter(IEnumerable<IModelProvider> providers, IOptions<ModelRoutingOptions> routing, InvestigationCapture capture) : IModelRouter
{
    public const string TestDouble = ModelRoutingOptions.TestDouble;

    private readonly IReadOnlyList<IModelProvider> _providers = [.. providers];

    public IReadOnlyCollection<string> Configured =>
        [.. _providers.Select(provider => provider.Provider == DeterministicModelProvider.ProviderName ? TestDouble : provider.Provider)];

    public IModelProvider For(string role)
    {
        string name = capture.ModelProfile ?? routing.Value.ProviderFor(role);
        string provider = name == TestDouble ? DeterministicModelProvider.ProviderName : name;
        return _providers.FirstOrDefault(candidate => candidate.Provider == provider)
            ?? throw new InvalidOperationException($"No model provider '{name}' is configured for the {role} role.");
    }
}
