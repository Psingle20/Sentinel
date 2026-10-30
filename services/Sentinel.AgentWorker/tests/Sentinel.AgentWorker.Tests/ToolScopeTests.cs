using Sentinel.AgentWorker.Application.Abstractions.Providers;
using Sentinel.AgentWorker.Application.Investigations;
using Sentinel.AgentWorker.Application.Tools;
using Sentinel.Contracts.Investigations;
using Shouldly;

namespace Sentinel.AgentWorker.Tests;

/// <summary>
/// Tenant isolation in tools: tenant, environment and window come from the investigation, so a model that names another
/// tenant, another environment or a wider window in a tool's arguments still reads only its own incident's data.
/// </summary>
public sealed class ToolScopeTests
{
    private const string Planted = "\"tenantId\":\"globex\",\"environmentId\":\"production\",\"from\":\"2026-01-01T00:00:00Z\",\"to\":\"2027-01-01T00:00:00Z\"";

    public static TheoryData<string, string> Calls => new()
    {
        { MetricRangeTool.Name, "\"metric\":\"redis_connections\",\"service\":\"checkout-service\"," },
        { LogSignaturesTool.Name, "\"service\":\"checkout-service\"," },
        { SlowSpansTool.Name, "\"service\":\"checkout-service\"," },
        { RecentDeploymentsTool.Name, "" },
        { ServiceDependenciesTool.Name, "" },
        { RunbookSearchTool.Name, "\"text\":\"redis connections\"," }
    };

    [Theory]
    [MemberData(nameof(Calls))]
    public async Task A_tool_reads_only_the_investigations_tenant_environment_and_window_whatever_the_model_asks(string tool, string arguments)
    {
        var telemetry = Telemetry.ConnectionLeak();
        string role = InvestigationRoles.Specialists.First(candidate => Specialists.For(candidate).Tools.Contains(tool));
        InvestigateIncident command = telemetry.Command(70, role);
        var model = new ScriptedModel(new ModelToolCall("c1", tool, $"{{{arguments}{Planted}}}"));

        InvestigationRun run = await Harness.Investigator(model, Harness.Catalog, Harness.AllTools(telemetry)).InvestigateAsync(command, false, CancellationToken.None);

        run.Execution.ToolCalls.ShouldHaveSingleItem().Error.ShouldBeNull();
        (telemetry.ObservationQueries.Count + telemetry.KnowledgeQueries.Count).ShouldBe(1);
        telemetry.ObservationQueries.ShouldAllBe(query => query.TenantId == Telemetry.Tenant && query.EnvironmentId == Telemetry.Environment
            && query.Range.From >= command.ObservationWindow.From && query.Range.To <= command.ObservationWindow.To);
        telemetry.KnowledgeQueries.ShouldAllBe(query => query.TenantId == Telemetry.Tenant && query.EnvironmentId == Telemetry.Environment);
    }
}
