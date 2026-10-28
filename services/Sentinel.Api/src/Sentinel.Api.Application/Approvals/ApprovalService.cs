using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.Api.Domain.Approvals;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Api.Application.Approvals;

/// <summary>Runtime's incident read model, which Api serves and never owns (ADR-001).</summary>
public interface IRuntimeIncidents
{
    Task<IReadOnlyList<IncidentSummary>> ListAsync(int limit, CancellationToken cancellationToken);

    Task<IncidentDetail?> GetAsync(IncidentId incidentId, CancellationToken cancellationToken);

    Task<IncidentRunRecord?> RunRecordAsync(IncidentId incidentId, CancellationToken cancellationToken);
}

public sealed record SignedAttestation(string Token, string KeyId);

/// <summary>
/// The Sentinel-owned signing interface (ADR-006). Locally a generated key pair; in production a
/// managed signing facility whose private key never leaves it.
/// </summary>
public interface IApprovalSigner
{
    Task<SignedAttestation> SignAsync(ApprovalAttestationClaims claims, CancellationToken cancellationToken);

    Task<ApprovalKeySet> PublishedKeysAsync(CancellationToken cancellationToken);
}

public interface IApprovalStore
{
    Task<ApprovalRecord?> FindAsync(IncidentId incidentId, PlanId planId, int planVersion, CancellationToken cancellationToken);

    Task SaveAsync(ApprovalRecord record, CancellationToken cancellationToken);
}

/// <summary>Configured as <c>Api:Approvals</c>.</summary>
public sealed class ApprovalOptions
{
    public const string Section = "Api:Approvals";

    /// <summary>How long an attestation is valid; Executor refuses it afterwards.</summary>
    public TimeSpan AttestationLifetime { get; set; } = TimeSpan.FromMinutes(15);
}

public sealed record ApprovePlan(IncidentId IncidentId, PlanId PlanId, int PlanVersion, string PlanHash, string ApproverId);

public sealed record RejectPlan(IncidentId IncidentId, PlanId PlanId, int PlanVersion, string Reason, string ApproverId);

public sealed record RevokeApproval(IncidentId IncidentId, PlanId PlanId, int PlanVersion, string Reason, string ApproverId);

public sealed record ApprovalReceipt(Guid ApprovalId, ApprovalDecision Decision, AttestationId? AttestationId, DateTimeOffset DecidedAt, DateTimeOffset? ExpiresAt);

