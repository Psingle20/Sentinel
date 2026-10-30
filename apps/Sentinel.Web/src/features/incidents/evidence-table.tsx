"use client";

import { useEffect, useState } from "react";

import { EvidenceDirectionBadge } from "@/design-system/domain/evidence-direction-badge";
import { Button } from "@/design-system/primitives/button";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/design-system/primitives/table";
import type { EvidenceView } from "@/lib/api/contracts";

import { percent } from "./presentation";

const pageSize = 50;

// The evidence, a page at a time (Phase 08): a long investigation can return hundreds of items. A link to one further
// down, such as a postmortem fact's source, reveals the rows up to it and scrolls there.
export function EvidenceTable({ evidence }: { evidence: EvidenceView[] }) {
  const [shown, setShown] = useState(pageSize);

  useEffect(() => {
    const reveal = () => {
      const target = window.location.hash.startsWith("#evidence-") ? window.location.hash.slice("#evidence-".length) : null;
      const index = target === null ? -1 : evidence.findIndex((item) => item.finding.evidenceId === target);
      if (index >= 0) {
        setShown((current) => Math.max(current, Math.ceil((index + 1) / pageSize) * pageSize));
        requestAnimationFrame(() => document.getElementById(`evidence-${target}`)?.scrollIntoView({ block: "center" }));
      }
    };

    reveal();
    window.addEventListener("hashchange", reveal);
    return () => window.removeEventListener("hashchange", reveal);
  }, [evidence]);

  const visible = evidence.slice(0, shown);
  const remaining = evidence.length - visible.length;
  return (
    <div className="flex flex-col gap-3">
      <Table aria-label="Evidence">
        <TableHeader>
          <TableRow>
            <TableHead>Direction</TableHead>
            <TableHead>Finding</TableHead>
            <TableHead>Strength</TableHead>
            <TableHead>Sources</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {visible.map((item) => (
            <TableRow key={item.finding.evidenceId} id={`evidence-${item.finding.evidenceId}`}>
              <TableCell>
                <EvidenceDirectionBadge direction={item.finding.direction} />
              </TableCell>
              <TableCell className="whitespace-normal">
                <span className="text-text-primary">{item.finding.statement}</span>
                {item.status === "rejected" ? <span className="block text-caption text-incident-critical">Rejected: {item.rejectionReason}</span> : null}
                {item.finding.partial ? <span className="block text-caption text-incident-warning">Based on partial data</span> : null}
              </TableCell>
              <TableCell>{percent(item.finding.strength)}</TableCell>
              <TableCell className="text-text-muted">
                <details>
                  <summary className="cursor-pointer">{item.finding.sourceObservationIds.length} observation(s)</summary>
                  <ul className="mt-1 flex flex-col gap-0.5 font-mono text-caption break-all">
                    {item.finding.sourceObservationIds.map((id) => (
                      <li key={id}>{id}</li>
                    ))}
                  </ul>
                </details>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
      {remaining > 0 ? (
        <Button variant="outline" size="sm" className="w-fit" onClick={() => setShown((current) => current + pageSize)}>
          {`Show ${Math.min(pageSize, remaining)} more of ${remaining} remaining`}
        </Button>
      ) : null}
    </div>
  );
}
