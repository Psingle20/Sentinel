import { cva } from "class-variance-authority"

// The incident's causal graph (Phase 08 evidence graph): changes, services, hypotheses, evidence
// and the symptom, with supporting and contradicting relations. The drawing is a visualization
// canvas, so it positions nodes in SVG units; every relation is also listed as text below it.
type GraphNodeKind = "change" | "service" | "hypothesis" | "evidence" | "symptom"

type GraphNode = { id: string; kind: GraphNodeKind; label: string }

type GraphEdge = { from: string; to: string; relation: string }

const columns: GraphNodeKind[] = ["change", "service", "hypothesis", "evidence", "symptom"]

const columnTitles: Record<GraphNodeKind, string> = {
  change: "Changes",
  service: "Services",
  hypothesis: "Hypotheses",
  evidence: "Evidence",
  symptom: "Symptom",
}

const nodeVariants = cva("stroke-2", {
  variants: {
    kind: {
      change: "fill-action-executing-subtle stroke-action-executing",
      service: "fill-surface-panel stroke-border-subtle",
      hypothesis: "fill-surface-panel stroke-evidence-neutral",
      evidence: "fill-surface-panel stroke-border-subtle",
      symptom: "fill-incident-critical-subtle stroke-incident-critical",
    },
    leading: { true: "fill-hypothesis-leading-subtle stroke-hypothesis-leading" },
  },
})

const edgeVariants = cva("fill-none stroke-2", {
  variants: {
    relation: {
      supports: "stroke-evidence-supporting",
      contradicts: "stroke-evidence-contradicting",
      other: "stroke-evidence-neutral",
    },
  },
})

const NodeWidth = 180
const NodeHeight = 36
const ColumnGap = 60
const RowGap = 16
const HeaderHeight = 28
const MaxPerColumn = 10

function truncate(text: string, length = 26) {
  return text.length <= length ? text : `${text.slice(0, length - 1)}…`
}

function relationTone(relation: string): "supports" | "contradicts" | "other" {
  return relation === "supports" || relation === "contradicts" ? relation : "other"
}

function EvidenceGraph({
  nodes,
  edges,
  leadingIds = [],
  title = "Causal graph",
}: {
  nodes: GraphNode[]
  edges: GraphEdge[]
  leadingIds?: string[]
  title?: string
}) {
  const placed = new Map<string, { x: number; y: number; node: GraphNode }>()
  const hidden: Partial<Record<GraphNodeKind, number>> = {}
  columns.forEach((kind, column) => {
    const inColumn = nodes.filter((node) => node.kind === kind)
    inColumn.slice(0, MaxPerColumn).forEach((node, row) => {
      placed.set(node.id, { x: column * (NodeWidth + ColumnGap), y: HeaderHeight + row * (NodeHeight + RowGap), node })
    })
    if (inColumn.length > MaxPerColumn) {
      hidden[kind] = inColumn.length - MaxPerColumn
    }
  })

  const rows = Math.max(1, ...columns.map((kind) => Math.min(MaxPerColumn, nodes.filter((node) => node.kind === kind).length)))
  const width = columns.length * NodeWidth + (columns.length - 1) * ColumnGap
  const height = HeaderHeight + rows * (NodeHeight + RowGap) + RowGap
  const drawn = edges.filter((edge) => placed.has(edge.from) && placed.has(edge.to))
  const labelOf = (id: string) => placed.get(id)?.node.label ?? id

  return (
    <figure className="flex flex-col gap-3">
      <div className="overflow-x-auto">
        <svg viewBox={`0 0 ${width} ${height}`} role="img" aria-labelledby="evidence-graph-title" className="h-auto w-full min-w-3xl">
          <title id="evidence-graph-title">{title}</title>
          {columns.map((kind, column) => (
            <text key={kind} x={column * (NodeWidth + ColumnGap)} y={16} className="fill-text-muted text-xs font-medium">
              {columnTitles[kind]}
              {hidden[kind] ? ` (+${hidden[kind]} more)` : ""}
            </text>
          ))}
          {drawn.map((edge) => {
            const from = placed.get(edge.from)!
            const to = placed.get(edge.to)!
            const forward = from.x <= to.x
            const startX = forward ? from.x + NodeWidth : from.x
            const endX = forward ? to.x : to.x + NodeWidth
            const startY = from.y + NodeHeight / 2
            const endY = to.y + NodeHeight / 2
            const bend = (endX - startX) / 2
            const tone = relationTone(edge.relation)
            return (
              <path
                key={`${edge.from}-${edge.relation}-${edge.to}`}
                d={`M ${startX} ${startY} C ${startX + bend} ${startY}, ${endX - bend} ${endY}, ${endX} ${endY}`}
                className={edgeVariants({ relation: tone })}
                strokeDasharray={tone === "contradicts" ? "6 4" : undefined}
              >
                <title>{`${labelOf(edge.from)} ${edge.relation} ${labelOf(edge.to)}`}</title>
              </path>
            )
          })}
          {[...placed.values()].map(({ x, y, node }) => (
            <g key={node.id}>
              <rect x={x} y={y} width={NodeWidth} height={NodeHeight} rx={6} className={nodeVariants({ kind: node.kind, leading: leadingIds.includes(node.id) })} />
              <text x={x + 10} y={y + NodeHeight / 2 + 4} className="fill-text-primary text-xs">
                {truncate(node.label)}
              </text>
              <title>{node.label}</title>
            </g>
          ))}
        </svg>
      </div>
      <figcaption className="text-xs text-text-muted">
        <details>
          <summary className="cursor-pointer">The graph as a list ({drawn.length} relations)</summary>
          <ul className="mt-2 flex flex-col gap-1" aria-label="Graph relations">
            {drawn.map((edge) => (
              <li key={`${edge.from}-${edge.relation}-${edge.to}`}>
                {labelOf(edge.from)} <span className="font-medium text-text-primary">{edge.relation}</span> {labelOf(edge.to)}
              </li>
            ))}
          </ul>
        </details>
      </figcaption>
    </figure>
  )
}

export { EvidenceGraph, type GraphEdge, type GraphNode, type GraphNodeKind }
