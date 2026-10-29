using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Ingestion.Application.ControlPlane;

namespace Sentinel.Ingestion.Application.Abstractions.Configuration;

/// <summary>
/// Makes a published version of Ingestion's source configuration take effect here at once: the pull
/// loops finish their current pulls and restart with the new sources. Other instances apply it at their
/// next refresh.
/// </summary>
public interface ISourceConfigurationActivator
{
    void Activate(PublishedDocument<SourceConfigurationDocument> published);
}
