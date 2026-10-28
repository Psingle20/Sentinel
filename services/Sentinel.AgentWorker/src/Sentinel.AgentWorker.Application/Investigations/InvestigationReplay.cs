using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sentinel.AgentWorker.Application.Models;
using Sentinel.AgentWorker.Application.Tools;
using Sentinel.AgentWorker.Domain.Executions;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.AgentWorker.Application.Investigations;

/// <summary>
/// Investigation replay (Phase 07, levels 2 and 3). It runs in its own scope whose capture serves
/// only the responses the execution recorded, so no live provider is ever called; it sends no
/// message, records no new execution and changes nothing but its own replay record.
/// <list type="bullet">
/// <item>Level 2 re-runs every recorded tool call over the captured responses and compares the
/// results byte for byte: the reasoning inputs are reproduced. The investigator itself is rerun
/// only when that is deterministic (code, or a deterministic model).</item>
/// <item>Level 3 reruns the investigator with a selected model profile and prompt version against
/// the frozen inputs and compares the findings; it is deterministic only if the model is.</item>
/// </list>
/// </summary>
public sealed class InvestigationReplayService(
    IExecutionStore executions,
    IEnumerable<IInvestigator> investigators,
    IEnumerable<IInvestigationTool> tools,
    IModelRouter models,
    InvestigationCapture capture,
    TimeProvider time,
    ILogger<InvestigationReplayService> logger)
{
    public async Task<InvestigationReplayResult?> ReplayAsync(AgentExecutionId executionId, InvestigationReplayRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Level is not (ReplayLevel.Investigation or ReplayLevel.ModelReevaluation))
        {
            throw new ArgumentException($"AgentWorker replays levels {ReplayLevel.Investigation} and {ReplayLevel.ModelReevaluation}; {request.Level} belongs to another service.", nameof(request));
        }

        if (request.PromptVersion is { } prompt && !InvestigatorPrompt.IsKnown(prompt))
        {
            throw new ArgumentException($"Prompt version '{prompt}' is not one of {string.Join(", ", InvestigatorPrompt.Versions)}.", nameof(request));
        }

        if (await executions.GetAsync(executionId, cancellationToken) is not { } execution)
        {
            return null;
        }

        var replayId = Guid.CreateVersion7(time.GetUtcNow());
        ExecutionInputs? inputs = await executions.GetInputsAsync(executionId, cancellationToken);
        if (inputs is null)
        {
            return await SaveAsync(new InvestigationReplayResult
            {
                ReplayId = replayId,
                AgentExecutionId = executionId,
                Level = request.Level,
                Deterministic = false,
                InputsReproduced = false,
                ToolCallsReplayed = 0,
                Explanation = "The execution was recorded before its inputs were captured (Phase 07), so it cannot be replayed without calling live providers.",
                ReplayedAt = time.GetUtcNow()
            }, cancellationToken);
        }

        InvestigateIncident command = JsonSerializer.Deserialize<InvestigateIncident>(inputs.Command, SentinelJson.Options)!;
        capture.ServeFrom(inputs.Responses);
        if (request.Level == ReplayLevel.ModelReevaluation)
        {
            capture.ModelProfile = request.ModelProfile;
            capture.PromptVersion = request.PromptVersion;
        }

        IReadOnlyList<ToolCallMismatch> mismatches = execution.Investigator == "model" ? await ReplayToolCallsAsync(execution, command, cancellationToken) : [];
        int replayedCalls = execution.Investigator == "model" ? execution.ToolCalls.Count : 0;

        IInvestigator investigator = investigators.FirstOrDefault(candidate => candidate.Name == execution.Investigator)
            ?? throw new InvalidOperationException($"No investigator named '{execution.Investigator}' to replay.");
        bool modelDriven = execution.Investigator == "model";
        IModelProvider? model = modelDriven ? models.For(Specialists.For(command.Role).Role) : null;
        bool rerun = request.Level == ReplayLevel.ModelReevaluation || !modelDriven || model!.Deterministic;

        InvestigationRun? run = null;
        if (rerun)
        {
            try
            {
                run = await investigator.InvestigateAsync(command, execution.Shadow, cancellationToken);
            }
            catch (NotCapturedException exception)
            {
                logger.LogInformation(exception, "Replay {ReplayId} of execution {ExecutionId} needed an uncaptured query", replayId, executionId);
            }
        }

        InvestigationCompleted original = JsonSerializer.Deserialize<InvestigationCompleted>(execution.Findings, SentinelJson.Options)!;
        IReadOnlyList<string> differences = run is null ? [] : FindingsComparison.Differences(original, run.Completed);
        HashSet<ObservationId> frozen = [.. inputs.Responses.SelectMany(ObservationIdsIn)];
        ObservationId[] unsupported = run is null ? [] : [.. run.Completed.Evidence.SelectMany(evidence => evidence.SourceObservationIds).Where(id => !frozen.Contains(id)).Distinct()];
        bool deterministic = request.Level == ReplayLevel.Investigation
            ? capture.Misses == 0 && (!rerun || !modelDriven || model!.Deterministic)
            : capture.Misses == 0 && model is { Deterministic: true };

        return await SaveAsync(new InvestigationReplayResult
        {
            ReplayId = replayId,
            AgentExecutionId = executionId,
            Level = request.Level,
            Deterministic = deterministic,
            InputsReproduced = mismatches.Count == 0 && capture.Misses == 0,
            ToolCallsReplayed = replayedCalls,
            ToolCallMismatches = mismatches,
            UncapturedQueries = capture.Misses,
            FindingsMatch = run is null ? null : differences.Count == 0,
            Differences = differences,
            UnsupportedCitations = unsupported,
            Model = run?.Completed.Model ?? original.Model,
            Replayed = run?.Completed,
            Explanation = Explain(request.Level, modelDriven, rerun, run is not null, model, capture.Misses),
            ReplayedAt = time.GetUtcNow()
        }, cancellationToken);
    }

    private async Task<IReadOnlyList<ToolCallMismatch>> ReplayToolCallsAsync(AgentExecution execution, InvestigateIncident command, CancellationToken cancellationToken)
    {
        var byName = tools.ToDictionary(tool => tool.Definition.Name, StringComparer.Ordinal);
        var context = new ToolContext(command.TenantId, command.EnvironmentId, command.IncidentId, command.AffectedServiceId, command.RelatedServiceIds, command.ObservationWindow);
        var mismatches = new List<ToolCallMismatch>();
        foreach (ToolCallRecord call in execution.ToolCalls.Where(call => call.Error is null))
        {
            if (!byName.TryGetValue(call.ToolName, out IInvestigationTool? tool))
            {
                mismatches.Add(new ToolCallMismatch(call.ToolCallId, call.ToolName, "The tool no longer exists."));
                continue;
            }

            try
            {
                using var arguments = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments);
                ToolOutcome outcome = await tool.InvokeAsync(context, arguments.RootElement, cancellationToken);
                if (!string.Equals(outcome.Content, call.Result, StringComparison.Ordinal))
                {
                    mismatches.Add(new ToolCallMismatch(call.ToolCallId, call.ToolName, "The replayed result differs from the recorded one."));
                }
            }
            catch (NotCapturedException exception)
            {
                mismatches.Add(new ToolCallMismatch(call.ToolCallId, call.ToolName, exception.Message));
            }
        }

        return mismatches;
    }

    private async Task<InvestigationReplayResult> SaveAsync(InvestigationReplayResult result, CancellationToken cancellationToken)
    {
        await executions.SaveReplayAsync(result, cancellationToken);
        return result;
    }

    private static IEnumerable<ObservationId> ObservationIdsIn(CapturedResponse response)
    {
        using var document = JsonDocument.Parse(response.Response);
        if (!document.RootElement.TryGetProperty("observations", out JsonElement observations) || observations.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. observations.EnumerateArray()
            .Where(observation => observation.TryGetProperty("observationId", out _))
            .Select(observation => ObservationId.From(observation.GetProperty("observationId").GetString()!))];
    }

    private static string Explain(ReplayLevel level, bool modelDriven, bool rerun, bool completed, IModelProvider? model, int misses)
    {
        string inputs = misses == 0
            ? "Every provider query was served from the captured responses; no live provider was called."
            : string.Create(CultureInfo.InvariantCulture, $"{misses} provider query(ies) were not among the captured responses and failed explicitly; no live provider was called.");
        string investigator = (modelDriven, rerun, completed) switch
        {
            (false, _, true) => " The investigator is deterministic code and was rerun over the frozen inputs.",
            (true, false, _) => $" The model {model?.Provider}/{model?.Model} is not deterministic, so it was not called: level 2 reproduces its inputs, not its output.",
            (true, true, true) when level == ReplayLevel.Investigation => $" The deterministic model {model?.Provider}/{model?.Model} was rerun over the frozen inputs.",
            (true, true, true) => $" Reevaluated with {model?.Provider}/{model?.Model}{(model?.Deterministic == true ? ", a deterministic model" : ", a live model: this result is not reproducible")}.",
            _ => " The rerun stopped on a query outside the frozen inputs."
        };
        return inputs + investigator;
    }
}

