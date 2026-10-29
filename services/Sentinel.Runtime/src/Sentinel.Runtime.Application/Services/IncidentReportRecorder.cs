using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.Logging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Application.Incidents;

namespace Sentinel.Runtime.Application.Services;

/// <summary>
/// Runtime's inbound side of the control path (ADR-006): what another service reports is recorded
/// through the incident's single writer, then the incident's workflow is told so it can take the
/// next step. A report the incident cannot take (unknown investigation, wrong state, a plan that is
/// no longer current) is logged and dropped, never retried.
/// </summary>
public sealed class IncidentReportRecorder(IIncidentOwner owner, IIncidentWorkflows workflows, ILogger<IncidentReportRecorder> logger)
{
    public async Task RecordAsync(IncidentId incidentId, IncidentCommand command, IncidentSignal signal, string what, CancellationToken cancellationToken)
    {
        Result<CommandReply> result = await owner.SendAsync(incidentId, command, cancellationToken);
        if (result.IsFailure)
        {
            logger.LogWarning("Incident {IncidentId} refused the {What}: {Reason}", incidentId, what, result.Error.Description);
            return;
        }

        // Signalled even when nothing changed: a redelivered message re-sends a signal that may have been lost.
        await workflows.SignalAsync(result.Value.Snapshot, signal, cancellationToken);
    }
}
