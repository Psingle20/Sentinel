using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Contracts.Messaging;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.ControlPlane;
using Shouldly;

namespace Sentinel.Runtime.Tests;

/// <summary>Runtime's control-plane document (Phase 08): mapping, validation and publication.</summary>
public sealed class ControlPlaneConfigurationTests
{
    private sealed class InMemoryStore : IVersionedDocumentStore<RuntimeConfigurationDocument>
    {
        public List<PublishedDocument<RuntimeConfigurationDocument>> Versions { get; } = [];

        public Task<PublishedDocument<RuntimeConfigurationDocument>?> CurrentAsync(CancellationToken cancellationToken) => Task.FromResult(Versions.LastOrDefault());

        public Task<IReadOnlyList<DocumentVersion>> HistoryAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DocumentVersion>>([.. Versions.Select(version => version.Version).Reverse()]);

        public Task<AppendOutcome> AppendAsync(RuntimeConfigurationDocument document, DocumentVersion version, CancellationToken cancellationToken)
        {
            Versions.Add(new PublishedDocument<RuntimeConfigurationDocument>(document, version));
            return Task.FromResult(AppendOutcome.Appended);
        }
    }

    private static RuntimeOptions Deployed()
    {
        var options = new RuntimeOptions();
        InMemoryRuntime.Configure(options);
        return options;
    }

    private static string Json(RuntimeConfigurationDocument document) => JsonSerializer.Serialize(document with { Version = "x" }, SentinelJson.Options);

    private static RuntimeConfigurationService Service(InMemoryStore store) =>
        new(new VersionedDocumentPublisher<RuntimeConfigurationDocument>(store, TimeProvider.System), new RuntimeDeploymentOptions(Deployed));

    [Fact]
    public void The_document_round_trips_the_deployed_options()
    {
        RuntimeConfigurationDocument seed = RuntimeConfigurationMapping.From(Deployed());
        var applied = new RuntimeOptions();
        RuntimeConfigurationMapping.Apply(applied, seed);

        Json(RuntimeConfigurationMapping.From(applied)).ShouldBe(Json(seed));
        seed.Version.ShouldBe(RuntimeConfigurationMapping.DeploymentVersion);
    }

    [Fact]
    public void The_document_survives_serialization_as_the_store_writes_it()
    {
        RuntimeConfigurationDocument seed = RuntimeConfigurationMapping.From(Deployed());

        RuntimeConfigurationDocument? read = JsonSerializer.Deserialize<RuntimeConfigurationDocument>(JsonSerializer.Serialize(seed, SentinelJson.Options), SentinelJson.Options);

        read.ShouldNotBeNull();
        Json(read).ShouldBe(Json(seed));
    }

    [Fact]
    public void Applying_a_document_replaces_rules_instead_of_merging_them()
    {
        RuntimeOptions options = Deployed();
        RuntimeConfigurationDocument seed = RuntimeConfigurationMapping.From(options);
        RuntimeConfigurationDocument tightened = seed with
        {
            Version = "runtime-2",
            Detection = [new DetectionRuleSetting("order-service", "error_rate", 0.2, 0.05, 3, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1))],
            Workflow = seed.Workflow with { ApprovalTimeout = TimeSpan.FromMinutes(10) }
        };

        RuntimeConfigurationMapping.Apply(options, tightened);

        options.Detection.Count.ShouldBe(1);
        options.Detection[0].Service.ShouldBe("order-service");
        options.Workflow.ApprovalTimeout.ShouldBe(TimeSpan.FromMinutes(10));
        options.IsValid().ShouldBeTrue();
    }

    [Fact]
    public void An_unrunnable_document_reports_each_problem()
    {
        RuntimeConfigurationDocument seed = RuntimeConfigurationMapping.From(Deployed());
        RuntimeConfigurationDocument broken = seed with
        {
            Detection = [new DetectionRuleSetting("checkout-service", "slo_burn_rate", 1, 2, 0, TimeSpan.Zero, TimeSpan.FromMinutes(1))],
            Investigation = seed.Investigation with { Roles = ["astrologer"] },
            Consensus = seed.Consensus with { AbstainBelowConfidence = 1.5 },
            Catalog = seed.Catalog with { Dependencies = new Dictionary<string, IReadOnlyList<string>> { ["checkout-service"] = ["checkout-service"] } }
        };

        IReadOnlyList<string> problems = RuntimeConfigurationMapping.Problems(broken);

        problems.ShouldContain(problem => problem.Contains("detection threshold above its onset threshold", StringComparison.Ordinal));
        problems.ShouldContain(problem => problem.Contains("required samples", StringComparison.Ordinal));
        problems.ShouldContain("'astrologer' is not an investigator role.");
        problems.ShouldContain(problem => problem.Contains("between 0 and 1", StringComparison.Ordinal));
        problems.ShouldContain("A service cannot depend on itself.");
    }

    [Fact]
    public void No_detection_rule_at_all_is_refused()
    {
        RuntimeConfigurationDocument empty = RuntimeConfigurationMapping.From(Deployed()) with { Detection = [] };

        RuntimeConfigurationMapping.Problems(empty).ShouldContain("At least one detection rule is required.");
    }

    [Fact]
    public async Task The_deployment_configuration_is_current_until_an_operator_publishes()
    {
        var store = new InMemoryStore();
        RuntimeConfigurationService service = Service(store);

        DocumentState<RuntimeConfigurationDocument> before = await service.CurrentAsync(CancellationToken.None);
        Result<PublishedDocument<RuntimeConfigurationDocument>> published = await service.PublishAsync(
            before.Document with { Version = "runtime-2026.10.29.1", Workflow = before.Document.Workflow with { ApprovalTimeout = TimeSpan.FromMinutes(15) } },
            RuntimeConfigurationMapping.DeploymentVersion,
            "operator@example.test",
            CancellationToken.None);

        before.Published.ShouldBeFalse();
        published.IsSuccess.ShouldBeTrue();
        published.Value.Version.BasedOn.ShouldBe(RuntimeConfigurationMapping.DeploymentVersion);
        DocumentState<RuntimeConfigurationDocument> after = await service.CurrentAsync(CancellationToken.None);
        after.Published.ShouldBeTrue();
        after.Document.Workflow.ApprovalTimeout.ShouldBe(TimeSpan.FromMinutes(15));
        (await service.HistoryAsync(CancellationToken.None)).Select(version => version.Version).ShouldBe(["runtime-2026.10.29.1", RuntimeConfigurationMapping.DeploymentVersion]);
    }

    [Fact]
    public async Task An_invalid_document_is_never_published()
    {
        var store = new InMemoryStore();
        RuntimeConfigurationService service = Service(store);
        RuntimeConfigurationDocument seed = (await service.CurrentAsync(CancellationToken.None)).Document;

        Result<PublishedDocument<RuntimeConfigurationDocument>> refused = await service.PublishAsync(
            seed with { Version = "runtime-bad", Workflow = seed.Workflow with { ApprovalTimeout = TimeSpan.Zero } },
            RuntimeConfigurationMapping.DeploymentVersion,
            "operator@example.test",
            CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("Configuration.Invalid");
        store.Versions.ShouldBeEmpty();
    }
}
