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

/// <summary>One investigator run: the message for Runtime and the audit record for AgentWorker.</summary>
public sealed record InvestigationRun(InvestigationCompleted Completed, AgentExecutionRecord Execution)
{
    /// <summary>Replaces the findings and keeps the audit record's copy identical to what is sent (Phase 07 replay compares against it).</summary>
    public InvestigationRun WithCompleted(InvestigationCompleted completed)
    {
        ArgumentNullException.ThrowIfNull(completed);

        return new InvestigationRun(completed, Execution with { Findings = JsonSerializer.Serialize(completed, SentinelJson.Options) });
    }
}
