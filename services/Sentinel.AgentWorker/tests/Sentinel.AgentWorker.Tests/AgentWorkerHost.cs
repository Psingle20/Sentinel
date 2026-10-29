using System.Collections.Concurrent;
using CleanArchitecture.BuildingBlocks.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Sentinel.AgentWorker.Application.Investigations;
using Sentinel.AgentWorker.Application.Models;
using Sentinel.AgentWorker.Domain.Executions;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.AgentWorker.Tests;

/// <summary>
/// The AgentWorker service as its <c>Program</c> composes it, with its execution store, configuration
/// store and clock replaced. No background work runs: the stream consumer, outbox dispatcher and
/// configuration refresher would reach Redis and PostgreSQL. It runs outside Development, so no
/// developer's user secrets (a configured model provider, say) change what it answers; operator
/// endpoints are switched on or off explicitly instead.
/// </summary>
public sealed class AgentWorkerHost(string environment, bool operatorEndpoints = true) : WebApplicationFactory<Program>
{
    public const string Testing = "Testing";

    public static readonly DateTimeOffset Now = new(2026, 10, 29, 9, 30, 0, TimeSpan.Zero);

    public FakeTimeProvider Time { get; } = new(Now);

    public InMemoryExecutions Executions { get; } = new();

    public InMemoryDocuments Documents { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("Sentinel:OperatorEndpoints:Enabled", operatorEndpoints ? "true" : "false");
        builder.UseSetting("ConnectionStrings:agentworker-db", "Host=localhost;Database=agentworker");
        builder.UseSetting("ConnectionStrings:messaging", "localhost:6379");
        builder.ConfigureTestServices(services =>
        {
            foreach (ServiceDescriptor hosted in services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType?.Name != "GenericWebHostService").ToList())
            {
                services.Remove(hosted);
            }

            services.RemoveAll<IExecutionStore>();
            services.AddSingleton<IExecutionStore>(Executions);
            services.RemoveAll<IVersionedDocumentStore<ModelConfigurationDocument>>();
            services.AddSingleton<IVersionedDocumentStore<ModelConfigurationDocument>>(Documents);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
        });
    }

    public sealed class InMemoryExecutions : IExecutionStore
    {
        private readonly ConcurrentDictionary<AgentExecutionId, AgentExecution> _executions = new();
        private readonly ConcurrentDictionary<AgentExecutionId, ExecutionInputs> _inputs = new();

        public ConcurrentQueue<InvestigationReplayResult> Replays { get; } = new();

        public Task SaveAsync(AgentExecution execution, CancellationToken cancellationToken)
        {
            _executions[execution.AgentExecutionId] = execution;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AgentExecution>> ForIncidentAsync(IncidentId incidentId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AgentExecution>>([.. _executions.Values.Where(execution => execution.IncidentId == incidentId).OrderBy(execution => execution.StartedAt)]);

        public Task<AgentExecution?> GetAsync(AgentExecutionId executionId, CancellationToken cancellationToken) =>
            Task.FromResult(_executions.GetValueOrDefault(executionId));

        public Task SaveInputsAsync(ExecutionInputs inputs, CancellationToken cancellationToken)
        {
            _inputs[inputs.AgentExecutionId] = inputs;
            return Task.CompletedTask;
        }

        public Task<ExecutionInputs?> GetInputsAsync(AgentExecutionId executionId, CancellationToken cancellationToken) =>
            Task.FromResult(_inputs.GetValueOrDefault(executionId));

        public Task SaveReplayAsync(InvestigationReplayResult replay, CancellationToken cancellationToken)
        {
            Replays.Enqueue(replay);
            return Task.CompletedTask;
        }
    }

    public sealed class InMemoryDocuments : IVersionedDocumentStore<ModelConfigurationDocument>
    {
        private readonly List<PublishedDocument<ModelConfigurationDocument>> _versions = [];

        public Task<PublishedDocument<ModelConfigurationDocument>?> CurrentAsync(CancellationToken cancellationToken)
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

        public Task<AppendOutcome> AppendAsync(ModelConfigurationDocument document, DocumentVersion version, CancellationToken cancellationToken)
        {
            lock (_versions)
            {
                _versions.Add(new PublishedDocument<ModelConfigurationDocument>(document, version));
            }

            return Task.FromResult(AppendOutcome.Appended);
        }
    }
}
