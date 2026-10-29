using System.Text.Json.Serialization;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.Incidents.Commands;

public sealed record RecordRecoveryCommand(RecoveryResult Result, IReadOnlyList<Measurement> Measurements, DateTimeOffset ObservedThrough, string? Reason) : IncidentCommand;
