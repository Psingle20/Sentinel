using System.Globalization;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Ingestion.Domain.Observations;

/// <summary>
/// Deterministic observation identity: the same source data always gets the same ID, so
/// re-ingesting it is a duplicate, never a second observation (Phase 02 reliability semantics).
/// IDs are unique within a tenant and environment, which the store keys on as well.
/// </summary>
public static class ObservationIdentity
{
    /// <summary>One metric sample: provider, series name, service and sample time in Unix seconds.</summary>
    public static ObservationId Metric(string provider, string series, ServiceId service, DateTimeOffset eventTime) =>
        ObservationId.From(string.Create(CultureInfo.InvariantCulture, $"{provider}:{series}:{service}:{eventTime.ToUnixTimeSeconds()}"));

    /// <summary>
    /// One state of a deployment. A deployment yields an observation when it starts and another
    /// when it completes or fails, so its state is part of the identity.
    /// </summary>
    public static ObservationId Deployment(string provider, string deploymentId, string state) =>
        ObservationId.From($"{provider}:deployment:{deploymentId}:{state}");
}
