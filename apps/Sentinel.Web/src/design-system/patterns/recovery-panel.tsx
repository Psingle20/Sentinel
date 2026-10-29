import { CircleCheck, CircleHelp, CircleX } from "lucide-react"

import { RecoveryState, type RecoveryStatus } from "../domain/recovery-state"
import { StatusBadge } from "../domain/status-badge"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "../primitives/table"

// Execution and measured recovery (Phase 08 recovery panel). Recovery is judged from the expected
// signals over the observation window, never from the action's own success response.
type RecoveryMeasurementRow = { criterion: string; observed?: string; met?: boolean }

function RecoveryPanel({
  execution,
  recovery,
}: {
  execution?: { label: string; detail?: string }
  recovery?: { status: RecoveryStatus; observedThrough: string; reason?: string; measurements: RecoveryMeasurementRow[] }
}) {
  return (
    <div className="flex flex-col gap-4">
      {execution ? (
        <div className="flex flex-col gap-1 text-sm" role="group" aria-label="Execution">
          <span className="font-medium text-text-primary">{execution.label}</span>
          {execution.detail ? <span className="text-text-muted">{execution.detail}</span> : null}
        </div>
      ) : null}
      {recovery ? (
        <div className="flex flex-col gap-3" role="group" aria-label="Recovery">
          <div className="flex flex-wrap items-center gap-2">
            <RecoveryState status={recovery.status} />
            <span className="text-xs text-text-muted">Observed through {recovery.observedThrough}</span>
          </div>
          {recovery.reason ? <p className="text-sm text-text-primary">{recovery.reason}</p> : null}
          <Table aria-label="Recovery measurements">
            <TableHeader>
              <TableRow>
                <TableHead>Signal</TableHead>
                <TableHead>Observed</TableHead>
                <TableHead>Result</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {recovery.measurements.map((measurement) => (
                <TableRow key={measurement.criterion}>
                  <TableCell>{measurement.criterion}</TableCell>
                  <TableCell>{measurement.observed ?? "No data"}</TableCell>
                  <TableCell>
                    {measurement.met === undefined ? (
                      <StatusBadge tone="neutral" icon={CircleHelp}>
                        Unknown
                      </StatusBadge>
                    ) : measurement.met ? (
                      <StatusBadge tone="stable" icon={CircleCheck}>
                        Met
                      </StatusBadge>
                    ) : (
                      <StatusBadge tone="failed" icon={CircleX}>
                        Not met
                      </StatusBadge>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      ) : (
        <p className="text-sm text-text-muted">Recovery is measured after the action is verified.</p>
      )}
    </div>
  )
}

export { RecoveryPanel, type RecoveryMeasurementRow }
