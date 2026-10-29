using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.BusinessTasks.Commands.Remediation;

public sealed record RecordInvestigationReportCommand(IncidentId IncidentId, InvestigationReport Report) : ICommand;
