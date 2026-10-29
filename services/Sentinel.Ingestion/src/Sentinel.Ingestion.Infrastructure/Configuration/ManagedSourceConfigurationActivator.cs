using CleanArchitecture.BuildingBlocks.Configuration;
using CleanArchitecture.BuildingBlocks.Infrastructure.Configuration;
using Sentinel.Ingestion.Application.Abstractions.Configuration;
using Sentinel.Ingestion.Application.ControlPlane;

namespace Sentinel.Ingestion.Infrastructure.Configuration;

/// <summary>Applies a published version to this instance's managed document, which re-binds Ingestion's options and restarts the pull loops.</summary>
internal sealed class ManagedSourceConfigurationActivator(ManagedDocument<SourceConfigurationDocument> current) : ISourceConfigurationActivator
{
    public void Activate(PublishedDocument<SourceConfigurationDocument> published) => current.Set(published);
}
