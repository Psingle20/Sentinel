using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Sentinel.ServiceDefaults;

/// <summary>
/// Leaves controllers marked <see cref="OperatorEndpointAttribute"/> out of the application where
/// operator endpoints are disabled, so their routes are unknown, exactly as unmapped endpoints are.
/// </summary>
public sealed class OperatorEndpointConvention(bool enabled) : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (!enabled)
        {
            foreach (ControllerModel controller in application.Controllers.Where(controller => controller.ControllerType.GetCustomAttribute<OperatorEndpointAttribute>() is not null).ToList())
            {
                application.Controllers.Remove(controller);
            }
        }
    }
}
