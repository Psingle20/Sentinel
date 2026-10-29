using FluentValidation;
using Sentinel.Gateway.Application.BusinessProcesses.Approvals;

namespace Sentinel.Gateway.Application.Validators.Approvals;

/// <summary>The approver is required; a missing reason is <see cref="Domain.Approvals.ApprovalErrors.ReasonRequired"/>, whose code the API keeps.</summary>
public sealed class RejectPlanRequestValidator : AbstractValidator<RejectPlanRequest>
{
    public RejectPlanRequestValidator()
    {
        RuleFor(request => request.ApproverId).NotEmpty();
    }
}
