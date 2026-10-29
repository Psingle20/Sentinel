// Statements with their provenance (Phase 08 postmortem and explanation views): each fact names the
// events, evidence and observations it is derived from, so a reader can check it.
type SourcedFact = { text: string; at?: string; sources: { kind: string; ref: string; href?: string }[] }

function sourceLabel(source: { kind: string; ref: string }) {
  return source.kind === "event" ? `event v${source.ref}` : `${source.kind} ${source.ref.length > 24 ? `${source.ref.slice(0, 23)}…` : source.ref}`
}

function FactList({ facts, label }: { facts: SourcedFact[]; label: string }) {
  return (
    <ol className="flex flex-col gap-3" aria-label={label}>
      {facts.map((fact, index) => (
        <li key={`${index}-${fact.text}`} className="flex flex-col gap-1 border-l border-border-subtle pl-3">
          {fact.at ? <span className="text-xs text-text-muted">{fact.at}</span> : null}
          <span className="text-sm text-text-primary">{fact.text}</span>
          <span className="flex flex-wrap gap-1.5" role="group" aria-label="Sources">
            {fact.sources.map((source) =>
              source.href ? (
                <a
                  key={`${source.kind}-${source.ref}`}
                  href={source.href}
                  className="rounded-sm border border-border-subtle px-1.5 font-mono text-xs text-text-muted hover:text-text-primary focus-visible:text-text-primary"
                >
                  {sourceLabel(source)}
                </a>
              ) : (
                <span key={`${source.kind}-${source.ref}`} className="rounded-sm border border-border-subtle px-1.5 font-mono text-xs text-text-muted" title={source.ref}>
                  {sourceLabel(source)}
                </span>
              )
            )}
          </span>
        </li>
      ))}
    </ol>
  )
}

export { FactList, type SourcedFact }
