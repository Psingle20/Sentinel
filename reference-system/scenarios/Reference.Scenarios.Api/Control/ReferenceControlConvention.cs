using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Reference.Scenarios.Api.Control;

/// <summary>Removes the actions marked <see cref="ReferenceControlAttribute"/> where failure controls are disabled, so their routes do not exist.</summary>
public sealed class ReferenceControlConvention(bool enabled) : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (enabled)
        {
            return;
        }

        foreach (ControllerModel controller in application.Controllers)
        {
            foreach (ActionModel action in controller.Actions.Where(action => action.Attributes.OfType<ReferenceControlAttribute>().Any()).ToList())
            {
                controller.Actions.Remove(action);
            }
        }
    }
}
