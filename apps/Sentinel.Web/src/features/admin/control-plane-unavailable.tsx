import { DataState } from "@/design-system/patterns/data-state";
import { ApiError } from "@/lib/api/server";

// What a control-plane page shows when its data cannot be read: a refusal for anyone without the
// operator role, otherwise which services did not answer.
export function ControlPlaneUnavailable({ error, owners }: { error: unknown; owners: string }) {
  if (error instanceof ApiError && error.status === 403) {
    return <DataState kind="permission-denied" title="The control plane is for operators" description="Ask an administrator for the operator role." />;
  }

  return <DataState kind="error" title="The control plane is unavailable" description={`${owners} did not answer.`} />;
}
