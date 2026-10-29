using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.Postmortems;

/// <summary>Runtime's incident history (Phase 07): postmortems, similar incidents and domain replay. Api serves them and owns none of them.</summary>
public interface IRuntimeHistory
{
    Task<Postmortem?> PostmortemAsync(IncidentId incidentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SimilarIncident>?> SimilarAsync(IncidentId incidentId, CancellationToken cancellationToken);

    Task<DomainReplayResult?> ReplayAsync(IncidentId incidentId, CancellationToken cancellationToken);
}

/// <summary>A replay answer from AgentWorker: the result, or why it was refused.</summary>
public sealed record AgentReplayResponse(InvestigationReplayResult? Result, int StatusCode, string? Problem);

/// <summary>AgentWorker's agent executions and their replays (Phase 07), passed through as AgentWorker publishes them.</summary>
public interface IAgentExecutions
{
    Task<JsonElement> ForIncidentAsync(IncidentId incidentId, CancellationToken cancellationToken);

    /// <summary>One execution, or null when AgentWorker has none with that ID.</summary>
    Task<JsonElement?> GetAsync(AgentExecutionId executionId, CancellationToken cancellationToken);

    Task<AgentReplayResponse> ReplayAsync(AgentExecutionId executionId, InvestigationReplayRequest request, CancellationToken cancellationToken);
}

/// <summary>Api's own store for the human-written part of postmortems.</summary>
public interface INarrativeStore
{
    Task<PostmortemNarrative?> GetAsync(IncidentId incidentId, CancellationToken cancellationToken);

    /// <summary>Saves <paramref name="narrative"/> only if the stored version is still <paramref name="expectedVersion"/>; false when another edit won.</summary>
    Task<bool> SaveAsync(IncidentId incidentId, PostmortemNarrative narrative, int expectedVersion, CancellationToken cancellationToken);
}

public sealed record SaveNarrative(IncidentId IncidentId, string Markdown, int ExpectedVersion, string EditorId);

public static class PostmortemErrors
{
    public const int MaxNarrativeLength = 200_000;

    public static readonly Error NotFound = Error.NotFound("Postmortem.NotFound", "The incident does not exist.");

    public static readonly Error VersionConflict = Error.Conflict("Postmortem.VersionConflict", "Someone else saved the narrative first; reload it and edit again.");

    public static readonly Error TooLong = Error.Problem("Postmortem.TooLong", $"The narrative is longer than {MaxNarrativeLength} characters.");
}

/// <summary>
/// Postmortems (Phase 07): Runtime generates the structured part from the incident's history, with
/// a source for every fact; responders write the narrative, which Api stores with optimistic versioning.
/// </summary>
public sealed class PostmortemService(IRuntimeHistory history, INarrativeStore narratives, IUnitOfWork unitOfWork, TimeProvider time)
{
    public async Task<Postmortem?> GetAsync(IncidentId incidentId, CancellationToken cancellationToken)
    {
        Postmortem? postmortem = await history.PostmortemAsync(incidentId, cancellationToken);
        return postmortem is null ? null : postmortem with { Narrative = await narratives.GetAsync(incidentId, cancellationToken) };
    }

    public async Task<Result<PostmortemNarrative>> SaveNarrativeAsync(SaveNarrative command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Markdown.Length > PostmortemErrors.MaxNarrativeLength)
        {
            return Result.Failure<PostmortemNarrative>(PostmortemErrors.TooLong);
        }

        if (await history.PostmortemAsync(command.IncidentId, cancellationToken) is null)
        {
            return Result.Failure<PostmortemNarrative>(PostmortemErrors.NotFound);
        }

        var narrative = new PostmortemNarrative(command.Markdown, command.ExpectedVersion + 1, command.EditorId, time.GetUtcNow());
        if (!await narratives.SaveAsync(command.IncidentId, narrative, command.ExpectedVersion, cancellationToken))
        {
            return Result.Failure<PostmortemNarrative>(PostmortemErrors.VersionConflict);
        }

        await unitOfWork.CommitAsync(cancellationToken);
        return Result.Success(narrative);
    }
}
