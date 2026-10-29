using System.Text.Json;
using Microsoft.Extensions.Options;
using Sentinel.AgentWorker.Application.Abstractions.Providers;
using Sentinel.AgentWorker.Application.Investigations;
using Sentinel.AgentWorker.Application.Options;
using Sentinel.Contracts.Investigations;
using Shouldly;

namespace Sentinel.AgentWorker.Tests;

/// <summary>
/// Tool results that state their call (prompt v4). A live model asked to cite the call behind each evidence item had
/// only the API's internal call IDs to go by, cited none it could see, and every specialist's findings were refused.
/// </summary>
public sealed class InvestigatorPromptTests
{
    [Fact]
    public void From_v4_a_tool_result_opens_with_the_call_that_returned_it()
    {
        string labelled = InvestigatorPrompt.ToolResult("call_7", """{"changes":[{"id":"obs:1"}]}""", InvestigatorPrompt.V4);

        labelled.ShouldStartWith("""{"toolCallId":"call_7",""");
        using (var document = JsonDocument.Parse(labelled))
        {
            document.RootElement.GetProperty("changes")[0].GetProperty("id").GetString().ShouldBe("obs:1");
        }

        InvestigatorPrompt.ToolResult("call_7", "The observation query failed.", InvestigatorPrompt.V4)
            .ShouldBe("""{"toolCallId":"call_7","content":"The observation query failed."}""");
        InvestigatorPrompt.ToolResult("call_7", """{"changes":[]}""", InvestigatorPrompt.V3).ShouldBe("""{"changes":[]}""", "earlier versions replay exactly as they ran");
    }

    [Theory]
    [InlineData(InvestigatorPrompt.V3, InvestigationOutcome.Abstained)]
    [InlineData(InvestigatorPrompt.V4, InvestigationOutcome.Completed)]
    public async Task A_model_that_can_cite_only_what_it_reads_cites_the_call_from_v4(string version, InvestigationOutcome expected)
    {
        var telemetry = Telemetry.ConnectionLeak();
        InvestigationOptions settings = Harness.CatalogOptions();
        settings.PromptVersion = version;

        InvestigationRun run = await Harness.Investigator(new CitingModel(), Options.Create(settings), Harness.AllTools(telemetry))
            .InvestigateAsync(telemetry.Command(70, InvestigationRoles.Changes), false, CancellationToken.None);

        run.Completed.Outcome.ShouldBe(expected);
        if (expected == InvestigationOutcome.Completed)
        {
            run.Completed.Evidence.ShouldHaveSingleItem();
        }
        else
        {
            run.Completed.AbstentionReason.ShouldNotBeNull().ShouldContain("must name the tool call");
        }
    }

    /// <summary>
    /// A model that cites only what it reads: it asks for recent deployments, then cites the first change listed and the
    /// call ID the result states, or a description of the call when the result states none.
    /// </summary>
    private sealed class CitingModel : IModelProvider
    {
        public string Provider => "citing";

        public string Model => "citing";

        public string? ModelVersion => null;

        public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
        {
            ModelMessage? result = request.Messages.LastOrDefault(message => message.Role == ModelRole.Tool);
            if (result is null)
            {
                return Task.FromResult(new ModelResponse(ModelFinish.ToolCalls, [new ModelToolCall("opaque-1", "recent_deployments", "{}")], null, new ModelUsage(10, 10)));
            }

            using var document = JsonDocument.Parse(result.Content);
            string cited = document.RootElement.TryGetProperty("toolCallId", out JsonElement id) ? id.GetString()! : "the recent deployments call";
            string change = document.RootElement.GetProperty("changes")[0].GetProperty("id").GetString()!;
            string findings = $$"""
                {"abstain":false,"abstentionReason":null,"unknowns":[],
                 "evidence":[{"id":"e1","statement":"A deployment served before the symptoms.","direction":"supports","strength":0.6,
                              "observationIds":["{{change}}"],"hypothesisIds":["h1"],"from":"2026-10-28T09:00:00Z","to":"2026-10-28T09:00:20Z",
                              "toolCallId":"{{cited}}","independenceGroup":"changes"}],
                 "hypotheses":[{"id":"h1","statement":"The deployment caused the symptoms.","causalChain":["deploy","burn"],"suspectedService":"checkout-service","supportingEvidenceIds":["e1"]},
                               {"id":"h2","statement":"A change in load caused the symptoms.","causalChain":["load","burn"]}]}
                """;
            return Task.FromResult(new ModelResponse(ModelFinish.Final, [], findings, new ModelUsage(10, 10)));
        }
    }
}
