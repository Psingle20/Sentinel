using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Contracts.Messaging;
using Sentinel.Ingestion.Application.Abstractions.Configuration;
using Sentinel.Ingestion.Application.BusinessTasks.Commands.Configuration;
using Sentinel.Ingestion.Application.BusinessTasks.Queries.Configuration;
using Sentinel.Ingestion.Application.ControlPlane;
using Sentinel.Ingestion.Application.Options;
using Shouldly;

namespace Sentinel.Ingestion.Tests;

/// <summary>Ingestion's control-plane document (Phase 08 provider configuration).</summary>
public sealed class SourceConfigurationTests
{
    private sealed class InMemoryStore : IVersionedDocumentStore<SourceConfigurationDocument>
    {
        public List<PublishedDocument<SourceConfigurationDocument>> Versions { get; } = [];

        public Task<PublishedDocument<SourceConfigurationDocument>?> CurrentAsync(CancellationToken cancellationToken) => Task.FromResult(Versions.LastOrDefault());

        public Task<IReadOnlyList<DocumentVersion>> HistoryAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DocumentVersion>>([.. Versions.Select(version => version.Version).Reverse()]);

        public Task<AppendOutcome> AppendAsync(SourceConfigurationDocument document, DocumentVersion version, CancellationToken cancellationToken)
        {
            Versions.Add(new PublishedDocument<SourceConfigurationDocument>(document, version));
            return Task.FromResult(AppendOutcome.Appended);
        }
    }

    private static readonly MetricSeriesDefinition BurnRate = new() { Name = "slo_burn_rate", Expression = "sum(rate(errors[1m])) by (service_name)", ServiceLabel = "service_name", Priority = 10 };

    private static IngestionOptions Deployed()
    {
        var options = new IngestionOptions { Tenant = "acme", Environment = "reference" };
        options.MetricSeries.Add(BurnRate);
        options.LogSources.Add(new LogSourceDefinition { Name = "checkout", Query = "{service_name=\"checkout-service\"}" });
        return options;
    }

    private sealed class RecordingActivator : ISourceConfigurationActivator
    {
        public List<PublishedDocument<SourceConfigurationDocument>> Activated { get; } = [];

        public void Activate(PublishedDocument<SourceConfigurationDocument> published) => Activated.Add(published);
    }

    [Fact]
    public void Applying_a_document_replaces_the_sources_and_keeps_the_deployment_identity()
    {
        IngestionOptions options = Deployed();
        SourceConfigurationDocument document = SourceConfigurationMapping.From(options) with
        {
            Version = "sources-2",
            PollInterval = TimeSpan.FromSeconds(10),
            LogSources = []
        };

        SourceConfigurationMapping.Apply(options, document);

        options.PollInterval.ShouldBe(TimeSpan.FromSeconds(10));
        options.LogSources.ShouldBeEmpty();
        options.MetricSeries.ShouldBe([BurnRate]);
        options.Tenant.ShouldBe("acme");
        options.IsValid().ShouldBeTrue();
    }

    [Fact]
    public void The_document_survives_serialization_as_the_store_writes_it()
    {
        SourceConfigurationDocument document = SourceConfigurationMapping.From(Deployed());

        SourceConfigurationDocument read = JsonSerializer.Deserialize<SourceConfigurationDocument>(JsonSerializer.Serialize(document, SentinelJson.Options), SentinelJson.Options)!;

        read.MetricSeries.ShouldBe([BurnRate]);
        read.LogSources[0].Query.ShouldBe(document.LogSources[0].Query);
    }

    [Fact]
    public void Sources_that_cannot_run_are_refused_with_reasons()
    {
        SourceConfigurationDocument document = SourceConfigurationMapping.From(Deployed()) with
        {
            PollInterval = TimeSpan.FromMilliseconds(100),
            MetricSeries = [BurnRate, BurnRate with { ServiceLabel = null }],
            LogSources = [new LogSourceDefinition { Name = "checkout", Query = "{}", Limit = 50_000 }]
        };

        IReadOnlyList<string> problems = SourceConfigurationMapping.Problems(document);

        problems.ShouldContain("The poll interval is between 1 second and 5 minutes.");
        problems.ShouldContain("Source 'metrics:slo_burn_rate' is defined more than once.");
        problems.ShouldContain("Metric series 'slo_burn_rate' names either a fixed service or a service label, not both or neither.");
        problems.ShouldContain(problem => problem.StartsWith("Log source 'checkout'", StringComparison.Ordinal));
    }

    [Fact]
    public void Removing_every_metric_series_is_refused_because_detection_depends_on_them()
    {
        SourceConfigurationDocument document = SourceConfigurationMapping.From(Deployed()) with { MetricSeries = [] };

        SourceConfigurationMapping.Problems(document).ShouldContain("At least one metric series is required: detection depends on it.");
    }

    [Fact]
    public async Task An_operator_publishes_new_sources_over_the_deployment_configuration()
    {
        var publisher = new VersionedDocumentPublisher<SourceConfigurationDocument>(new InMemoryStore(), TimeProvider.System);
        var deployment = new IngestionDeploymentOptions(Deployed);
        var activator = new RecordingActivator();
        var current = new GetSourceConfigurationQueryHandler(publisher, deployment);
        var history = new GetSourceConfigurationHistoryQueryHandler(publisher, deployment);
        var publish = new PublishSourceConfigurationCommandHandler(publisher, deployment, activator);
        SourceConfigurationDocument seed = (await current.Handle(new GetSourceConfigurationQuery(), CancellationToken.None)).Value.Document;

        Result<DocumentVersion> published = await publish.Handle(
            new PublishSourceConfigurationCommand(seed with { Version = "sources-2026.10.29.1", TopologyEnabled = true }, SourceConfigurationMapping.DeploymentVersion, "operator@example.test"),
            CancellationToken.None);

        published.Value.Version.ShouldBe("sources-2026.10.29.1");
        activator.Activated.ShouldHaveSingleItem().Document.TopologyEnabled.ShouldBeTrue();
        (await current.Handle(new GetSourceConfigurationQuery(), CancellationToken.None)).Value.Document.TopologyEnabled.ShouldBeTrue();
        (await history.Handle(new GetSourceConfigurationHistoryQuery(), CancellationToken.None)).Value.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_version_that_breaks_the_startup_rules_is_refused_and_not_applied()
    {
        var publisher = new VersionedDocumentPublisher<SourceConfigurationDocument>(new InMemoryStore(), TimeProvider.System);
        var deployment = new IngestionDeploymentOptions(Deployed);
        var activator = new RecordingActivator();
        var publish = new PublishSourceConfigurationCommandHandler(publisher, deployment, activator);
        SourceConfigurationDocument seed = SourceConfigurationMapping.From(Deployed());

        Result<DocumentVersion> refused = await publish.Handle(
            new PublishSourceConfigurationCommand(seed with { Version = "sources-2026.10.29.1", MetricSeries = [] }, SourceConfigurationMapping.DeploymentVersion, "operator@example.test"),
            CancellationToken.None);

        refused.Error.Code.ShouldBe("Configuration.Invalid");
        activator.Activated.ShouldBeEmpty();
    }
}
