using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Contracts.Messaging;
using Sentinel.Ingestion.Application;
using Sentinel.Ingestion.Application.Providers;
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

    private static SourceConfigurationService Service(InMemoryStore store) =>
        new(new VersionedDocumentPublisher<SourceConfigurationDocument>(store, TimeProvider.System), Deployed);

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
        var store = new InMemoryStore();
        SourceConfigurationService service = Service(store);
        SourceConfigurationDocument seed = (await service.CurrentAsync(CancellationToken.None)).Document;

        Result<PublishedDocument<SourceConfigurationDocument>> published = await service.PublishAsync(
            seed with { Version = "sources-2026.10.29.1", TopologyEnabled = true },
            SourceConfigurationMapping.DeploymentVersion,
            "operator@example.test",
            CancellationToken.None);

        published.IsSuccess.ShouldBeTrue();
        (await service.CurrentAsync(CancellationToken.None)).Document.TopologyEnabled.ShouldBeTrue();
        (await service.HistoryAsync(CancellationToken.None)).Count.ShouldBe(2);
    }
}
