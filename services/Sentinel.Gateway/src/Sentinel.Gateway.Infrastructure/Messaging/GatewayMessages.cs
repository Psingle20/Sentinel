using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Remediation;
using Sentinel.ServiceDefaults;

namespace Sentinel.Gateway.Infrastructure.Messaging;

public static class GatewayMessages
{
    /// <summary>
    /// What the Gateway exchanges: the approval decisions it relays to Runtime, and Runtime's
    /// lifecycle events, which feed the live-update channel and notifications (Phase 08).
    /// </summary>
    public static MessagingBuilder RegisterGatewayMessages(this MessagingBuilder messaging) =>
        messaging
            .Produces<RecordApproval>()
            .Produces<RecordRejection>()
            .Produces<RecordRevocation>()
            .Consumes<IncidentOpened, IncidentOpenedHandler>()
            .Consumes<IncidentStatusChanged, IncidentStatusChangedHandler>()
            .Consumes<IncidentBlackboardUpdated, IncidentBlackboardUpdatedHandler>()
            .Consumes<DecisionRecorded, DecisionRecordedHandler>()
            .Consumes<RemediationPlanProposed, RemediationPlanProposedHandler>()
            .Consumes<RecoveryEvaluated, RecoveryEvaluatedHandler>();
}
