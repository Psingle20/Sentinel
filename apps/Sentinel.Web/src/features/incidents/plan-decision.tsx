"use client";

import { useActionState, useId } from "react";

import { Button } from "@/design-system/primitives/button";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/design-system/primitives/dialog";
import { Textarea } from "@/design-system/primitives/textarea";
import { track } from "@/features/analytics/track";

import { approve, type DecisionState, reject, revoke } from "./actions";

const idle: DecisionState = { status: "idle" };

type PlanIdentity = {
  incidentId: string;
  planId: string;
  planVersion: number;
  planHash: string;
  summary: string;
};

// Approve or reject one immutable plan version. The hidden fields are exactly what the approver
// reviewed; Api refuses them if the incident's current plan has changed since (ADR-006).
export function PlanDecision({ plan }: { plan: PlanIdentity }) {
  const [approval, approveAction, approving] = useActionState(approve, idle);
  const [rejection, rejectAction, rejecting] = useActionState(reject, idle);
  const reasonId = useId();
  const result = approval.status !== "idle" ? approval : rejection;

  const fields = (
    <>
      <input type="hidden" name="incidentId" value={plan.incidentId} />
      <input type="hidden" name="planId" value={plan.planId} />
      <input type="hidden" name="planVersion" value={plan.planVersion} />
      <input type="hidden" name="planHash" value={plan.planHash} />
    </>
  );

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap gap-2">
        <Dialog onOpenChange={(open) => open && track("approval.dialog-opened", { action: "approve" })}>
          <DialogTrigger asChild>
            <Button disabled={approving || rejecting || approval.status === "done"}>Approve plan</Button>
          </DialogTrigger>
          <DialogContent>
            <form action={approveAction} className="flex flex-col gap-4">
              {fields}
              <DialogHeader>
                <DialogTitle>Approve plan v{plan.planVersion}?</DialogTitle>
                <DialogDescription>
                  {plan.summary}. Your approval is signed for this exact plan content and can be used once.
                </DialogDescription>
              </DialogHeader>
              <p className="font-mono text-xs break-all text-text-muted">{plan.planHash}</p>
              <DialogFooter>
                <DialogClose asChild>
                  <Button type="button" variant="outline">
                    Cancel
                  </Button>
                </DialogClose>
                <DialogClose asChild>
                  <Button type="submit">Confirm approval</Button>
                </DialogClose>
              </DialogFooter>
            </form>
          </DialogContent>
        </Dialog>

        <Dialog onOpenChange={(open) => open && track("approval.dialog-opened", { action: "reject" })}>
          <DialogTrigger asChild>
            <Button variant="outline" disabled={approving || rejecting || approval.status === "done"}>
              Reject plan
            </Button>
          </DialogTrigger>
          <DialogContent>
            <form action={rejectAction} className="flex flex-col gap-4">
              {fields}
              <DialogHeader>
                <DialogTitle>Reject plan v{plan.planVersion}?</DialogTitle>
                <DialogDescription>The incident is escalated to responders with your reason.</DialogDescription>
              </DialogHeader>
              <label htmlFor={reasonId} className="text-sm font-medium text-text-primary">
                Reason
              </label>
              <Textarea id={reasonId} name="reason" required />
              <DialogFooter>
                <DialogClose asChild>
                  <Button type="button" variant="outline">
                    Cancel
                  </Button>
                </DialogClose>
                <Button type="submit" variant="destructive">
                  Confirm rejection
                </Button>
              </DialogFooter>
            </form>
          </DialogContent>
        </Dialog>
      </div>

      {result.status !== "idle" ? (
        <p role={result.status === "error" ? "alert" : "status"} className="text-sm text-text-primary">
          {result.message}
        </p>
      ) : null}
    </div>
  );
}

// Withdraws an approval while execution has not started (Phase 06). After Runtime confirms the
// start, the point of no return, Api refuses and the action runs to its verified outcome.
export function RevokeApproval({ plan }: { plan: PlanIdentity }) {
  const [result, revokeAction, revoking] = useActionState(revoke, idle);
  const reasonId = useId();

  return (
    <div className="flex flex-col gap-3">
      <Dialog onOpenChange={(open) => open && track("approval.dialog-opened", { action: "revoke" })}>
        <DialogTrigger asChild>
          <Button variant="outline" className="w-fit" disabled={revoking || result.status === "done"}>
            Revoke approval
          </Button>
        </DialogTrigger>
        <DialogContent>
          <form action={revokeAction} className="flex flex-col gap-4">
            <input type="hidden" name="incidentId" value={plan.incidentId} />
            <input type="hidden" name="planId" value={plan.planId} />
            <input type="hidden" name="planVersion" value={plan.planVersion} />
            <DialogHeader>
              <DialogTitle>Revoke the approval of plan v{plan.planVersion}?</DialogTitle>
              <DialogDescription>Nothing runs, and the incident is escalated to responders with your reason.</DialogDescription>
            </DialogHeader>
            <label htmlFor={reasonId} className="text-sm font-medium text-text-primary">
              Reason
            </label>
            <Textarea id={reasonId} name="reason" required />
            <DialogFooter>
              <DialogClose asChild>
                <Button type="button" variant="outline">
                  Cancel
                </Button>
              </DialogClose>
              <Button type="submit" variant="destructive">
                Confirm revocation
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
      {result.status !== "idle" ? (
        <p role={result.status === "error" ? "alert" : "status"} className="text-sm text-text-primary">
          {result.message}
        </p>
      ) : null}
    </div>
  );
}