/// <summary>
/// Approval capture (ADR-006): check that the approver saw the incident's current plan, exactly,
/// sign a short-lived single-use attestation bound to its ID, version and content hash, and relay
/// it to Runtime. Api never sends anything to Executor.
/// </summary>
public sealed class ApprovalService(
    IRuntimeIncidents incidents,
    IApprovalSigner signer,
    IApprovalStore approvals,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    IOptions<ApprovalOptions> options,
    ILogger<ApprovalService> logger)
{
    public async Task<Result<ApprovalReceipt>> ApproveAsync(ApprovePlan command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<(IncidentDetail Incident, PlanView Plan)> current = await CurrentPlanAsync(command.IncidentId, command.PlanId, command.PlanVersion, cancellationToken);
        if (current.IsFailure)
        {
            return Result.Failure<ApprovalReceipt>(current.Error);
        }

        (IncidentDetail incident, PlanView plan) = current.Value;
        if (plan.Plan.PlanHash != command.PlanHash || !RemediationPlanHash.Matches(plan.Plan))
        {
            return Result.Failure<ApprovalReceipt>(ApprovalErrors.HashMismatch);
        }

        if (await approvals.FindAsync(command.IncidentId, command.PlanId, command.PlanVersion, cancellationToken) is { } existing)
        {
            return existing.Decision == ApprovalDecision.Approved
                ? Result.Success(Receipt(existing))
                : Result.Failure<ApprovalReceipt>(ApprovalErrors.AlreadyDecided);
        }

        DateTimeOffset now = time.GetUtcNow();
        var claims = new ApprovalAttestationClaims
        {
            AttestationId = AttestationId.New(),
            TenantId = incident.Summary.TenantId,
            EnvironmentId = incident.Summary.EnvironmentId,
            IncidentId = command.IncidentId,
            PlanId = command.PlanId,
            PlanVersion = command.PlanVersion,
            PlanHash = command.PlanHash,
            ApproverId = command.ApproverId,
            ApprovalScope = ApprovalAttestation.ExecutePlanScope,
            PolicyVersion = plan.PolicyEvaluation.PolicyVersion,
            IssuedAt = now,
            ExpiresAt = now + options.Value.AttestationLifetime,
            Issuer = ApprovalAttestation.Issuer,
            Audience = ApprovalAttestation.Audience
        };
        SignedAttestation signed = await signer.SignAsync(claims, cancellationToken);

        var record = new ApprovalRecord
        {
            ApprovalId = Guid.CreateVersion7(now),
            TenantId = claims.TenantId,
            EnvironmentId = claims.EnvironmentId,
            IncidentId = command.IncidentId,
            PlanId = command.PlanId,
            PlanVersion = command.PlanVersion,
            PlanHash = command.PlanHash,
            Decision = ApprovalDecision.Approved,
            ActorId = command.ApproverId,
            AttestationId = claims.AttestationId,
            KeyId = signed.KeyId,
            DecidedAt = now,
            ExpiresAt = claims.ExpiresAt
        };
        await approvals.SaveAsync(record, cancellationToken);
        outbox.Enqueue(
            new RecordApproval
            {
                TenantId = claims.TenantId,
                EnvironmentId = claims.EnvironmentId,
                IncidentId = command.IncidentId,
                PlanId = command.PlanId,
                PlanVersion = command.PlanVersion,
                PlanHash = command.PlanHash,
                Attestation = signed.Token,
                ApproverId = command.ApproverId,
                ApprovedAt = now
            },
            Metadata(incident));
        await unitOfWork.CommitAsync(cancellationToken);

        logger.LogInformation("{Approver} approved plan {PlanId} v{Version} for incident {IncidentId} with attestation {AttestationId}", command.ApproverId, command.PlanId, command.PlanVersion, command.IncidentId, claims.AttestationId);
        return Result.Success(Receipt(record));
    }

    public async Task<Result<ApprovalReceipt>> RejectAsync(RejectPlan command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Result.Failure<ApprovalReceipt>(ApprovalErrors.ReasonRequired);
        }

        Result<(IncidentDetail Incident, PlanView Plan)> current = await CurrentPlanAsync(command.IncidentId, command.PlanId, command.PlanVersion, cancellationToken);
        if (current.IsFailure)
        {
            return Result.Failure<ApprovalReceipt>(current.Error);
        }

        if (await approvals.FindAsync(command.IncidentId, command.PlanId, command.PlanVersion, cancellationToken) is { } existing)
        {
            return existing.Decision == ApprovalDecision.Rejected
                ? Result.Success(Receipt(existing))
                : Result.Failure<ApprovalReceipt>(ApprovalErrors.AlreadyDecided);
        }

        IncidentDetail incident = current.Value.Incident;
        DateTimeOffset now = time.GetUtcNow();
        var record = new ApprovalRecord
        {
            ApprovalId = Guid.CreateVersion7(now),
            TenantId = incident.Summary.TenantId,
            EnvironmentId = incident.Summary.EnvironmentId,
            IncidentId = command.IncidentId,
            PlanId = command.PlanId,
            PlanVersion = command.PlanVersion,
            PlanHash = current.Value.Plan.Plan.PlanHash,
            Decision = ApprovalDecision.Rejected,
            ActorId = command.ApproverId,
            Reason = command.Reason,
            DecidedAt = now
        };
        await approvals.SaveAsync(record, cancellationToken);
        outbox.Enqueue(
            new RecordRejection
            {
                TenantId = incident.Summary.TenantId,
                EnvironmentId = incident.Summary.EnvironmentId,
                IncidentId = command.IncidentId,
                PlanId = command.PlanId,
                PlanVersion = command.PlanVersion,
                RejectedBy = command.ApproverId,
                Reason = command.Reason,
                RejectedAt = now
            },
            Metadata(incident));
        await unitOfWork.CommitAsync(cancellationToken);
        return Result.Success(Receipt(record));
    }

    /// <summary>
    /// Withdraws an approval before execution starts (Phase 06). Api marks its record revoked and
    /// tells Runtime, which refuses any start confirmation; the attestation can never take effect.
    /// </summary>
    public async Task<Result<ApprovalReceipt>> RevokeAsync(RevokeApproval command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Result.Failure<ApprovalReceipt>(ApprovalErrors.ReasonRequired);
        }

        if (await approvals.FindAsync(command.IncidentId, command.PlanId, command.PlanVersion, cancellationToken) is not { } existing || existing.Decision == ApprovalDecision.Rejected)
        {
            return Result.Failure<ApprovalReceipt>(ApprovalErrors.NothingToRevoke);
        }

        if (existing.Decision == ApprovalDecision.Revoked)
        {
            return Result.Success(Receipt(existing));
        }

        IncidentDetail? incident = await incidents.GetAsync(command.IncidentId, cancellationToken);
        if (incident is null)
        {
            return Result.Failure<ApprovalReceipt>(ApprovalErrors.IncidentNotFound);
        }

        if (incident.Execution?.StartedAt is not null)
        {
            return Result.Failure<ApprovalReceipt>(ApprovalErrors.ExecutionStarted);
        }

        DateTimeOffset now = time.GetUtcNow();
        ApprovalRecord revoked = existing with { Decision = ApprovalDecision.Revoked, Reason = command.Reason, RevokedBy = command.ApproverId, RevokedAt = now };
        await approvals.SaveAsync(revoked, cancellationToken);
        outbox.Enqueue(
            new RecordRevocation
            {
                TenantId = existing.TenantId,
                EnvironmentId = existing.EnvironmentId,
                IncidentId = command.IncidentId,
                PlanId = command.PlanId,
                PlanVersion = command.PlanVersion,
                RevokedBy = command.ApproverId,
                Reason = command.Reason,
                RevokedAt = now
            },
            Metadata(incident));
        await unitOfWork.CommitAsync(cancellationToken);

        logger.LogInformation("{Approver} revoked approval {ApprovalId} of plan {PlanId} v{Version}", command.ApproverId, existing.ApprovalId, command.PlanId, command.PlanVersion);
        return Result.Success(Receipt(revoked));
    }

    private async Task<Result<(IncidentDetail Incident, PlanView Plan)>> CurrentPlanAsync(IncidentId incidentId, PlanId planId, int planVersion, CancellationToken cancellationToken)
    {
        IncidentDetail? incident = await incidents.GetAsync(incidentId, cancellationToken);
        if (incident is null)
        {
            return Result.Failure<(IncidentDetail, PlanView)>(ApprovalErrors.IncidentNotFound);
        }

        if (incident.Plan is not { } plan || plan.Plan.PlanId != planId || plan.Plan.PlanVersion != planVersion)
        {
            return Result.Failure<(IncidentDetail, PlanView)>(ApprovalErrors.NotCurrentPlan);
        }

        return plan.Status == PlanStatus.AwaitingApproval && incident.Summary.Status == IncidentStatus.AwaitingApproval
            ? Result.Success((incident, plan))
            : Result.Failure<(IncidentDetail, PlanView)>(ApprovalErrors.NotAwaitingApproval(plan.Status));
    }

    private static MessageMetadata Metadata(IncidentDetail incident) => new(
        CorrelationId: incident.Summary.IncidentId.ToString(),
        Headers: SentinelHeaders.For(incident.Summary.TenantId, incident.Summary.EnvironmentId, incident.Summary.AffectedServiceId, incident.Summary.IncidentId));

    private static ApprovalReceipt Receipt(ApprovalRecord record) =>
        new(record.ApprovalId, record.Decision, record.AttestationId, record.DecidedAt, record.ExpiresAt);
}

public static class ApprovalErrors
{
    public static readonly Error IncidentNotFound = Error.NotFound("Approval.IncidentNotFound", "The incident does not exist.");

    public static readonly Error NotCurrentPlan = Error.Conflict("Approval.NotCurrentPlan", "The plan is not the incident's current plan version; review the current plan.");

    public static readonly Error HashMismatch = Error.Conflict("Approval.HashMismatch", "The plan content changed since it was reviewed; review the current plan.");

    public static readonly Error AlreadyDecided = Error.Conflict("Approval.AlreadyDecided", "This plan version already has a different decision.");

    public static readonly Error ReasonRequired = Error.Problem("Approval.ReasonRequired", "A rejection or revocation needs a reason.");

    public static readonly Error NothingToRevoke = Error.Conflict("Approval.NothingToRevoke", "There is no approval of this plan version to revoke.");

    public static readonly Error ExecutionStarted = Error.Conflict("Approval.ExecutionStarted", "Execution already started; the approval can no longer be revoked. Use the emergency stop.");

    public static Error NotAwaitingApproval(PlanStatus status) =>
        Error.Conflict("Approval.NotAwaitingApproval", $"The plan is {status}, not awaiting approval.");
}
