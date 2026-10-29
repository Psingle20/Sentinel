using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents;

public static class IncidentErrors
{
    public static readonly Error AlreadyDecided = Error.Conflict("Incident.AlreadyDecided", "The incident already has a decision.");

    public static readonly Error PlanWithoutDiagnosis = Error.Conflict("Incident.PlanWithoutDiagnosis", "A plan must remediate the diagnosed leading hypothesis.");

    public static readonly Error StalePlanVersion = Error.Conflict("Incident.StalePlanVersion", "A new plan must have a higher version than the current one.");

    public static readonly Error NotCurrentPlan = Error.Conflict("Incident.NotCurrentPlan", "The plan is not the incident's current plan.");

    public static readonly Error PlanHashMismatch = Error.Conflict("Incident.PlanHashMismatch", "The approved hash does not match the current plan's content.");

    public static readonly Error AlreadyApproved = Error.Conflict("Incident.AlreadyApproved", "Another plan version is already approved.");

    public static readonly Error UnknownHypothesis = Error.Problem("Incident.UnknownHypothesis", "A challenged hypothesis is not among the incident's hypotheses.");

    public static Error InvalidState(IncidentState state, string action) =>
        Error.Conflict("Incident.InvalidState", $"Cannot {action} while the incident is {state}.");

    public static Error UnknownInvestigation(InvestigationId investigationId) =>
        Error.NotFound("Incident.UnknownInvestigation", $"Investigation {investigationId} was not requested for this incident.");

    public static Error StaleFencingToken(long presented, long current) =>
        Error.Conflict("Incident.StaleFencingToken", $"Fencing token {presented} does not match the current execution's {current}.");
}
