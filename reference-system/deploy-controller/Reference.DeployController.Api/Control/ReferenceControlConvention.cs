using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Reference.DeployController.Api.Control;

/// <summary>Removes the controllers marked <see cref="ReferenceControlAttribute"/> where failure controls are disabled, so their routes do not exist.</summary>
public sealed class ReferenceControlConvention(bool enabled) : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (enabled)
        {
            return;
        }

        foreach (ControllerModel controller in application.Controllers.Where(controller => controller.Attributes.OfType<ReferenceControlAttribute>().Any()).ToList())
        {
            application.Controllers.Remove(controller);
        }
    }
}
