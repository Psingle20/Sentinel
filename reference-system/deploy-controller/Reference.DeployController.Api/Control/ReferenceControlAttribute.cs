using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Reference.Common.Control;

namespace Reference.DeployController.Api.Control;

/// <summary>Marks the reference controls: they exist only where failure controls are enabled, and need the control token.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ReferenceControlAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.HttpContext.RequestServices.GetRequiredService<FailureControl>().IsAuthorized(context.HttpContext.Request))
        {
            context.Result = new UnauthorizedResult();
        }
    }
}
