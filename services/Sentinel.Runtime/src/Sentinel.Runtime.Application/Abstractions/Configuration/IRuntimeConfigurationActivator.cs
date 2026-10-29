using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Runtime.Application.ControlPlane;

namespace Sentinel.Runtime.Application.Abstractions.Configuration;

/// <summary>Makes a published version of Runtime's configuration take effect here at once; other instances apply it at their next refresh.</summary>
public interface IRuntimeConfigurationActivator
{
    void Activate(PublishedDocument<RuntimeConfigurationDocument> published);
}
