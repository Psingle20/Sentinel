using System.Text.Json.Serialization;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.Incidents.Commands;

public sealed record TimeOutInvestigationsCommand(IReadOnlyList<InvestigationId> InvestigationIds) : IncidentCommand;
