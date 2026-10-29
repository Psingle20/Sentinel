using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.AgentWorker.Application.Tools;
using Sentinel.AgentWorker.Domain.Executions;
using Sentinel.Contracts;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;

namespace Sentinel.AgentWorker.Application.Investigations;

/// <summary>
/// The investigator contract (Phase 01A): every investigator, model-driven or not, answers the same
/// command with the same findings contract, so they can be swapped and compared.
/// </summary>
public interface IInvestigator
{
    string Name { get; }

    Task<InvestigationRun> InvestigateAsync(InvestigateIncident command, bool shadow, CancellationToken cancellationToken);
}
