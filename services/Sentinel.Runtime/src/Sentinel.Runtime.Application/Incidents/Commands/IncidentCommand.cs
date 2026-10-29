using System.Text.Json.Serialization;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.Incidents.Commands;

/// <summary>
/// A command to the incident's single writer (ADR-004). Commands are plain, serializable data: any
/// I/O they depend on, such as a policy evaluation or a recovery measurement, happens before the
/// command is sent, so the writer only applies invariants and appends events. Every command is
/// idempotent, so a retried workflow step or redelivered message appends nothing new.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "command")]
[JsonDerivedType(typeof(OpenIncidentCommand), "open")]
[JsonDerivedType(typeof(RequestInvestigationsCommand), "request-investigations")]
[JsonDerivedType(typeof(RecordInvestigationCommand), "record-investigation")]
[JsonDerivedType(typeof(TimeOutInvestigationsCommand), "time-out-investigations")]
[JsonDerivedType(typeof(UpdateChangesCommand), "update-changes")]
[JsonDerivedType(typeof(RecordDecisionCommand), "record-decision")]
[JsonDerivedType(typeof(DecideCommand), "decide")]
[JsonDerivedType(typeof(ProposePlanCommand), "propose-plan")]
[JsonDerivedType(typeof(EscalateCommand), "escalate")]
[JsonDerivedType(typeof(ApprovePlanCommand), "approve-plan")]
[JsonDerivedType(typeof(RejectPlanCommand), "reject-plan")]
[JsonDerivedType(typeof(RevokeApprovalCommand), "revoke-approval")]
[JsonDerivedType(typeof(RequestExecutionCommand), "request-execution")]
[JsonDerivedType(typeof(ConfirmStartCommand), "confirm-start")]
[JsonDerivedType(typeof(RecordExecutionCommand), "record-execution")]
[JsonDerivedType(typeof(ScheduleRecoveryCommand), "schedule-recovery")]
[JsonDerivedType(typeof(RecordRecoveryCommand), "record-recovery")]
[JsonDerivedType(typeof(ResolveClearedCommand), "resolve-cleared")]
[JsonDerivedType(typeof(LinkSignalCommand), "link-signal")]
[JsonDerivedType(typeof(ChangeSeverityCommand), "change-severity")]
[JsonDerivedType(typeof(ReopenCommand), "reopen")]
public abstract record IncidentCommand;
