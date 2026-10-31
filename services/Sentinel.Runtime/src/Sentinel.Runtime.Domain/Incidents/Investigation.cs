using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents.Events;

namespace Sentinel.Runtime.Domain.Incidents;

/// <summary>
/// The incident's investigations (Phases 04 and 05), a child of <see cref="Incident"/>: who was asked,
/// what they reported, who timed out, and the evidence blackboard built from the reports. Runtime,
/// not the agent, decides what becomes evidence.
/// </summary>
internal sealed class Investigation
{
    private static readonly Dictionary<InvestigationState, InvestigationState[]> Transitions = new()
    {
        [InvestigationState.NotStarted] = [InvestigationState.Pending],
        [InvestigationState.Pending] = [InvestigationState.Pending, InvestigationState.Settled],
        [InvestigationState.Settled] = [InvestigationState.Pending, InvestigationState.Settled]
    };

    private readonly Dictionary<InvestigationId, InvestigationRequested> _requested = [];
    private readonly List<InvestigationReported> _reports = [];
    private readonly HashSet<InvestigationId> _timedOut = [];
    private HypothesisBlackboard? _blackboard;

    public InvestigationState State
    {
        get
        {
            if (_requested.Count == 0)
            {
                return InvestigationState.NotStarted;
            }

            return Pending.Count > 0 ? InvestigationState.Pending : InvestigationState.Settled;
        }
    }

    public IReadOnlyCollection<InvestigationRequested> Requested => _requested.Values;

    public IReadOnlyList<InvestigationReported> Reports => _reports;

    public IReadOnlyCollection<InvestigationId> TimedOut => _timedOut;

    /// <summary>Requested investigations that have neither reported nor timed out.</summary>
    public IReadOnlyList<InvestigationRequested> Pending =>
        [.. _requested.Values.Where(requested => !_timedOut.Contains(requested.InvestigationId) && !_reports.Exists(report => report.Report.InvestigationId == requested.InvestigationId))];

    public HypothesisBlackboard Blackboard => _blackboard ??= HypothesisBlackboard.Build(_reports);

    public bool Has(InvestigationId investigationId) => _requested.ContainsKey(investigationId);

    public bool IsPending(InvestigationId investigationId) => Pending.Any(pending => pending.InvestigationId == investigationId);

    public IReadOnlyList<Evidence> AcceptedEvidence() =>
        [.. _reports.SelectMany(report => report.Report.Evidence
            .Where(evidence => !report.RejectedEvidence.Any(rejected => rejected.EvidenceId == evidence.EvidenceId)))
            .Select(Blackboard.Remap)];

    public IReadOnlyList<Hypothesis> AllHypotheses() => Blackboard.Competing;

    /// <summary>An assignment; a challenge must name hypotheses the blackboard knows.</summary>
    public Result<InvestigationRequested> Request(
        InvestigationId investigationId,
        string objective,
        DateTimeOffset windowFrom,
        DateTimeOffset windowTo,
        DateTimeOffset now,
        string? role,
        IReadOnlyList<HypothesisId>? challengedHypothesisIds,
        IReadOnlyList<PriorIncident>? similarIncidents,
        EscalationState? escalation = null)
    {
        if (challengedHypothesisIds is { Count: > 0 } challenged && challenged.Any(id => !AllHypotheses().Any(hypothesis => hypothesis.HypothesisId == id)))
        {
            return Result.Failure<InvestigationRequested>(IncidentErrors.UnknownHypothesis);
        }

        Require(InvestigationState.Pending);
        return Result.Success(new InvestigationRequested(investigationId, objective, windowFrom, windowTo, now, role, challengedHypothesisIds, similarIncidents is { Count: > 0 } ? similarIncidents : null, escalation));
    }

    /// <summary>
    /// Findings, or null for a repeated report. Evidence that cites no observation, has an inverted
    /// time range or a strength outside 0 to 1, or points at an unknown hypothesis is rejected.
    /// </summary>
    public Result<InvestigationReported?> Record(InvestigationReport report, DateTimeOffset now)
    {
        if (!_requested.ContainsKey(report.InvestigationId))
        {
            return Result.Failure<InvestigationReported?>(IncidentErrors.UnknownInvestigation(report.InvestigationId));
        }

        if (_reports.Exists(existing => existing.Report.InvestigationId == report.InvestigationId))
        {
            return Result.Success<InvestigationReported?>(null);
        }

        // Evidence may link to the report's own hypotheses or to any already on the blackboard,
        // which is how the verifier's findings attach to the hypotheses it challenges.
        HashSet<HypothesisId> hypotheses = [.. report.Hypotheses.Select(hypothesis => hypothesis.HypothesisId), .. Blackboard.All.SelectMany(entry => entry.Lineage.Append(entry.Hypothesis.HypothesisId))];
        var rejected = new List<RejectedEvidence>();
        foreach (Evidence evidence in report.Evidence)
        {
            string? reason = evidence switch
            {
                { SourceObservationIds.Count: 0 } => "cites no observation",
                _ when evidence.To < evidence.From => "has an inverted time range",
                _ when evidence.Strength is < 0 or > 1 => "has a strength outside 0 to 1",
                _ when evidence.HypothesisIds.Any(id => !hypotheses.Contains(id)) => "refers to an unknown hypothesis",
                _ => null
            };

            if (reason is not null)
            {
                rejected.Add(new RejectedEvidence(evidence.EvidenceId, reason));
            }
        }

        return Result.Success<InvestigationReported?>(new InvestigationReported(report, rejected, now));
    }

    public void Apply(IIncidentEvent @event)
    {
        switch (@event)
        {
            case InvestigationRequested requested:
                _requested[requested.InvestigationId] = requested;
                break;
            case InvestigationReported reported:
                _reports.Add(reported);
                _blackboard = null;
                break;
            case InvestigationTimedOut timedOut:
                _timedOut.Add(timedOut.InvestigationId);
                break;
            case IncidentReopened:
                // A new cycle starts with an empty blackboard; earlier reports stay in the stream.
                _reports.Clear();
                _blackboard = null;
                break;
        }
    }

    private void Require(InvestigationState next)
    {
        if (!Transitions[State].Contains(next))
        {
            throw new InvalidOperationException($"Investigations cannot move from {State} to {next}.");
        }
    }
}
