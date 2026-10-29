using System.Collections.Concurrent;
using CleanArchitecture.BuildingBlocks.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Application;
using Sentinel.Ingestion.Application.Knowledge;
using Sentinel.Ingestion.Domain.Sources;

namespace Sentinel.Ingestion.Tests;

/// <summary>
/// The Ingestion service as its <c>Program</c> composes it, with its stores, knowledge provider and
/// clock replaced and a query bulkhead of one slot and no queue. No background work runs: the pull
/// loops, outbox dispatcher and configuration refresher would reach PostgreSQL, Redis and the providers.
/// </summary>
public sealed class IngestionHost(string environment) : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset Now = new(2026, 10, 29, 9, 30, 0, TimeSpan.Zero);

    public FakeTimeProvider Time { get; } = new(Now);

    public InMemoryObservations Observations { get; } = new();

    public InMemorySources Sources { get; } = new();

    public InMemoryDocuments Documents { get; } = new();

    public FixedKnowledge Knowledge { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("ConnectionStrings:ingestion-db", "Host=localhost;Database=ingestion");
        builder.UseSetting("ConnectionStrings:messaging", "localhost:6379");
        builder.UseSetting("Ingestion:Providers:Prometheus:Endpoint", "http://prometheus");
        builder.UseSetting("Ingestion:Providers:DeployController:Token", "endpoint-test-not-a-token");
        builder.UseSetting("Ingestion:QueryConcurrency", "1");
        builder.UseSetting("Ingestion:QueryQueueLimit", "0");
        builder.ConfigureTestServices(services =>
        {
            foreach (ServiceDescriptor hosted in services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType?.Name != "GenericWebHostService").ToList())
            {
                services.Remove(hosted);
            }

            services.RemoveAll<IObservationStore>();
            services.AddSingleton<IObservationStore>(Observations);
            services.RemoveAll<ISourceStateStore>();
            services.AddSingleton<ISourceStateStore>(Sources);
            services.RemoveAll<IVersionedDocumentStore<SourceConfigurationDocument>>();
            services.AddSingleton<IVersionedDocumentStore<SourceConfigurationDocument>>(Documents);
            services.RemoveAll<IKnowledgeProvider>();
            services.AddSingleton<IKnowledgeProvider>(Knowledge);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
        });
    }

    /// <summary>Stored observations; <see cref="Hold"/> keeps every query waiting until <see cref="Release"/>.</summary>
    public sealed class InMemoryObservations : IObservationStore
    {
        private readonly ConcurrentDictionary<ObservationId, Observation> _observations = new();
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource? _gate;

        /// <summary>Completes when the first query reaches the store.</summary>
        public Task Entered => _entered.Task;

        public void Add(Observation observation) => _observations[observation.ObservationId] = observation;

        public void Hold() => _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _gate?.TrySetResult();

        public Task<IReadOnlyList<Observation>> AddNewAsync(IReadOnlyList<Observation> observations, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Observation>>([.. observations.Where(observation => _observations.TryAdd(observation.ObservationId, observation))]);

        public async Task<ObservationPage> QueryAsync(ObservationQuery query, CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
            if (_gate is { } gate)
            {
                await gate.Task.WaitAsync(cancellationToken);
            }

            return new ObservationPage([.. _observations.Values], false, null);
        }
    }

    public sealed class InMemorySources : ISourceStateStore
    {
        private readonly ConcurrentDictionary<string, SourceState> _states = new(StringComparer.Ordinal);

        public void Add(SourceState state) => _states[state.SourceId] = state;

        public Task<SourceState?> GetAsync(string sourceId, CancellationToken cancellationToken) => Task.FromResult(_states.GetValueOrDefault(sourceId));

        public Task<IReadOnlyList<SourceState>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SourceState>>([.. _states.Values.OrderBy(state => state.SourceId, StringComparer.Ordinal)]);

        public Task SaveAsync(SourceState state, CancellationToken cancellationToken)
        {
            Add(state);
            return Task.CompletedTask;
        }
    }

    public sealed class InMemoryDocuments : IVersionedDocumentStore<SourceConfigurationDocument>
    {
        private readonly List<PublishedDocument<SourceConfigurationDocument>> _versions = [];

        public Task<PublishedDocument<SourceConfigurationDocument>?> CurrentAsync(CancellationToken cancellationToken)
        {
            lock (_versions)
            {
                return Task.FromResult(_versions.LastOrDefault());
            }
        }

        public Task<IReadOnlyList<DocumentVersion>> HistoryAsync(int limit, CancellationToken cancellationToken)
        {
            lock (_versions)
            {
                return Task.FromResult<IReadOnlyList<DocumentVersion>>([.. _versions.Select(version => version.Version).Reverse().Take(limit)]);
            }
        }

        public Task<AppendOutcome> AppendAsync(SourceConfigurationDocument document, DocumentVersion version, CancellationToken cancellationToken)
        {
            lock (_versions)
            {
                _versions.Add(new PublishedDocument<SourceConfigurationDocument>(document, version));
            }

            return Task.FromResult(AppendOutcome.Appended);
        }
    }

    /// <summary>One runbook about Redis connection exhaustion.</summary>
    public sealed class FixedKnowledge : IKnowledgeProvider
    {
        public string Name => "runbooks";

        public Task<IReadOnlyList<KnowledgeEntry>> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<KnowledgeEntry>>([
                new KnowledgeEntry(
                    "redis-connection-exhaustion",
                    "Redis connection exhaustion",
                    "checkout-service",
                    "Check the pool size first.\n\nRoll back the deployment that raised Redis connections.",
                    "runbooks/redis-connection-exhaustion.md",
                    Now.AddDays(-3))
            ]);
    }
}
