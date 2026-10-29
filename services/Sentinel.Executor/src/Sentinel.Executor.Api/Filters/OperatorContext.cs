using Microsoft.AspNetCore.Http;

namespace Sentinel.Executor.Api.Filters;

/// <summary>The operator an admin action runs for: the actor the verified attestation names, never one the body claims.</summary>
public static class OperatorContext
{
    public static string Actor(this HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);

        return http.Items[OperatorAttestationFilter.ActorItem] as string
            ?? throw new InvalidOperationException("The action ran without a verified operator attestation.");
    }
}
