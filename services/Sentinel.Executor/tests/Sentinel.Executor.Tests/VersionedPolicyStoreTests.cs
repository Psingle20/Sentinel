using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Sentinel.Executor.Application.Options;
using Sentinel.Executor.Domain.Policy;
using Sentinel.Executor.Infrastructure.Persistence;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Sentinel.Executor.Tests;

/// <summary>Policy publication against a real PostgreSQL (review finding F-009): publishers racing over one version.</summary>
public sealed class VersionedPolicyStoreTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.3").Build();
    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _dataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        await new ExecutorSchema(_dataSource).InitializeAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Of_publishers_racing_over_the_same_version_exactly_one_succeeds()
    {
        VersionedPolicyStore store = Store();
        string seed = (await store.CurrentAsync(CancellationToken.None)).Version;

        bool[] outcomes = await Task.WhenAll(Enumerable.Range(1, 10).Select(publisher =>
            Task.Run(() => store.AppendAsync(Policy($"policy-race-{publisher}"), seed, $"operator{publisher}@example.test", Now.AddSeconds(publisher), CancellationToken.None))));

        outcomes.Count(published => published).ShouldBe(1);
        (await store.HistoryAsync(CancellationToken.None)).Count(version => version.Source == "control-plane").ShouldBe(1);
    }

    [Fact]
    public async Task A_publication_over_a_version_that_is_no_longer_current_is_refused()
    {
        VersionedPolicyStore store = Store();
        string seed = (await store.CurrentAsync(CancellationToken.None)).Version;

        (await store.AppendAsync(Policy("policy-v2"), seed, "operator@example.test", Now, CancellationToken.None)).ShouldBeTrue();
        (await store.AppendAsync(Policy("policy-v3"), seed, "operator@example.test", Now.AddMinutes(1), CancellationToken.None)).ShouldBeFalse("v2 was published since the operator read the seed");
        (await store.AppendAsync(Policy("policy-v3"), "policy-v2", "operator@example.test", Now.AddMinutes(2), CancellationToken.None)).ShouldBeTrue();
        (await store.CurrentAsync(CancellationToken.None)).Version.ShouldBe("policy-v3");
    }

    private VersionedPolicyStore Store() => new(
        new FilePolicyStore(
            Options.Create(new ExecutorOptions { PolicyFile = "no-such-policy.json" }),
            new HostingEnvironment { ContentRootPath = AppContext.BaseDirectory },
            NullLogger<FilePolicyStore>.Instance),
        _dataSource);

    private static PolicyDocument Policy(string version) => FakePolicyStore.Reference() with { Version = version };
}
