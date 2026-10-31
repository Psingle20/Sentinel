using System.Text.Json.Serialization;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.Incidents.Commands;

/// <summary>Records the recovery verdict; with <see cref="FollowUp"/>, an unrecovered verdict may open a follow-up cycle (Phase E03).</summary>
public sealed record RecordRecoveryCommand(RecoveryResult Result, IReadOnlyList<Measurement> Measurements, DateTimeOffset ObservedThrough, string? Reason, bool FollowUp = false) : IncidentCommand;
