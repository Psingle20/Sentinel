using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Reference.DeployController.Application.Options;

namespace Reference.DeployController.Api.Security;

/// <summary>Requires a bearer token for <see cref="Scope"/>, compared in constant time; anything else is a bare 401.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireTokenAttribute(TokenScope scope) : Attribute, IAuthorizationFilter
{
    public TokenScope Scope { get; } = scope;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        DeployControllerOptions options = context.HttpContext.RequestServices.GetRequiredService<DeployControllerOptions>();
        string?[] accepted = Scope == TokenScope.Deploy ? [options.DeployToken] : [options.ReadToken, options.DeployToken];
        string header = context.HttpContext.Request.Headers.Authorization.ToString();
        byte[] presented = Encoding.UTF8.GetBytes(header.StartsWith("Bearer ", StringComparison.Ordinal) ? header["Bearer ".Length..] : string.Empty);
        bool allowed = accepted
            .Where(token => !string.IsNullOrEmpty(token))
            .Any(token => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token!), presented));
        if (!allowed)
        {
            context.Result = new UnauthorizedResult();
        }
    }
}
