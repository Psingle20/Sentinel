using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sentinel.AgentWorker.Application.Investigations;
using Sentinel.AgentWorker.Application.Tools;
using Sentinel.AgentWorker.Infrastructure.Models;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Shouldly;
using Sentinel.AgentWorker.Application.Abstractions.Providers;
using Sentinel.AgentWorker.Application.Diagnostics;
using Sentinel.AgentWorker.Application.Options;

namespace Sentinel.AgentWorker.Tests;

/// <summary>Builds the model investigator over the synthetic telemetry, as AgentWorker's container would.</summary>
internal static class Harness
{
    public static readonly IOptions<InvestigationOptions> Catalog = Options.Create(CatalogOptions());

    public static ModelInvestigator Investigator(Telemetry telemetry, IOptions<InvestigationOptions> options, IEnumerable<IInvestigationTool> tools, TestDoubleMode mode = TestDoubleMode.Deterministic, AgentWorkerTelemetry? metrics = null) =>
        Investigator(new DeterministicModelProvider(Options.Create(new TestDoubleOptions { Mode = mode })), options, tools, metrics);

    public static ModelInvestigator Investigator(IModelProvider model, IOptions<InvestigationOptions> options, IEnumerable<IInvestigationTool> tools, AgentWorkerTelemetry? metrics = null) =>
        new(
            new FixedRouter(model),
            tools,
            options,
            Options.Create(new ModelPricingOptions { InputPerMillionTokens = 1, OutputPerMillionTokens = 2 }),
            metrics ?? new AgentWorkerTelemetry(),
            TimeProvider.System,
            NullLogger<ModelInvestigator>.Instance);

    /// <summary>Every tool AgentWorker registers, over the synthetic telemetry.</summary>
    public static IInvestigationTool[] AllTools(Telemetry telemetry) =>
    [
        new MetricRangeTool(telemetry, Catalog),
        new RecentDeploymentsTool(telemetry),
        new LogSignaturesTool(telemetry),
        new SlowSpansTool(telemetry),
        new ServiceDependenciesTool(telemetry),
        new RunbookSearchTool(telemetry)
    ];

    internal static InvestigationOptions CatalogOptions()
    {
        var options = new InvestigationOptions();
        options.Metrics["slo_burn_rate"] = "SLO burn rate.";
        options.Metrics["redis_connections"] = "Redis connections by client.";
        options.Metrics["latency_p95"] = "p95 latency by service.";
        options.Metrics["dependency_wait_p95"] = "p95 wait on a dependency.";
        options.Metrics["request_rate"] = "Requests per second.";
        options.Metrics["queue_max_receive_count"] = "Highest receive count of a message in a queue.";
        options.Metrics["queue_processed_rate"] = "Messages processed per second.";
        options.Metrics["queue_duplicate_rate"] = "Orders booked again per second.";
        options.Metrics["queue_retry_rate"] = "Calls retried per second.";
        return options;
    }

    private sealed class FixedRouter(IModelProvider model) : IModelRouter
    {
        public IReadOnlyCollection<string> Configured => [model.Provider];

        public IModelProvider For(string role) => model;
    }
}
