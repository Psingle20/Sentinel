using CleanArchitecture.BuildingBlocks.Configuration;
using CleanArchitecture.BuildingBlocks.Infrastructure.Configuration;
using Sentinel.Runtime.Application.Abstractions.Configuration;
using Sentinel.Runtime.Application.ControlPlane;

namespace Sentinel.Runtime.Infrastructure.Configuration;

/// <summary>Applies a published version to this instance's managed document, which re-binds Runtime's options.</summary>
internal sealed class ManagedRuntimeConfigurationActivator(ManagedDocument<RuntimeConfigurationDocument> current) : IRuntimeConfigurationActivator
{
    public void Activate(PublishedDocument<RuntimeConfigurationDocument> published) => current.Set(published);
}
