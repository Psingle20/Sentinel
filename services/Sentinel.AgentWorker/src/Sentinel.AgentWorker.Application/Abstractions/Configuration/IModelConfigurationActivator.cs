using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.AgentWorker.Application.ControlPlane;

namespace Sentinel.AgentWorker.Application.Abstractions.Configuration;

/// <summary>
/// Makes a published version of AgentWorker's model configuration take effect here at once;
/// investigations already running keep the settings they started with, and other instances apply it
/// at their next refresh.
/// </summary>
public interface IModelConfigurationActivator
{
    void Activate(PublishedDocument<ModelConfigurationDocument> published);
}
