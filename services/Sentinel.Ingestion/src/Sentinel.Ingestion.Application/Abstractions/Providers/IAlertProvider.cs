using Sentinel.Contracts.Observations;

namespace Sentinel.Ingestion.Application.Abstractions.Providers;

/// <summary>
/// A monitoring system's alerts (Phase 00 provider interfaces, Phase 02 signal types): the alerts pending or firing when
/// asked, as canonical observations. Alerts that have resolved are no longer returned; their observations remain.
/// </summary>
public interface IAlertProvider
{
    string Name { get; }

    Task<ProviderResult<AlertObservation>> GetActiveAlertsAsync(AlertRequest request, CancellationToken cancellationToken);
}
