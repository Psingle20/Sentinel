using Sentinel.AgentWorker.Application.Abstractions.Providers;

namespace Sentinel.AgentWorker.Tests;

/// <summary>A model API that answers every call as the Meta adapter reports a 429.</summary>
internal sealed class RateLimitedModel : IModelProvider
{
    public string Provider => "scripted";

    public string Model => "scripted";

    public string? ModelVersion => null;

    public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(ModelResponse.Failed(ModelFinish.RateLimited, "The model API is rate limiting requests."));
}
