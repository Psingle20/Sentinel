using Sentinel.Runtime.Application.Incidents;

namespace Sentinel.Runtime.Application.Abstractions.Runtime;

/// <summary>The durable incident workflow (ADR-005), one run per incident cycle.</summary>
public interface IIncidentWorkflows
{
    /// <summary>Starts the cycle's workflow unless it is already running or finished; safe to repeat.</summary>
    Task EnsureStartedAsync(IncidentSnapshot incident, CancellationToken cancellationToken);

    Task SignalAsync(IncidentSnapshot incident, IncidentSignal signal, CancellationToken cancellationToken);
}