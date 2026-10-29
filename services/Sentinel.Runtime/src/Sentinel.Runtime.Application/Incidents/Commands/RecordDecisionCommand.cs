using System.Text.Json.Serialization;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.Incidents.Commands;

/// <summary>Records a decision; when <see cref="Supersede"/> is set, a later decision replaces the current one.</summary>
public sealed record RecordDecisionCommand(Decision Decision, string? Supersede = null) : IncidentCommand;
