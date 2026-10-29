using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Configuration;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Npgsql;
using Shouldly;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.IntegrationTests;

[Collection(MessagingCollection.Name)]
public sealed class VersionedDocumentStoreTests(MessagingInfrastructure infrastructure)
{
    private const string Table = "settings.document_versions";

    private sealed record Settings(string Name, int Limit);

    private static DocumentVersion Version(string version, string? basedOn) => new(version, "operator@example.test", DateTimeOffset.UtcNow, basedOn);

    private async Task<PostgresVersionedDocumentStore<Settings>> CreateStoreAsync()
    {
        string connectionString = await infrastructure.CreateDatabaseAsync("documents");
        NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);
        await using NpgsqlCommand command = dataSource.CreateCommand("create schema if not exists settings; " + PostgresVersionedDocumentStore<Settings>.CreateTableSql(Table));
        await command.ExecuteNonQueryAsync();
        return new PostgresVersionedDocumentStore<Settings>(dataSource, Table, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    [Fact]
    public async Task Nothing_is_current_until_a_version_is_published()
    {
        PostgresVersionedDocumentStore<Settings> store = await CreateStoreAsync();

        (await store.CurrentAsync(CancellationToken.None)).ShouldBeNull();
        (await store.HistoryAsync(10, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Publishing_makes_the_version_current_and_keeps_the_history_newest_first()
    {
        PostgresVersionedDocumentStore<Settings> store = await CreateStoreAsync();

        (await store.AppendAsync(new Settings("v1", 1), Version("v1", "seed"), CancellationToken.None)).ShouldBe(AppendOutcome.Appended);
        (await store.AppendAsync(new Settings("v2", 2), Version("v2", "v1"), CancellationToken.None)).ShouldBe(AppendOutcome.Appended);

        PublishedDocument<Settings>? current = await store.CurrentAsync(CancellationToken.None);
        current.ShouldNotBeNull();
        current.Document.ShouldBe(new Settings("v2", 2));
        current.Version.BasedOn.ShouldBe("v1");
        (await store.HistoryAsync(10, CancellationToken.None)).Select(version => version.Version).ShouldBe(["v2", "v1"]);
    }

    [Fact]
    public async Task A_change_based_on_an_older_version_is_refused()
    {
        PostgresVersionedDocumentStore<Settings> store = await CreateStoreAsync();
        await store.AppendAsync(new Settings("v1", 1), Version("v1", "seed"), CancellationToken.None);
        await store.AppendAsync(new Settings("v2", 2), Version("v2", "v1"), CancellationToken.None);

        (await store.AppendAsync(new Settings("v3", 3), Version("v3", "v1"), CancellationToken.None)).ShouldBe(AppendOutcome.Conflict);
        (await store.CurrentAsync(CancellationToken.None))!.Version.Version.ShouldBe("v2");
    }

    [Fact]
    public async Task A_version_name_is_never_reused()
    {
        PostgresVersionedDocumentStore<Settings> store = await CreateStoreAsync();
        await store.AppendAsync(new Settings("v1", 1), Version("v1", "seed"), CancellationToken.None);
        await store.AppendAsync(new Settings("v2", 2), Version("v2", "v1"), CancellationToken.None);

        (await store.AppendAsync(new Settings("v1", 9), Version("v1", "v2"), CancellationToken.None)).ShouldBe(AppendOutcome.DuplicateVersion);
    }

    [Fact]
    public async Task Concurrent_publishes_from_the_same_version_keep_the_history_linear()
    {
        PostgresVersionedDocumentStore<Settings> store = await CreateStoreAsync();
        await store.AppendAsync(new Settings("v1", 1), Version("v1", "seed"), CancellationToken.None);

        AppendOutcome[] outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(index =>
            store.AppendAsync(new Settings($"v2-{index}", index), Version($"v2-{index}", "v1"), CancellationToken.None)));

        outcomes.Count(outcome => outcome == AppendOutcome.Appended).ShouldBe(1);
        outcomes.Count(outcome => outcome == AppendOutcome.Conflict).ShouldBe(7);
        (await store.HistoryAsync(10, CancellationToken.None)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Reading_before_the_owner_created_the_table_finds_nothing_published()
    {
        string connectionString = await infrastructure.CreateDatabaseAsync("documents");
        var store = new PostgresVersionedDocumentStore<Settings>(NpgsqlDataSource.Create(connectionString), Table, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        (await store.CurrentAsync(CancellationToken.None)).ShouldBeNull();
        (await store.HistoryAsync(10, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public void Only_a_plain_schema_qualified_table_name_is_accepted()
    {
        Should.Throw<ArgumentException>(() => PostgresVersionedDocumentStore<Settings>.CreateTableSql("versions; drop table x"));
        Should.Throw<ArgumentException>(() => PostgresVersionedDocumentStore<Settings>.CreateTableSql("unqualified"));
    }
}
