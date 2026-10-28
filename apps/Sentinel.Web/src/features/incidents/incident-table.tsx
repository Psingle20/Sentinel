import Link from "next/link";

import { IncidentStatusBadge } from "@/design-system/domain/incident-status-badge";
import { SeverityIndicator } from "@/design-system/domain/severity-indicator";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/design-system/primitives/table";
import type { IncidentSummary } from "@/lib/api/contracts";

import { formatDateTime, incidentStatus } from "./presentation";

export function IncidentTable({ incidents }: { incidents: IncidentSummary[] }) {
  return (
    <Table aria-label="Incidents">
      <TableHeader>
        <TableRow>
          <TableHead>Incident</TableHead>
          <TableHead>Service</TableHead>
          <TableHead>Severity</TableHead>
          <TableHead>Status</TableHead>
          <TableHead>Opened</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {incidents.map((incident) => (
          <TableRow key={incident.incidentId}>
            <TableCell>
              <Link
                href={`/incidents/${incident.incidentId}`}
                className="font-medium text-text-primary underline-offset-4 hover:underline focus-visible:underline"
              >
                {incident.title}
              </Link>
            </TableCell>
            <TableCell className="text-text-muted">{incident.affectedServiceId}</TableCell>
            <TableCell>
              <SeverityIndicator severity={incident.severity} />
            </TableCell>
            <TableCell>
              <IncidentStatusBadge status={incidentStatus(incident.status)} />
            </TableCell>
            <TableCell className="text-text-muted">{formatDateTime(incident.openedAt)}</TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
