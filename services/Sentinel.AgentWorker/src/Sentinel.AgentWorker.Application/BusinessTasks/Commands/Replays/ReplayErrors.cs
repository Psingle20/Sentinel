using CleanArchitecture.BuildingBlocks;
using Sentinel.AgentWorker.Application.Investigations;
using Sentinel.Contracts.Incidents;

namespace Sentinel.AgentWorker.Application.BusinessTasks.Commands.Replays;

/// <summary>
/// Why AgentWorker refuses a replay request. The descriptions keep the wording the replay endpoint has
/// always answered with, which names the offending parameter.
/// </summary>
public static class ReplayErrors
{
    public static Error UnsupportedLevel(ReplayLevel level) =>
        new("Replay.UnsupportedLevel", OfRequest($"AgentWorker replays levels {ReplayLevel.Investigation} and {ReplayLevel.ModelReevaluation}; {level} belongs to another service."), ErrorType.Validation);

    public static Error UnknownPrompt(string promptVersion) =>
        new("Replay.UnknownPrompt", OfRequest($"Prompt version '{promptVersion}' is not one of {string.Join(", ", InvestigatorPrompt.Versions)}."), ErrorType.Validation);

    private static string OfRequest(string reason) => $"{reason} (Parameter 'request')";
}
