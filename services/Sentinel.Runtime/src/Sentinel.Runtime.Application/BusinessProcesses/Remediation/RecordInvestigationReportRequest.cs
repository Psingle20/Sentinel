using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.BusinessProcesses.Remediation;

/// <summary>AgentWorker reported an investigation's findings.</summary>
public sealed record RecordInvestigationReportRequest(IncidentId IncidentId, InvestigationReport Report) : IRequest;
