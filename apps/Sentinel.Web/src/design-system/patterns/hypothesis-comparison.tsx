import { CircleCheck, CircleDot, CircleSlash, Scale, ShieldCheck, ShieldQuestion, ShieldX } from "lucide-react"

import { StatusBadge, type StatusPresentation } from "../domain/status-badge"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "../primitives/table"

// Competing explanations side by side (Phase 08 hypothesis comparison): how each scored, how much
// independent evidence supports and contradicts it, how it fared under challenge, and why it won,
// was kept as dissent or was eliminated.
type HypothesisStanding = "leading" | "dissent" | "standing" | "eliminated"

type ChallengeVerdict = "survived" | "falsified" | "inconclusive"

type ComparedHypothesis = {
  id: string
  statement: string
  hypothesisKey?: string
  service?: string
  standing: HypothesisStanding
  score?: number
  support?: number
  contradiction?: number
  reason?: string
  challenges: { falsifier: string; verdict: ChallengeVerdict; detail: string }[]
}

const standings: Record<HypothesisStanding, StatusPresentation> = {
  leading: { label: "Leading", tone: "leading", icon: CircleCheck },
  dissent: { label: "Dissent kept", tone: "warning", icon: Scale },
  standing: { label: "Not leading", tone: "neutral", icon: CircleDot },
  eliminated: { label: "Eliminated", tone: "contradicting", icon: CircleSlash },
}

const verdicts: Record<ChallengeVerdict, StatusPresentation> = {
  survived: { label: "Survived", tone: "stable", icon: ShieldCheck },
  falsified: { label: "Falsified", tone: "critical", icon: ShieldX },
  inconclusive: { label: "Inconclusive", tone: "neutral", icon: ShieldQuestion },
}

function HypothesisComparison({ hypotheses }: { hypotheses: ComparedHypothesis[] }) {
  return (
    <Table aria-label="Hypothesis comparison">
      <TableHeader>
        <TableRow>
          <TableHead>Hypothesis</TableHead>
          <TableHead>Standing</TableHead>
          <TableHead>Score</TableHead>
          <TableHead>Independent evidence</TableHead>
          <TableHead>Challenges</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {hypotheses.map((hypothesis) => {
          const standing = standings[hypothesis.standing]
          return (
            <TableRow key={hypothesis.id}>
              <TableCell className="whitespace-normal">
                <span className="text-text-primary">{hypothesis.statement}</span>
                <span className="block text-xs text-text-muted">
                  {[hypothesis.service, hypothesis.hypothesisKey].filter(Boolean).join(" · ")}
                </span>
                {hypothesis.reason ? <span className="block text-xs text-text-muted">{hypothesis.reason}</span> : null}
              </TableCell>
              <TableCell>
                <StatusBadge tone={standing.tone} icon={standing.icon}>
                  {standing.label}
                </StatusBadge>
              </TableCell>
              <TableCell>{hypothesis.score === undefined ? "–" : hypothesis.score.toFixed(2)}</TableCell>
              <TableCell>
                {hypothesis.support === undefined ? (
                  "–"
                ) : (
                  <span>
                    {hypothesis.support} supporting, {hypothesis.contradiction ?? 0} contradicting
                  </span>
                )}
              </TableCell>
              <TableCell className="whitespace-normal">
                {hypothesis.challenges.length === 0 ? (
                  <span className="text-text-muted">Not challenged</span>
                ) : (
                  <ul className="flex flex-col gap-1.5">
                    {hypothesis.challenges.map((challenge) => {
                      const verdict = verdicts[challenge.verdict]
                      return (
                        <li key={challenge.falsifier} className="flex flex-col gap-0.5">
                          <StatusBadge tone={verdict.tone} icon={verdict.icon}>
                            {verdict.label}: {challenge.falsifier}
                          </StatusBadge>
                          <span className="text-xs text-text-muted">{challenge.detail}</span>
                        </li>
                      )
                    })}
                  </ul>
                )}
              </TableCell>
            </TableRow>
          )
        })}
      </TableBody>
    </Table>
  )
}

export { HypothesisComparison, type ChallengeVerdict, type ComparedHypothesis, type HypothesisStanding }
