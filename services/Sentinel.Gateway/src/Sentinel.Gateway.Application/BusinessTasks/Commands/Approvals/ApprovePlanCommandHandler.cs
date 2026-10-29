using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.Gateway.Application.Abstractions.Persistence;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Gateway.Application.Abstractions.Security;
using Sentinel.Gateway.Application.Options;
using Sentinel.Gateway.Domain.Approvals;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Gateway.Application.BusinessProcesses.Approvals;

namespace Sentinel.Gateway.Application.BusinessTasks.Commands.Approvals;

/// <summary>
/// Approval capture (ADR-006): once the approval rules allow it, sign a short-lived single-use
/// attestation bound to the plan's ID, version and content hash, keep the approval and relay it to
/// Runtime. The Gateway never sends anything to Executor.
/// </summary>
public sealed class ApprovePlanCommandHandler(
    IRuntimeIncidents incidents,
    IApprovalSigner signer,
    IApprovalStore approvals,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    IOptions<ApprovalOptions> options,
    ILogger<ApprovePlanCommandHandler> logger) : ICommandHandler<ApprovePlanCommand, ApprovalReceipt>
{
    public async Task<Result<ApprovalReceipt>> Handle(ApprovePlanCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        IncidentDetail? incident = await incidents.GetAsync(command.IncidentId, cancellationToken);
        IncidentState? state = ApprovalMapping.State(incident);
        Approval? existing = await approvals.FindAsync(command.IncidentId, command.PlanId, command.PlanVersion, cancellationToken);
        Result<Approval?> allowed = Approval.CheckApproval(existing, state, new PlanReference(command.PlanId, command.PlanVersion), command.PlanHash);
        if (allowed.IsFailure)
        {
            return Result.Failure<ApprovalReceipt>(allowed.Error);
        }

        if (allowed.Value is { } repeated)
        {
            return Result.Success(ApprovalMapping.Receipt(repeated));
        }

        DateTimeOffset now = time.GetUtcNow();
        var claims = new ApprovalAttestationClaims
        {
            AttestationId = AttestationId.New(),
            TenantId = state!.TenantId,
            EnvironmentId = state.EnvironmentId,
            IncidentId = command.IncidentId,
            PlanId = command.PlanId,
            PlanVersion = command.PlanVersion,
            PlanHash = command.PlanHash,
            ApproverId = command.ApproverId,
            ApprovalScope = ApprovalAttestation.ExecutePlanScope,
            PolicyVersion = incident!.Plan!.PolicyEvaluation.PolicyVersion,
            IssuedAt = now,
            ExpiresAt = now + options.Value.AttestationLifetime,
            Issuer = ApprovalAttestation.Issuer,
            Audience = ApprovalAttestation.Audience
        };
        SignedAttestation signed = await signer.SignAsync(claims, cancellationToken);

        var approval = Approval.Approve(state, command.ApproverId, new IssuedAttestation(claims.AttestationId, signed.KeyId, claims.ExpiresAt), now);
        await approvals.SaveAsync(approval, cancellationToken);
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
            ApprovalMapping.Metadata(incident));
        await unitOfWork.CommitAsync(cancellationToken);

        logger.LogInformation("{Approver} approved plan {PlanId} v{Version} for incident {IncidentId} with attestation {AttestationId}", command.ApproverId, command.PlanId, command.PlanVersion, command.IncidentId, claims.AttestationId);
        return Result.Success(ApprovalMapping.Receipt(approval));
    }
}
