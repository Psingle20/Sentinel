using Temporalio.Common;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace Sentinel.Runtime.Infrastructure.Workflows;

public sealed record IncidentWorkflowInput(Guid IncidentId, int Cycle, WorkflowTimings Timings);
