using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Reference.Common.Control;

namespace Reference.Scenarios.Api.Control;

/// <summary>Marks a control: it exists only where failure controls are enabled, and needs the control token.</summary>
[AttributeUsage(AttributeTargets.Method)]
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