/// <summary>Compares two findings by meaning: identifiers minted per run are ignored, everything stated is compared.</summary>
public static class FindingsComparison
{
    public static IReadOnlyList<string> Differences(InvestigationCompleted recorded, InvestigationCompleted replayed)
    {
        ArgumentNullException.ThrowIfNull(recorded);
        ArgumentNullException.ThrowIfNull(replayed);

        var differences = new List<string>();
        if (recorded.Outcome != replayed.Outcome)
        {
            differences.Add($"Outcome {recorded.Outcome} was replayed as {replayed.Outcome}.");
        }

        Compare("hypothesis", recorded.Hypotheses.Select(Hypothesis), replayed.Hypotheses.Select(Hypothesis), differences);
        Compare("evidence", recorded.Evidence.Select(Evidence), replayed.Evidence.Select(Evidence), differences);
        Compare("challenge", (recorded.Challenges ?? []).Select(challenge => Challenge(challenge, recorded)), (replayed.Challenges ?? []).Select(challenge => Challenge(challenge, replayed)), differences);
        Compare("unknown", recorded.Unknowns ?? [], replayed.Unknowns ?? [], differences);
        return differences;
    }

    private static string Hypothesis(HypothesisFinding hypothesis) =>
        $"{hypothesis.Key ?? hypothesis.Statement} | {hypothesis.SuspectedServiceId} | {hypothesis.SuspectedChangeObservationId}";

