using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Infrastructure.Messaging.Consumers;
using Sentinel.ServiceDefaults;

namespace Sentinel.Runtime.Infrastructure.Messaging;

public static class RuntimeMessages
{
    /// <summary>
    /// What Runtime exchanges: the lifecycle events it publishes and its requests to AgentWorker and
    /// Executor, and the observations, findings, approval decisions and execution results it consumes.
    /// </summary>
    public static MessagingBuilder RegisterRuntimeMessages(this MessagingBuilder messaging) =>
        messaging
            .Produces<IncidentOpened>()
            .Produces<IncidentStatusChanged>()
            .Produces<IncidentBlackboardUpdated>()
            .Produces<DecisionRecorded>()
            .Produces<RemediationPlanProposed>()
            .Produces<RecoveryEvaluated>()
            .Produces<InvestigateIncident>()
            .Produces<ExecuteRemediation>()
            .Consumes<ObservationRecorded, ObservationRecordedHandler>()
            .Consumes<InvestigationCompleted, InvestigationCompletedHandler>()
            .Consumes<RecordApproval, RecordApprovalConsumer>()
            .Consumes<RecordRejection, RecordRejectionConsumer>()
            .Consumes<RecordRevocation, RecordRevocationConsumer>()
            .Consumes<RemediationExecuted, RemediationExecutedHandler>();
}