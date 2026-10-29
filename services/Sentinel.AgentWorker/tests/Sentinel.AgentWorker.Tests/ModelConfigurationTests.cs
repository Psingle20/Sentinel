using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.AgentWorker.Application.Investigations;
using Sentinel.AgentWorker.Application.Models;
using Sentinel.AgentWorker.Application.Tools;
using Sentinel.Contracts.Messaging;
using Shouldly;

namespace Sentinel.AgentWorker.Tests;

/// <summary>AgentWorker's control-plane document (Phase 08 model selection).</summary>
public sealed class ModelConfigurationTests
{
    private static readonly string[] TestDoubleOnly = [ModelRoutingOptions.TestDouble];

    private sealed class InMemoryStore : IVersionedDocumentStore<ModelConfigurationDocument>
    {
        public List<PublishedDocument<ModelConfigurationDocument>> Versions { get; } = [];

        public Task<PublishedDocument<ModelConfigurationDocument>?> CurrentAsync(CancellationToken cancellationToken) => Task.FromResult(Versions.LastOrDefault());

        public Task<IReadOnlyList<DocumentVersion>> HistoryAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DocumentVersion>>([.. Versions.Select(version => version.Version).Reverse()]);

        public Task<AppendOutcome> AppendAsync(ModelConfigurationDocument document, DocumentVersion version, CancellationToken cancellationToken)
        {
            Versions.Add(new PublishedDocument<ModelConfigurationDocument>(document, version));
            return Task.FromResult(AppendOutcome.Appended);
        }
    }

    private static ModelConfigurationDocument Seed()
    {
        var routing = new ModelRoutingOptions();
        routing.Routing["logs"] = ModelRoutingOptions.TestDouble;
        return ModelConfigurationMapping.From(routing, new InvestigationOptions(), new ModelPricingOptions { InputPerMillionTokens = 0.2, OutputPerMillionTokens = 0.6 });
    }

    private static ModelConfigurationService Service(InMemoryStore store, IReadOnlyCollection<string> providers) =>
        new(new VersionedDocumentPublisher<ModelConfigurationDocument>(store, TimeProvider.System), Seed, providers);

    [Fact]
    public void The_document_carries_routing_prompt_budget_baseline_and_prices()
    {
        ModelConfigurationDocument document = Seed() with { DefaultProvider = "meta", MaxModelTurns = 4, ShadowBaseline = false };
        var routing = new ModelRoutingOptions();
        var investigation = new InvestigationOptions();
        var pricing = new ModelPricingOptions();

        ModelConfigurationMapping.Apply(routing, document);
        ModelConfigurationMapping.Apply(investigation, document);
        ModelConfigurationMapping.Apply(pricing, document);

        routing.ProviderFor("metrics").ShouldBe("meta");
        routing.ProviderFor("logs").ShouldBe(ModelRoutingOptions.TestDouble);
        investigation.MaxModelTurns.ShouldBe(4);
        investigation.ShadowBaseline.ShouldBeFalse();
        pricing.OutputPerMillionTokens.ShouldBe(0.6);
        JsonSerializer.Deserialize<ModelConfigurationDocument>(JsonSerializer.Serialize(document, SentinelJson.Options), SentinelJson.Options)!.Routing["logs"].ShouldBe(ModelRoutingOptions.TestDouble);
    }

    [Fact]
    public void A_provider_without_credentials_in_this_deployment_cannot_be_selected()
    {
        ModelConfigurationDocument document = Seed() with { DefaultProvider = "meta" };

        ModelConfigurationMapping.Problems(document, TestDoubleOnly).ShouldContain(problem => problem.StartsWith("Provider 'meta' is not configured", StringComparison.Ordinal));
        ModelConfigurationMapping.Problems(document, [ModelRoutingOptions.TestDouble, "meta"]).ShouldBeEmpty();
    }

    [Fact]
    public void Unknown_roles_prompts_and_budgets_are_refused()
    {
        ModelConfigurationDocument document = Seed() with
        {
            Routing = new Dictionary<string, string> { ["verifier"] = ModelRoutingOptions.TestDouble },
            PromptVersion = "investigator-v0",
            MaxModelTurns = 0,
            InputPricePerMillionTokens = -1
        };

        IReadOnlyList<string> problems = ModelConfigurationMapping.Problems(document, TestDoubleOnly);

        problems.ShouldContain("'verifier' is not a model-backed investigator role.");
        problems.ShouldContain(problem => problem.StartsWith("Prompt version 'investigator-v0'", StringComparison.Ordinal));
        problems.ShouldContain(problem => problem.Contains("model turns", StringComparison.Ordinal));
        problems.ShouldContain("Token prices are finite and not negative.");
    }

    [Fact]
    public async Task An_operator_moves_a_role_to_another_prompt_version_through_a_new_version()
    {
        var store = new InMemoryStore();
        ModelConfigurationService service = Service(store, TestDoubleOnly);

        Result<PublishedDocument<ModelConfigurationDocument>> published = await service.PublishAsync(
            Seed() with { Version = "models-2026.10.29.1", PromptVersion = InvestigatorPrompt.Versions[0] },
            ModelConfigurationMapping.DeploymentVersion,
            "operator@example.test",
            CancellationToken.None);

        published.IsSuccess.ShouldBeTrue();
        (await service.CurrentAsync(CancellationToken.None)).Document.PromptVersion.ShouldBe(InvestigatorPrompt.Versions[0]);
    }

    [Fact]
    public async Task A_document_routing_to_an_unconfigured_provider_is_never_published()
    {
        var store = new InMemoryStore();
        ModelConfigurationService service = Service(store, TestDoubleOnly);

        Result<PublishedDocument<ModelConfigurationDocument>> refused = await service.PublishAsync(
            Seed() with { Version = "models-meta", DefaultProvider = "meta" },
            ModelConfigurationMapping.DeploymentVersion,
            "operator@example.test",
            CancellationToken.None);

        refused.Error.Code.ShouldBe("Configuration.Invalid");
        store.Versions.ShouldBeEmpty();
    }
}
