using CleanArchitecture.BuildingBlocks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Api.Contracts;
using Sentinel.Executor.Application.Abstractions.Persistence;
using Sentinel.Executor.Application.Services;

namespace Sentinel.Executor.Api.Filters;

/// <summary>
/// The Gateway authenticates and authorizes an operator, then signs what they submitted (ADR-006); this
/// filter verifies that signature before an admin action runs, binds it to the action's resource and
/// content, consumes it so it cannot be replayed, and records the actor from the claims rather than
/// from the body. Without a valid attestation the action is refused as unauthorized, and no caller can
/// change policy or a control by naming an operator.
/// </summary>
public sealed class OperatorAttestationFilter(AttestationVerifier verifier, IExecutionStore store, string scope) : IAsyncActionFilter
{
    public const string ActorItem = "sentinel.operator.actor";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        HttpContext http = context.HttpContext;
        string? token = http.Request.Headers[ApprovalAttestation.OperatorHeader].ToString() is { Length: > 0 } header ? header : null;
        (string resource, string contentHash) = Content(context);

        Result<OperatorAttestationClaims> verified = await verifier.VerifyOperatorAsync(token, scope, resource, contentHash, http.RequestAborted);
        if (verified.IsFailure)
        {
            context.Result = Unauthorized(verified.Error.Code, verified.Error.Description);
            return;
        }

        if (!await store.TryConsumeAttestationAsync(verified.Value.AttestationId, Guid.CreateVersion7(), http.RequestAborted))
        {
            context.Result = Unauthorized("Attestation.Replayed", "The operator attestation was already used.");
            return;
        }

        http.Items[ActorItem] = verified.Value.ActorId;
        await next();
    }

    private static UnauthorizedObjectResult Unauthorized(string code, string detail) =>
        new(new ProblemDetails { Title = code, Detail = detail, Status = StatusCodes.Status401Unauthorized });

    /// <summary>The resource and content hash the Gateway signed, recomputed from the route and the bound body.</summary>
    private static (string Resource, string ContentHash) Content(ActionExecutingContext context)
    {
        context.ActionArguments.TryGetValue("body", out object? body);
        return body switch
        {
            PublishPolicyBody policy => ("policy", OperatorContentHash.Compute(new PolicyPublicationContent(PolicyJson.ToElement(policy.Document), policy.ExpectedVersion))),
            ControlChangeBody control when context.RouteData.Values.TryGetValue("actionKind", out object? kind) && kind is string actionKind
                => ($"controls/actions/{actionKind}", OperatorContentHash.Compute(new ControlChangeContent($"controls/actions/{actionKind}", control.Enabled))),
            ControlChangeBody control => ("controls/emergency-stop", OperatorContentHash.Compute(new ControlChangeContent("controls/emergency-stop", control.Enabled))),
            _ => (context.HttpContext.Request.Path.Value ?? string.Empty, string.Empty)
        };
    }
}
