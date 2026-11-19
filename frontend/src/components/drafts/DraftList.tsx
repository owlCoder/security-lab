import { draftStatusLabel, labTypeLabel } from './DraftTypes'
import type { Draft } from './DraftTypes'

type Props = {
  drafts: Draft[]
  onOpen: (draftId: string) => void
}

export default function DraftList({ drafts, onOpen }: Props) {
  if (drafts.length === 0) {
    return (
      <div className="drafts-empty">
        <p>Trenutno nemate nijedan nacrt zadatka.</p>
      </div>
    )
  }

  return (
    <div className="drafts-list">
      <table className="table">
        <thead>
          <tr>
            <th>Naziv</th>
            <th>Tip laboratorije</th>
            <th>Status</th>
            <th>
              <span className="visually-hidden">Akcije</span>
            </th>
          </tr>
        </thead>

        <tbody>
          {drafts.map(draft => (
            <tr key={draft.id}>
              <td>{draft.title}</td>
              <td>{labTypeLabel(draft.type)}</td>
              <td>{draftStatusLabel(draft.status)}</td>
              <td>
                <button
                  type="button"
                  aria-label={`Otvori nacrt ${draft.title}`}
                  onClick={() => onOpen(draft.id)}
                >
                  Otvori
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
