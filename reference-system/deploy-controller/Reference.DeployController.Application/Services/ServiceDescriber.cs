using Reference.DeployController.Application.Abstractions;
using Reference.DeployController.Application.BusinessProcesses.Services;
using Reference.DeployController.Domain.Revisions;

namespace Reference.DeployController.Application.Services;

/// <summary>Describes a managed service with the revision it serves; an unreachable service is described with the error instead.</summary>
public sealed class ServiceDescriber(LedgerState ledger, IRevisionSwitch revisions)
{
    public async Task<ServiceView> DescribeAsync(ManagedRevisions managed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(managed);

        DeploymentRecord? active = ledger.Use(state => state.Active(managed.Service));
        try
        {
            string? serving = await revisions.ServingAsync(managed.Service, cancellationToken);
            return new ServiceView(managed.Service, serving, managed.Baseline, managed.Revisions, active, null);
        }
        catch (HttpRequestException exception)
        {
            return new ServiceView(managed.Service, null, managed.Baseline, managed.Revisions, active, exception.Message);
        }
    }
}
