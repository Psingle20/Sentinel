using System.Text.Json.Serialization;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.Incidents.Commands;

public sealed record EscalateCommand(string Reason, bool WithoutPlan = false) : IncidentCommand;