    private static string Evidence(EvidenceFinding evidence) =>
        string.Create(CultureInfo.InvariantCulture, $"{evidence.Statement} | {evidence.Direction} | {evidence.Strength:F3} | {string.Join(",", evidence.SourceObservationIds.Select(id => id.ToString()).Order(StringComparer.Ordinal))} | {evidence.IndependenceGroup}");

    /// <summary>A challenge of the run's own hypothesis is named by that hypothesis, whose ID is minted per run; a challenged input hypothesis keeps its ID.</summary>
    private static string Challenge(ChallengeFinding challenge, InvestigationCompleted findings)
    {
        HypothesisFinding? own = findings.Hypotheses.FirstOrDefault(hypothesis => hypothesis.HypothesisId == challenge.HypothesisId);
        return $"{(own is null ? challenge.HypothesisId.ToString() : Hypothesis(own))} | {challenge.Falsifier} | {challenge.Verdict}";
    }

    private static void Compare(string kind, IEnumerable<string> recorded, IEnumerable<string> replayed, List<string> differences)
    {
        List<string> left = [.. recorded];
        List<string> right = [.. replayed];
        foreach (string item in left.ToList())
        {
            if (right.Remove(item))
            {
                left.Remove(item);
            }
        }

        differences.AddRange(left.Select(item => $"Recorded {kind} missing from the replay: {item}"));
        differences.AddRange(right.Select(item => $"Replay added a {kind}: {item}"));
    }
}
