import { Bot, CircleCheck, CircleSlash, CircleX, FlaskConical } from "lucide-react"

import { StatusBadge, type StatusPresentation } from "../domain/status-badge"

// What each investigator did (Phase 08 agent activity): model and prompt version, tools, tokens,
// duration and outcome. Bounded metadata only; prompts, credentials and raw model output never appear.
type AgentRun = {
  id: string
  investigator: string
  role?: string
  shadow: boolean
  model: string
  promptVersion: string
  result: "completed" | "abstained" | "failed"
  toolCalls: number
  failedToolCalls: number
  tokens: number
  durationSeconds: number
  note?: string
}

const results: Record<AgentRun["result"], StatusPresentation> = {
  completed: { label: "Completed", tone: "stable", icon: CircleCheck },
  abstained: { label: "Abstained", tone: "neutral", icon: CircleSlash },
  failed: { label: "Failed", tone: "critical", icon: CircleX },
}

// A list rather than a table: it sits in a narrow column, where six table columns would scroll.
function AgentActivity({ runs }: { runs: AgentRun[] }) {
  return (
    <ul className="flex flex-col gap-4" aria-label="Agent activity">
      {runs.map((run) => {
        const result = results[run.result]
        return (
          <li key={run.id} className="flex flex-col gap-1.5 border-l border-border-subtle pl-3 text-sm">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <span className="flex items-center gap-1.5 font-medium text-text-primary">
                {run.shadow ? <FlaskConical aria-hidden="true" className="size-4 text-text-muted" /> : <Bot aria-hidden="true" className="size-4 text-text-muted" />}
                {run.role ?? run.investigator}
              </span>
              <StatusBadge tone={result.tone} icon={result.icon}>
                {result.label}
              </StatusBadge>
            </div>
            <span className="text-xs text-text-muted">{run.shadow ? "Shadow baseline, never sent to Runtime" : run.investigator}</span>
            <span className="font-mono text-xs break-all text-text-muted">
              {run.model} · {run.promptVersion}
            </span>
            <dl className="flex flex-wrap gap-x-4 gap-y-1 text-xs">
              <div className="flex gap-1">
                <dt className="text-text-muted">Tools</dt>
                <dd className="text-text-primary">
                  {run.toolCalls}
                  {run.failedToolCalls > 0 ? <span className="text-incident-warning">, {run.failedToolCalls} failed</span> : null}
                </dd>
              </div>
              <div className="flex gap-1">
                <dt className="text-text-muted">Tokens</dt>
                <dd className="text-text-primary">{run.tokens.toLocaleString("en-GB")}</dd>
              </div>
              <div className="flex gap-1">
                <dt className="text-text-muted">Duration</dt>
                <dd className="text-text-primary">{run.durationSeconds.toFixed(1)} s</dd>
              </div>
            </dl>
            {run.note ? <p className="text-xs text-text-muted">{run.note}</p> : null}
          </li>
        )
      })}
    </ul>
  )
}

export { AgentActivity, type AgentRun }
