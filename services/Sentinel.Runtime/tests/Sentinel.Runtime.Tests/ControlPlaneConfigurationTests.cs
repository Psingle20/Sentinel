using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Contracts.Messaging;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.ControlPlane;
using Shouldly;
using Sentinel.Runtime.Application.Options;
using Sentinel.Runtime.Application.Abstractions.Configuration;
using Sentinel.Runtime.Application.BusinessTasks.Commands.Configuration;
using Sentinel.Runtime.Application.BusinessTasks.Queries.Configuration;

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

    private sealed class RecordingActivator : IRuntimeConfigurationActivator
    {
        public List<PublishedDocument<RuntimeConfigurationDocument>> Activated { get; } = [];

        public void Activate(PublishedDocument<RuntimeConfigurationDocument> published) => Activated.Add(published);
    }

    private static readonly RuntimeDeploymentOptions Deployment = new(Deployed);

    private static async Task<DocumentState<RuntimeConfigurationDocument>> CurrentDocumentAsync(VersionedDocumentPublisher<RuntimeConfigurationDocument> publisher) =>
        (await new GetRuntimeConfigurationQueryHandler(publisher, Deployment).Handle(new GetRuntimeConfigurationQuery(), CancellationToken.None)).Value;

    private static Task<Result<DocumentVersion>> PublishAsync(VersionedDocumentPublisher<RuntimeConfigurationDocument> publisher, RecordingActivator activator, RuntimeConfigurationDocument document) =>
        new PublishRuntimeConfigurationCommandHandler(publisher, Deployment, activator).Handle(
            new PublishRuntimeConfigurationCommand(document, RuntimeConfigurationMapping.DeploymentVersion, "operator@example.test"), CancellationToken.None);

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
        var publisher = new VersionedDocumentPublisher<RuntimeConfigurationDocument>(new InMemoryStore(), TimeProvider.System);
        var activator = new RecordingActivator();

        DocumentState<RuntimeConfigurationDocument> before = await CurrentDocumentAsync(publisher);
        Result<DocumentVersion> published = await PublishAsync(
            publisher,
            activator,
            before.Document with { Version = "runtime-2026.10.29.1", Workflow = before.Document.Workflow with { ApprovalTimeout = TimeSpan.FromMinutes(15) } });

        before.Published.ShouldBeFalse();
        published.IsSuccess.ShouldBeTrue();
        published.Value.BasedOn.ShouldBe(RuntimeConfigurationMapping.DeploymentVersion);
        activator.Activated.ShouldHaveSingleItem().Version.ShouldBe(published.Value);
        DocumentState<RuntimeConfigurationDocument> after = await CurrentDocumentAsync(publisher);
        after.Published.ShouldBeTrue();
        after.Document.Workflow.ApprovalTimeout.ShouldBe(TimeSpan.FromMinutes(15));
        IReadOnlyList<DocumentVersion> history = (await new GetRuntimeConfigurationHistoryQueryHandler(publisher, Deployment).Handle(new GetRuntimeConfigurationHistoryQuery(), CancellationToken.None)).Value;
        history.Select(version => version.Version).ShouldBe(["runtime-2026.10.29.1", RuntimeConfigurationMapping.DeploymentVersion]);
    }

    [Fact]
    public async Task An_invalid_document_is_never_published()
    {
        var store = new InMemoryStore();
        var publisher = new VersionedDocumentPublisher<RuntimeConfigurationDocument>(store, TimeProvider.System);
        var activator = new RecordingActivator();
        RuntimeConfigurationDocument seed = (await CurrentDocumentAsync(publisher)).Document;

        Result<DocumentVersion> refused = await PublishAsync(publisher, activator, seed with { Version = "runtime-bad", Workflow = seed.Workflow with { ApprovalTimeout = TimeSpan.Zero } });

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("Configuration.Invalid");
        store.Versions.ShouldBeEmpty();
        activator.Activated.ShouldBeEmpty();
    }
}
