using CleanArchitecture.BuildingBlocks.Configuration;
using CleanArchitecture.BuildingBlocks.Infrastructure.Configuration;
using Sentinel.AgentWorker.Application.Abstractions.Configuration;
using Sentinel.AgentWorker.Application.ControlPlane;

namespace Sentinel.AgentWorker.Infrastructure.Configuration;

/// <summary>Applies a published version to this instance's managed document, which re-binds the routing, investigation and pricing options.</summary>
internal sealed class ManagedModelConfigurationActivator(ManagedDocument<ModelConfigurationDocument> current) : IModelConfigurationActivator
{
    public void Activate(PublishedDocument<ModelConfigurationDocument> published) => current.Set(published);
}
