using System.Text.Json.Serialization;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.Incidents.Commands;

public sealed record LinkSignalCommand(ServiceId ServiceId, string MetricName, double Value, IReadOnlyList<ObservationId> ObservationIds, double Score, IReadOnlyList<string> Reasons) : IncidentCommand;
