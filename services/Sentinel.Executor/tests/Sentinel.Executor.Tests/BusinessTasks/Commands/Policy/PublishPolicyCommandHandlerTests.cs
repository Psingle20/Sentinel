using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.Time.Testing;
using Sentinel.Executor.Application;
using Sentinel.Executor.Domain.Policy;
using Shouldly;
using Sentinel.Executor.Application.BusinessTasks.Commands.Policy;

namespace Sentinel.Executor.Tests.BusinessTasks.Commands.Policy;

/// <summary>Phase 08 control plane: a policy change is validated, concurrency-checked and stored with its author.</summary>
public sealed class PublishPolicyCommandHandlerTests
{
    private readonly FakePolicyStore _store = new();
    private readonly PublishPolicyCommandHandler _handler;

    public PublishPolicyCommandHandlerTests() =>
        _handler = new PublishPolicyCommandHandler(_store, new FakeTimeProvider(new DateTimeOffset(2026, 10, 29, 9, 0, 0, TimeSpan.Zero)));

    [Fact]
    public async Task A_valid_change_from_the_current_version_takes_effect_and_names_its_author()
    {
        PolicyDocument next = FakePolicyStore.Reference() with { Version = "policy-test-2" };

        Result<PolicyDocument> published = await _handler.Handle(new PublishPolicyCommand(next, "operator@example.test", "policy-test"), CancellationToken.None);

        published.IsSuccess.ShouldBeTrue();
        (await _store.CurrentAsync(CancellationToken.None)).Version.ShouldBe("policy-test-2");
        _store.Published.ShouldHaveSingleItem().Author.ShouldBe("operator@example.test");
    }

    [Fact]
    public async Task A_change_based_on_an_older_version_or_keeping_the_version_is_refused()
    {
        PolicyDocument next = FakePolicyStore.Reference() with { Version = "policy-test-2" };

        (await _handler.Handle(new PublishPolicyCommand(next, "operator@example.test", "policy-old"), CancellationToken.None)).Error.ShouldBe(PolicyErrors.VersionConflict);
        (await _handler.Handle(new PublishPolicyCommand(FakePolicyStore.Reference(), "operator@example.test", "policy-test"), CancellationToken.None)).Error.ShouldBe(PolicyErrors.VersionUnchanged);
        _store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_document_that_would_silently_change_behaviour_is_refused_with_every_problem()
    {
        PolicyRule rule = FakePolicyStore.Reference().Rules[0];
        PolicyDocument broken = FakePolicyStore.Reference() with
        {
            Version = "policy-test-2",
            Rules = [rule, rule with { MaxRisk = "extreme", MinConfidence = 1.5 }, rule with { Id = "everywhere", Environments = [] }],
            Freezes = [new ChangeFreeze("backwards", ["reference"], DateTimeOffset.UnixEpoch.AddDays(2), DateTimeOffset.UnixEpoch, "test")]
        };

        Result<PolicyDocument> refused = await _handler.Handle(new PublishPolicyCommand(broken, "operator@example.test", "policy-test"), CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Description.ShouldContain("used more than once");
        refused.Error.Description.ShouldContain("unknown maxRisk 'extreme'");
        refused.Error.Description.ShouldContain("minConfidence 1.5");
        refused.Error.Description.ShouldContain("covers no environment");
        refused.Error.Description.ShouldContain("ends before it starts");
        _store.Published.ShouldBeEmpty();
    }
}
