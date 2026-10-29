using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Sentinel.Contracts;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;

namespace Sentinel.AgentWorker.Application.Tools;

/// <summary>
/// What a tool call may touch. Tenant, environment and the time window come from the investigation
/// command, never from model arguments, so a model cannot widen its own scope.
/// </summary>
public sealed record ToolContext(
    TenantId TenantId,
    EnvironmentId EnvironmentId,
    IncidentId IncidentId,
    ServiceId AffectedServiceId,
    IReadOnlyList<ServiceId> RelatedServiceIds,
    TimeRange Window);
