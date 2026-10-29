using CleanArchitecture.BuildingBlocks.Configuration;
using CleanArchitecture.BuildingBlocks.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Tests;

public sealed class ManagedDocumentTests
{
    private sealed class LimitOptions
    {
        public int Limit { get; set; } = 5;

        public IList<string> Targets { get; } = ["deployed"];
    }

    private sealed record LimitDocument(int Limit, string[] Targets);

    private sealed class InMemoryStore : IVersionedDocumentStore<LimitDocument>
    {
        public PublishedDocument<LimitDocument>? Latest { get; set; }

        public bool Failing { get; set; }

        public Task<PublishedDocument<LimitDocument>?> CurrentAsync(CancellationToken cancellationToken) =>
            Failing ? throw new InvalidOperationException("The store is unavailable.") : Task.FromResult(Latest);

        public Task<IReadOnlyList<DocumentVersion>> HistoryAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DocumentVersion>>(Latest is null ? [] : [Latest.Version]);

        public Task<AppendOutcome> AppendAsync(LimitDocument document, DocumentVersion version, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static PublishedDocument<LimitDocument> Published(string version, int limit, params string[] targets) =>
        new(new LimitDocument(limit, targets), new DocumentVersion(version, "operator@example.test", DateTimeOffset.UnixEpoch, null));

    private static ServiceProvider Build(InMemoryStore store) =>
        new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<IVersionedDocumentStore<LimitDocument>>(store)
            .AddManagedDocument<LimitOptions, LimitDocument>((options, document) =>
            {
                options.Limit = document.Limit;
                options.Targets.Clear();
                foreach (string target in document.Targets)
                {
                    options.Targets.Add(target);
                }
            })
            .BuildServiceProvider();

    private static async Task StartAsync(ServiceProvider provider)
    {
        foreach (IHostedService service in provider.GetServices<IHostedService>())
        {
            await service.StartAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task The_deployment_configuration_applies_until_a_version_is_published()
    {
        await using ServiceProvider provider = Build(new InMemoryStore());
        await StartAsync(provider);

        LimitOptions options = provider.GetRequiredService<IOptions<LimitOptions>>().Value;

        options.Limit.ShouldBe(5);
        options.Targets.ShouldBe(["deployed"]);
    }

    [Fact]
    public async Task The_published_version_is_applied_over_the_deployment_configuration_at_startup()
    {
        await using ServiceProvider provider = Build(new InMemoryStore { Latest = Published("v1", 9, "published") });
        await StartAsync(provider);

        LimitOptions options = provider.GetRequiredService<IOptions<LimitOptions>>().Value;

        options.Limit.ShouldBe(9);
        options.Targets.ShouldBe(["published"]);
    }

    [Fact]
    public async Task A_new_version_replaces_collections_and_reaches_existing_consumers()
    {
        await using ServiceProvider provider = Build(new InMemoryStore { Latest = Published("v1", 9, "a", "b", "c") });
        await StartAsync(provider);
        IOptions<LimitOptions> options = provider.GetRequiredService<IOptions<LimitOptions>>();
        options.Value.Targets.Count.ShouldBe(3);

        provider.GetRequiredService<ManagedDocument<LimitDocument>>().Set(Published("v2", 2, "a"));

        options.Value.Limit.ShouldBe(2);
        options.Value.Targets.ShouldBe(["a"]);
    }

    [Fact]
    public async Task A_store_failure_keeps_the_deployment_configuration()
    {
        await using ServiceProvider provider = Build(new InMemoryStore { Failing = true });
        await StartAsync(provider);

        provider.GetRequiredService<IOptions<LimitOptions>>().Value.Limit.ShouldBe(5);
    }

    private sealed class TargetOptions
    {
        public int TargetCount { get; set; }
    }

    [Fact]
    public async Task One_document_can_shape_several_options_types_and_is_refreshed_once()
    {
        var store = new InMemoryStore { Latest = Published("v1", 9, "a", "b") };
        await using ServiceProvider provider = new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<IVersionedDocumentStore<LimitDocument>>(store)
            .AddManagedDocument<LimitOptions, LimitDocument>((options, document) => options.Limit = document.Limit)
            .AddManagedDocument<TargetOptions, LimitDocument>((options, document) => options.TargetCount = document.Targets.Length)
            .BuildServiceProvider();
        await StartAsync(provider);

        provider.GetServices<IHostedService>().Count().ShouldBe(1);
        provider.GetRequiredService<IOptions<LimitOptions>>().Value.Limit.ShouldBe(9);
        provider.GetRequiredService<IOptions<TargetOptions>>().Value.TargetCount.ShouldBe(2);
    }

    [Fact]
    public void Setting_the_same_version_again_does_not_signal_a_change()
    {
        var document = new ManagedDocument<LimitDocument>();
        document.Set(Published("v1", 1));
        Microsoft.Extensions.Primitives.IChangeToken token = document.GetChangeToken();

        document.Set(Published("v1", 1));

        token.HasChanged.ShouldBeFalse();
        document.Set(Published("v2", 2));
        token.HasChanged.ShouldBeTrue();
    }
}
