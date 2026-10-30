using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Sentinel.AppHost;

/// <summary>
/// The reference system's resources that Sentinel's services connect to (ADR-011). In the AWS environment there is no
/// local queue-consumer, and the emulator is what Ingestion reads changes from and Executor quarantines through.
/// </summary>
internal sealed record ReferenceSystemResources(
    IResourceBuilder<ProjectResource> DeployController,
    IResourceBuilder<ProjectResource> ScenarioRunner,
    IResourceBuilder<ProjectResource> ReportingWorker,
    IResourceBuilder<ProjectResource>? QueueConsumer,
    IResourceBuilder<ContainerResource> Prometheus,
    IResourceBuilder<ContainerResource> Loki,
    IResourceBuilder<ContainerResource> Jaeger,
    IResourceBuilder<ParameterResource> DeployToken,
    IResourceBuilder<ParameterResource> ReadToken,
    IResourceBuilder<ParameterResource> ControlToken,
    string KnowledgeDirectory,
    IResourceBuilder<ContainerResource>? AwsEmulator = null);
