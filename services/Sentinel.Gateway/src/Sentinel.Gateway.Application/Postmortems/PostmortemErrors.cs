using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.Postmortems;

public static class PostmortemErrors
{
    public const int MaxNarrativeLength = 200_000;

    public static readonly Error NotFound = Error.NotFound("Postmortem.NotFound", "The incident does not exist.");

    public static readonly Error VersionConflict = Error.Conflict("Postmortem.VersionConflict", "Someone else saved the narrative first; reload it and edit again.");

    public static readonly Error TooLong = Error.Problem("Postmortem.TooLong", $"The narrative is longer than {MaxNarrativeLength} characters.");
}
