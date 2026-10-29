using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.Time.Testing;
using Sentinel.Gateway.Application.Postmortems;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Shouldly;
using Sentinel.Gateway.Application.Abstractions.Persistence;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Gateway.Application.BusinessTasks.Commands.Postmortems;
using Sentinel.Gateway.Application.BusinessTasks.Queries.Postmortems;

namespace Sentinel.Gateway.Tests;

public sealed class PostmortemServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private readonly IncidentId _incident = IncidentId.New();
    private readonly InMemoryNarratives _narratives = new();
    private readonly GetPostmortemQueryHandler _get;
    private readonly SaveNarrativeCommandHandler _save;

    public PostmortemServiceTests()
    {
        var history = new StubHistory(_incident);
        _get = new GetPostmortemQueryHandler(history, _narratives);
        _save = new SaveNarrativeCommandHandler(history, _narratives, new NoUnitOfWork(), new FakeTimeProvider(Now));
    }

    [Fact]
    public async Task The_generated_postmortem_comes_back_with_the_saved_narrative()
    {
        (await _save.Handle(new SaveNarrativeCommand(_incident, "We rolled back too late.", 0, "responder@example.test"), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        Postmortem postmortem = (await _get.Handle(new GetPostmortemQuery(_incident), CancellationToken.None)).Value.ShouldNotBeNull();

        postmortem.Summary.Sources.ShouldNotBeEmpty();
        PostmortemNarrative narrative = postmortem.Narrative.ShouldNotBeNull();
        narrative.Markdown.ShouldBe("We rolled back too late.");
        narrative.Version.ShouldBe(1);
        narrative.EditedBy.ShouldBe("responder@example.test");
        narrative.EditedAt.ShouldBe(Now);
    }

    [Fact]
    public async Task An_edit_based_on_an_old_version_is_refused_so_no_one_overwrites_another_edit()
    {
        await _save.Handle(new SaveNarrativeCommand(_incident, "First.", 0, "responder@example.test"), CancellationToken.None);
        await _save.Handle(new SaveNarrativeCommand(_incident, "Second.", 1, "viewer@example.test"), CancellationToken.None);

        Result<PostmortemNarrative> stale = await _save.Handle(new SaveNarrativeCommand(_incident, "Stale.", 1, "responder@example.test"), CancellationToken.None);

        stale.Error.ShouldBe(PostmortemErrors.VersionConflict);
        (await _narratives.GetAsync(_incident, CancellationToken.None))!.Markdown.ShouldBe("Second.");
    }

    [Fact]
    public async Task A_narrative_for_an_unknown_incident_or_one_too_long_is_refused()
    {
        (await _save.Handle(new SaveNarrativeCommand(IncidentId.New(), "Text.", 0, "responder@example.test"), CancellationToken.None)).Error.ShouldBe(PostmortemErrors.NotFound);
        (await _save.Handle(new SaveNarrativeCommand(_incident, new string('x', PostmortemErrors.MaxNarrativeLength + 1), 0, "responder@example.test"), CancellationToken.None)).Error.ShouldBe(PostmortemErrors.TooLong);
    }

    private sealed class StubHistory(IncidentId known) : IRuntimeHistory
    {
        public Task<Postmortem?> PostmortemAsync(IncidentId incidentId, CancellationToken cancellationToken) =>
            Task.FromResult(incidentId != known ? null : new Postmortem
            {
                IncidentId = known,
                Title = "checkout-service slo_burn_rate at 9.0",
                Status = IncidentStatus.Resolved,
                StreamVersion = 22,
                GeneratedAt = Now,
                Summary = new PostmortemFact("Resolved.", [new SourceRef("event", "1")]),
                Versions = new VersionManifest { SchemaVersion = 1 }
            });

        public Task<IReadOnlyList<SimilarIncident>?> SimilarAsync(IncidentId incidentId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SimilarIncident>?>([]);

        public Task<DomainReplayResult?> ReplayAsync(IncidentId incidentId, CancellationToken cancellationToken) => Task.FromResult<DomainReplayResult?>(null);
    }

    private sealed class InMemoryNarratives : INarrativeStore
    {
        private readonly Dictionary<IncidentId, PostmortemNarrative> _narratives = [];

        public Task<PostmortemNarrative?> GetAsync(IncidentId incidentId, CancellationToken cancellationToken) =>
            Task.FromResult(_narratives.GetValueOrDefault(incidentId));

        public Task<bool> SaveAsync(IncidentId incidentId, PostmortemNarrative narrative, int expectedVersion, CancellationToken cancellationToken)
        {
            int current = _narratives.TryGetValue(incidentId, out PostmortemNarrative? existing) ? existing.Version : 0;
            if (current != expectedVersion)
            {
                return Task.FromResult(false);
            }

            _narratives[incidentId] = narrative;
            return Task.FromResult(true);
        }
    }

    private sealed class NoUnitOfWork : IUnitOfWork
    {
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
