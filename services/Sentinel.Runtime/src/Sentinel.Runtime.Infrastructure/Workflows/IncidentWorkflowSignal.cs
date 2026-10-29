using Temporalio.Common;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace Sentinel.Runtime.Infrastructure.Workflows;

/// <summary>A hint that the incident's stream changed; the workflow re-reads the incident before acting on it.</summary>
public sealed record IncidentWorkflowSignal(string Kind, string? Reference);
