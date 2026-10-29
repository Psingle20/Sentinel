import * as React from "react"

import { ApprovalState, type ApprovalStatus } from "../domain/approval-state"

// A remediation plan as an approver must review it (Phase 08 remediation review): the exact
// actions, risk, blast radius, preconditions, expected effect and recovery signals, compensation,
// the policy's verdict with its rule trace, and the content hash the approval is bound to.
type ReviewedPlan = {
  version: number
  actions: string[]
  risk: string
  blastRadius: string[]
  preconditions: string[]
  expectedEffect?: string
  expectedRecovery: string[]
  observationWindow: string
  compensation?: string
  policy: { version: string; decision: string; trace: string[] }
  planHash: string
  approval: ApprovalStatus
  approvedBy?: string
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-1">
      <dt className="text-xs font-medium text-text-muted">{label}</dt>
      <dd className="text-sm text-text-primary">{children}</dd>
    </div>
  )
}

function List({ items, empty }: { items: string[]; empty: string }) {
  return items.length === 0 ? (
    <span className="text-text-muted">{empty}</span>
  ) : (
    <ul className="flex flex-col gap-0.5">
      {items.map((item) => (
        <li key={item}>{item}</li>
      ))}
    </ul>
  )
}

function RemediationReview({ plan, children }: { plan: ReviewedPlan; children?: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-2">
        <ApprovalState status={plan.approval} />
        <span className="text-sm text-text-muted">
          Policy {plan.policy.version}: {plan.policy.decision}
          {plan.approvedBy ? ` · approved by ${plan.approvedBy}` : ""}
        </span>
      </div>
      <dl className="grid gap-4 sm:grid-cols-2">
        <Field label="Actions">
          <List items={plan.actions} empty="No action" />
        </Field>
        <Field label="Risk and blast radius">
          {plan.risk} risk; {plan.blastRadius.length === 0 ? "blast radius not stated" : plan.blastRadius.join(", ")}
        </Field>
        <Field label="Preconditions checked before execution">
          <List items={plan.preconditions} empty="None stated" />
        </Field>
        <Field label={`Expected recovery within ${plan.observationWindow}`}>
          <List items={plan.expectedRecovery} empty="No recovery signal" />
        </Field>
        {plan.expectedEffect ? <Field label="Expected effect">{plan.expectedEffect}</Field> : null}
        {plan.compensation ? <Field label="If it goes wrong">{plan.compensation}</Field> : null}
        <Field label="Policy rule trace">
          <List items={plan.policy.trace} empty="No rule matched" />
        </Field>
        <Field label={`Plan v${plan.version} content hash`}>
          <span className="font-mono text-xs break-all">{plan.planHash}</span>
        </Field>
      </dl>
      {children}
    </div>
  )
}

export { RemediationReview, type ReviewedPlan }
