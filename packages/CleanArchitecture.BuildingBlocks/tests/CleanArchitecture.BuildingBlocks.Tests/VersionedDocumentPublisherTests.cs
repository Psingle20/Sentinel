using CleanArchitecture.BuildingBlocks.Configuration;
using Shouldly;

namespace CleanArchitecture.BuildingBlocks.Tests;

public sealed class VersionedDocumentPublisherTests
{
    private sealed record Limits(string Version, int Limit) : IVersionedDocument;

    private sealed class InMemoryStore : IVersionedDocumentStore<Limits>
    {
        private readonly List<PublishedDocument<Limits>> _versions = [];

        public Task<PublishedDocument<Limits>?> CurrentAsync(CancellationToken cancellationToken) => Task.FromResult(_versions.LastOrDefault());

        public Task<IReadOnlyList<DocumentVersion>> HistoryAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DocumentVersion>>([.. _versions.Select(published => published.Version).Reverse().Take(limit)]);

        public Task<AppendOutcome> AppendAsync(Limits document, DocumentVersion version, CancellationToken cancellationToken)
        {
            if (_versions.Any(published => published.Version.Version == version.Version))
            {
                return Task.FromResult(AppendOutcome.DuplicateVersion);
            }

            if (_versions.Count > 0 && _versions[^1].Version.Version != version.BasedOn)
            {
                return Task.FromResult(AppendOutcome.Conflict);
            }

            _versions.Add(new PublishedDocument<Limits>(document, version));
            return Task.FromResult(AppendOutcome.Appended);
        }
    }

    private static readonly Func<Limits> Seed = () => new Limits("deployment", 5);

    private static IReadOnlyList<string> Valid(Limits document) => document.Limit > 0 ? [] : ["The limit must be positive."];

    private readonly InMemoryStore _store = new();

    private VersionedDocumentPublisher<Limits> Publisher => new(_store, TimeProvider.System);

    private Task<Result<PublishedDocument<Limits>>> PublishAsync(Limits document, string expectedVersion) =>
        Publisher.PublishAsync(document, expectedVersion, "operator@example.test", Seed, Valid, CancellationToken.None);

    [Fact]
    public async Task The_seed_is_in_effect_until_a_version_is_published()
    {
        DocumentState<Limits> current = await Publisher.CurrentAsync(Seed, CancellationToken.None);

        current.Published.ShouldBeFalse();
        current.Document.ShouldBe(new Limits("deployment", 5));
        current.Version.Author.ShouldBe(VersionedDocumentPublisher<Limits>.SeedAuthor);
    }

    [Fact]
    public async Task A_publish_based_on_the_current_version_becomes_current_and_records_its_base()
    {
        Result<PublishedDocument<Limits>> first = await PublishAsync(new Limits("v1", 9), "deployment");
        Result<PublishedDocument<Limits>> second = await PublishAsync(new Limits("v2", 3), "v1");

        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        second.Value.Version.BasedOn.ShouldBe("v1");
        DocumentState<Limits> current = await Publisher.CurrentAsync(Seed, CancellationToken.None);
        current.Published.ShouldBeTrue();
        current.Document.Limit.ShouldBe(3);
        (await Publisher.HistoryAsync(Seed, 10, CancellationToken.None)).Select(version => version.Version).ShouldBe(["v2", "v1", "deployment"]);
    }

    [Fact]
    public async Task A_publish_based_on_a_stale_version_is_a_conflict()
    {
        await PublishAsync(new Limits("v1", 9), "deployment");

        Result<PublishedDocument<Limits>> stale = await PublishAsync(new Limits("v2", 3), "deployment");

        stale.Error.ShouldBe(VersionedDocumentErrors.VersionConflict);
    }

    [Fact]
    public async Task A_publish_needs_a_new_version_name()
    {
        Result<PublishedDocument<Limits>> unchanged = await PublishAsync(new Limits("deployment", 9), "deployment");
        Result<PublishedDocument<Limits>> empty = await PublishAsync(new Limits(" ", 9), "deployment");

        unchanged.Error.ShouldBe(VersionedDocumentErrors.VersionUnchanged);
        empty.Error.ShouldBe(VersionedDocumentErrors.VersionUnchanged);
    }

    [Fact]
    public async Task An_invalid_document_is_refused_with_the_owners_reasons()
    {
        Result<PublishedDocument<Limits>> invalid = await PublishAsync(new Limits("v1", 0), "deployment");

        invalid.Error.Code.ShouldBe("Configuration.Invalid");
        invalid.Error.Description.ShouldBe("The limit must be positive.");
        (await Publisher.CurrentAsync(Seed, CancellationToken.None)).Published.ShouldBeFalse();
    }

    [Fact]
    public async Task A_reused_version_name_is_refused()
    {
        await PublishAsync(new Limits("v1", 9), "deployment");
        await PublishAsync(new Limits("v2", 3), "v1");

        Result<PublishedDocument<Limits>> reused = await PublishAsync(new Limits("v1", 4), "v2");

        reused.Error.ShouldBe(VersionedDocumentErrors.DuplicateVersion);
    }
}
