using FluentValidation;
using Sentinel.Gateway.Application.BusinessProcesses.Approvals;

namespace Sentinel.Gateway.Application.Validators.Approvals;

/// <summary>
/// The approver is required. The version and hash are judged against the incident's current plan
/// by <see cref="Domain.Approvals.Approval"/>, so a wrong one is a conflict, not a validation failure.
/// </summary>
public sealed class ApprovePlanRequestValidator : AbstractValidator<ApprovePlanRequest>
{
    public ApprovePlanRequestValidator()
    {
        RuleFor(request => request.ApproverId).NotEmpty();
    }
}
