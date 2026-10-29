using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Domain.Approvals;

/// <summary>
/// An approver's decision on one immutable plan version (ADR-006). The rules that decide whether a
/// decision may be made live here: only the incident's current plan version, only while it awaits
/// approval, only with exactly the content the approver reviewed, one decision per version, and a
/// revocation only of an approval whose execution has not started. Each <c>Check</c> answers with
/// the approval that already records the same decision, which makes a repeat a no-op, or with no
/// approval when the decision is new; signing sits between the check and <see cref="Approve"/>.
/// </summary>
public sealed class Approval
{
    // Rejected and revoked are final; an approval can only be withdrawn.
    private static readonly Dictionary<ApprovalDecision, ApprovalDecision[]> Allowed = new()
    {
        [ApprovalDecision.Approved] = [ApprovalDecision.Revoked],
        [ApprovalDecision.Rejected] = [],
        [ApprovalDecision.Revoked] = []
    };

    private ApprovalSnapshot _state;

    private Approval(ApprovalSnapshot state) => _state = state;

    public Guid ApprovalId => _state.ApprovalId;

    public TenantId TenantId => _state.TenantId;

    public EnvironmentId EnvironmentId => _state.EnvironmentId;

    public IncidentId IncidentId => _state.IncidentId;

    public PlanId PlanId => _state.PlanId;

    public int PlanVersion => _state.PlanVersion;

    public string PlanHash => _state.PlanHash;

    public ApprovalDecision Decision => _state.Decision;

    public string ActorId => _state.ActorId;

    public string? Reason => _state.Reason;

    public AttestationId? AttestationId => _state.AttestationId;

    public DateTimeOffset DecidedAt => _state.DecidedAt;

    public DateTimeOffset? ExpiresAt => _state.ExpiresAt;

    public static Approval Rehydrate(ApprovalSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new Approval(snapshot);
    }

    /// <summary>
    /// Whether <paramref name="reviewed"/>, with the content hash <paramref name="reviewedHash"/>, may be
    /// approved: it must be the current version, awaiting approval, and unchanged since it was reviewed.
    /// </summary>
    public static Result<Approval?> CheckApproval(Approval? existing, IncidentState? incident, PlanReference reviewed, string reviewedHash)
    {
        Result<CurrentPlan> current = Decidable(incident, reviewed);
        if (current.IsFailure)
        {
            return Result.Failure<Approval?>(current.Error);
        }

        if (current.Value.PlanHash != reviewedHash || !current.Value.ContentMatchesHash)
        {
            return Result.Failure<Approval?>(ApprovalErrors.HashMismatch);
        }

        return Repeated(existing, ApprovalDecision.Approved);
    }

    /// <summary>Approves the incident's current plan, checked with <see cref="CheckApproval"/>, under the attestation signed for it.</summary>
    public static Approval Approve(IncidentState incident, string approverId, IssuedAttestation attestation, DateTimeOffset decidedAt)
    {
        ArgumentNullException.ThrowIfNull(attestation);

        return new(Decide(incident, ApprovalDecision.Approved, approverId, decidedAt) with
        {
            AttestationId = attestation.AttestationId,
            KeyId = attestation.KeyId,
            ExpiresAt = attestation.ExpiresAt
        });
    }

    /// <summary>Whether <paramref name="reviewed"/> may be rejected: a reason is required, and the version must be current and awaiting approval.</summary>
    public static Result<Approval?> CheckRejection(Approval? existing, IncidentState? incident, PlanReference reviewed, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure<Approval?>(ApprovalErrors.ReasonRequired);
        }

        Result<CurrentPlan> current = Decidable(incident, reviewed);
        return current.IsFailure ? Result.Failure<Approval?>(current.Error) : Repeated(existing, ApprovalDecision.Rejected);
    }

    /// <summary>Rejects the incident's current plan, checked with <see cref="CheckRejection"/>.</summary>
    public static Approval Reject(IncidentState incident, string approverId, string reason, DateTimeOffset decidedAt) =>
        new(Decide(incident, ApprovalDecision.Rejected, approverId, decidedAt) with { Reason = reason });

    /// <summary>
    /// Whether <paramref name="approval"/> may be withdrawn: a reason is required, there must be an
    /// approval of the version, and its execution must not have started.
    /// </summary>
    public static Result<Approval?> CheckRevocation(Approval? approval, IncidentState? incident, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure<Approval?>(ApprovalErrors.ReasonRequired);
        }

        if (approval?.Decision == ApprovalDecision.Revoked)
        {
            return Result.Success<Approval?>(approval);
        }

        if (approval is null || !Allowed[approval.Decision].Contains(ApprovalDecision.Revoked))
        {
            return Result.Failure<Approval?>(ApprovalErrors.NothingToRevoke);
        }

        if (incident is null)
        {
            return Result.Failure<Approval?>(ApprovalErrors.IncidentNotFound);
        }

        return incident.ExecutionStarted ? Result.Failure<Approval?>(ApprovalErrors.ExecutionStarted) : Result.Success<Approval?>(null);
    }

    /// <summary>Withdraws this approval, checked with <see cref="CheckRevocation"/>; the attestation can never take effect.</summary>
    public void Revoke(string revokedBy, string reason, DateTimeOffset revokedAt)
    {
        if (!Allowed[Decision].Contains(ApprovalDecision.Revoked))
        {
            throw new InvalidOperationException($"A {Decision} decision cannot be revoked.");
        }

        _state = _state with { Decision = ApprovalDecision.Revoked, Reason = reason, RevokedBy = revokedBy, RevokedAt = revokedAt };
    }

    public ApprovalSnapshot Snapshot() => _state;

    private static Result<CurrentPlan> Decidable(IncidentState? incident, PlanReference reviewed)
    {
        ArgumentNullException.ThrowIfNull(reviewed);

        if (incident is null)
        {
            return Result.Failure<CurrentPlan>(ApprovalErrors.IncidentNotFound);
        }

        if (incident.Plan is not { } plan || plan.PlanId != reviewed.PlanId || plan.PlanVersion != reviewed.PlanVersion)
        {
            return Result.Failure<CurrentPlan>(ApprovalErrors.NotCurrentPlan);
        }

        return plan.AwaitingApproval && incident.AwaitingApproval
            ? Result.Success(plan)
            : Result.Failure<CurrentPlan>(ApprovalErrors.NotAwaitingApproval(plan.Status));
    }

    // One decision per plan version: the same decision again is the existing approval, another one is refused.
    private static Result<Approval?> Repeated(Approval? existing, ApprovalDecision decision)
    {
        if (existing is null)
        {
            return Result.Success<Approval?>(null);
        }

        return existing.Decision == decision ? Result.Success<Approval?>(existing) : Result.Failure<Approval?>(ApprovalErrors.AlreadyDecided);
    }

    private static ApprovalSnapshot Decide(IncidentState incident, ApprovalDecision decision, string actorId, DateTimeOffset decidedAt)
    {
        ArgumentNullException.ThrowIfNull(incident);
        CurrentPlan plan = incident.Plan ?? throw new InvalidOperationException("The incident has no current plan to decide on.");

        return new ApprovalSnapshot
        {
            ApprovalId = Guid.CreateVersion7(decidedAt),
            TenantId = incident.TenantId,
            EnvironmentId = incident.EnvironmentId,
            IncidentId = incident.IncidentId,
            PlanId = plan.PlanId,
            PlanVersion = plan.PlanVersion,
            PlanHash = plan.PlanHash,
            Decision = decision,
            ActorId = actorId,
            DecidedAt = decidedAt
        };
    }
}
