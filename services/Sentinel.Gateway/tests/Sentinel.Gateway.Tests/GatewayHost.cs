using System.Collections.Concurrent;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Sentinel.Gateway.Application.Approvals;
using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Gateway.Tests;

/// <summary>
/// The Gateway as its <c>Program</c> composes it, against a real PostgreSQL database, with Runtime's
/// incident read model replaced by <see cref="Runtime"/> and the clock fixed. No background work
/// runs: the outbox dispatcher, stream consumers and refreshers would reach Redis and the peers.
/// </summary>
public sealed class GatewayHost(string connectionString) : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset Now = new(2026, 10, 29, 9, 30, 0, TimeSpan.Zero);

    public FakeRuntimeIncidents Runtime { get; } = new();

    public FakeTimeProvider Time { get; } = new(Now);

    /// <summary>Creates the Gateway's own tables and the messaging tables, as the messaging initializer would at startup.</summary>
    public async Task InitializeStoresAsync()
    {
        using IServiceScope scope = Services.CreateScope();
        foreach (IMessagingStoreInitializer initializer in scope.ServiceProvider.GetServices<IMessagingStoreInitializer>())
        {
            await initializer.InitializeAsync(CancellationToken.None);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting("ConnectionStrings:api-db", connectionString);
        builder.UseSetting("ConnectionStrings:messaging", "localhost:6379");
        builder.ConfigureTestServices(services =>
        {
            foreach (ServiceDescriptor hosted in services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType?.Name != "GenericWebHostService").ToList())
            {
                services.Remove(hosted);
            }

            services.RemoveAll<IRuntimeIncidents>();
            services.AddSingleton<IRuntimeIncidents>(Runtime);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
        });
    }

    /// <summary>Runtime's incident read model: incidents awaiting approval of one plan, in the tenant they are added for.</summary>
    public sealed class FakeRuntimeIncidents : IRuntimeIncidents
    {
        private readonly ConcurrentDictionary<IncidentId, IncidentDetail> _incidents = new();

        public IncidentDetail Add(string tenant, PlanStatus status = PlanStatus.AwaitingApproval)
        {
            var draft = new RemediationPlan
            {
                PlanId = PlanId.New(),
                PlanVersion = 1,
                PlanHash = string.Empty,
                HypothesisId = HypothesisId.New(),
                Actions = [new RollbackAction { ActionId = ActionId.New(), TargetServiceId = ServiceId.From("checkout-service"), FromRevision = "v2.4.1", ToRevision = "v2.4.0" }],
                ExpectedRecovery = [],
                ObservationWindow = TimeSpan.FromSeconds(90),
                Risk = RiskLevel.Medium
            };
            RemediationPlan plan = draft with { PlanHash = RemediationPlanHash.Compute(draft) };
            var incidentId = IncidentId.New();
            var incident = new IncidentDetail
            {
                Summary = new IncidentSummary
                {
                    TenantId = TenantId.From(tenant),
                    EnvironmentId = EnvironmentId.From("reference"),
                    IncidentId = incidentId,
                    AffectedServiceId = ServiceId.From("checkout-service"),
                    Title = "checkout error budget burn",
                    Severity = Severity.Critical,
                    Status = status == PlanStatus.AwaitingApproval ? IncidentStatus.AwaitingApproval : IncidentStatus.Remediating,
                    OpenedAt = Now,
                    UpdatedAt = Now,
                    StreamVersion = 6
                },
                Plan = new PlanView(plan, new PolicyEvaluation(PolicyDecision.ApprovalRequired, "static-2026.10.1", ["rollback-needs-approval"], Now), status, null, null)
            };
            _incidents[incidentId] = incident;
            return incident;
        }

        /// <summary>Runtime reports that execution of the incident's plan has started.</summary>
        public void StartExecution(IncidentId incidentId)
        {
            IncidentDetail incident = _incidents[incidentId];
            _incidents[incidentId] = incident with
            {
                Execution = new ExecutionView(incident.Plan!.Plan.PlanId, incident.Plan.Plan.PlanVersion, null, null, Now, Now, null)
            };
        }

        public Task<IReadOnlyList<IncidentSummary>> ListAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IncidentSummary>>([.. _incidents.Values.Select(incident => incident.Summary).Take(limit)]);

        public Task<IncidentDetail?> GetAsync(IncidentId incidentId, CancellationToken cancellationToken) =>
            Task.FromResult(_incidents.GetValueOrDefault(incidentId));

        public Task<IncidentRunRecord?> RunRecordAsync(IncidentId incidentId, CancellationToken cancellationToken) =>
            Task.FromResult<IncidentRunRecord?>(null);
    }
}
