import { DECISION_LABELS, STATUS_LABELS, formatDateTime, shortHash, type Decision, type RevisionStatus } from '../api/revisions'

export function StatusBadge({ status }: { status: RevisionStatus }) {
  return <span className={`badge badge-${status.toLowerCase()}`}>{STATUS_LABELS[status] ?? status}</span>
}

/** Istorija odluka. Komentari se prikazuju iskljucivo kao tekst (React ih escapuje). */
export function DecisionHistory({ decisions }: { decisions: Decision[] }) {
  if (decisions.length === 0) return <p className="muted">Još nema odluka za ovu reviziju.</p>

  return (
    <ol className="timeline" aria-label="Istorija odluka">
      {decisions.map(d => (
        <li key={d.id} className={`timeline-item timeline-${d.type.toLowerCase()}`}>
          <div className="timeline-head">
            <strong>{DECISION_LABELS[d.type] ?? d.type}</strong>
            {d.revisionNumber != null && <span className="pill">revizija br. {d.revisionNumber}</span>}
          </div>
          <div className="muted small">
            {d.actor.displayName} · <time dateTime={d.at}>{formatDateTime(d.at)}</time> · <code title={d.contentHash}>{shortHash(d.contentHash)}</code>
          </div>
          {d.comment && <p className="comment">{d.comment}</p>}
        </li>
      ))}
    </ol>
  )
}
