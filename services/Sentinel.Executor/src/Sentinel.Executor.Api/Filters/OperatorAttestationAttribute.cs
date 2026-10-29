using Microsoft.AspNetCore.Mvc;

namespace Sentinel.Executor.Api.Filters;

/// <summary>
/// Admits an operator action only with a Gateway-signed operator attestation for <paramref name="scope"/>
/// on the action's resource, over the content submitted; see <see cref="OperatorAttestationFilter"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class OperatorAttestationAttribute : TypeFilterAttribute
{
    public OperatorAttestationAttribute(string scope)
        : base(typeof(OperatorAttestationFilter))
    {
        Scope = scope;
        Arguments = [scope];
    }

    public string Scope { get; }
}
