using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Sentinel.Gateway.Api.Controllers;

/// <summary>Who the caller is: their ID, roles and tenants.</summary>
[Route(Routes.Root + "/me")]
[Authorize(Roles = Roles.Responder)]
public sealed class MeController : ApiController
{
    [HttpGet]
    public IResult Get() => Results.Ok(new
    {
        Id = User.FindFirstValue(ClaimTypes.NameIdentifier),
        Roles = User.FindAll(ClaimTypes.Role).Select(role => role.Value),
        Tenants = User.Tenants()
    });
}