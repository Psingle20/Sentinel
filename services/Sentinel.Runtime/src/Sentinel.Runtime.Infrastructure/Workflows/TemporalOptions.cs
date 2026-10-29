using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.Incidents;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using Temporalio.Exceptions;
using Temporalio.Extensions.OpenTelemetry;
using Temporalio.Worker;
using Sentinel.Runtime.Application.Abstractions.Runtime;

namespace Sentinel.Runtime.Infrastructure.Workflows;

/// <summary>Configured as <c>Runtime:Temporal</c>; the address comes from the orchestrator.</summary>
public sealed class TemporalOptions
{
    public const string Section = "Runtime:Temporal";

    /// <summary><c>host:port</c> of the Temporal frontend.</summary>
    public string Address { get; set; } = string.Empty;

    public string Namespace { get; set; } = "default";

    public string TaskQueue { get; set; } = IncidentWorkflow.TaskQueue;
}
