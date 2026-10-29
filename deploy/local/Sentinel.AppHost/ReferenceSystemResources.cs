using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Sentinel.AppHost;

/// <summary>The reference system's resources that Sentinel's services connect to (ADR-011).</summary>
internal sealed record ReferenceSystemResources(
    IResourceBuilder<ProjectResource> DeployController,
    IResourceBuilder<ProjectResource> ScenarioRunner,
    IResourceBuilder<ProjectResource> ReportingWorker,
    IResourceBuilder<ContainerResource> Prometheus,
    IResourceBuilder<ContainerResource> Loki,
    IResourceBuilder<ContainerResource> Jaeger,
    IResourceBuilder<ParameterResource> DeployToken,
    IResourceBuilder<ParameterResource> ReadToken,
    IResourceBuilder<ParameterResource> ControlToken,
    string KnowledgeDirectory);
