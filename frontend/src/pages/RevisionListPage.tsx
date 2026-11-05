import { useCallback, useEffect, useState } from 'react'
import type { ApiResult } from '../api/http'
import { formatDateTime, type RevisionSummary } from '../api/revisions'
import { StatusBadge } from '../components/RevisionBits'

type Props = {
  title: string
  description?: string
  emptyText: string
  load: () => Promise<ApiResult<RevisionSummary[]>>
  onOpen: (id: string) => void
}

export default function RevisionListPage({ title, description, emptyText, load, onOpen }: Props) {
  const [items, setItems] = useState<RevisionSummary[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  const refresh = useCallback(async () => {
    setLoading(true)
    setError(null)
    const r = await load()
    setLoading(false)
    if (r.ok) setItems(r.data)
    else setError(r.error.message)
  }, [load])

  useEffect(() => { refresh() }, [refresh])

  return (
    <section className="card">
      <div className="card-head">
        <h2>{title}</h2>
        <button className="secondary" onClick={refresh} disabled={loading}>Osveži</button>
      </div>
      {description && <p className="muted">{description}</p>}

      {loading && items == null && <p className="muted">Učitavanje…</p>}
      {error && <p className="alert alert-error" role="alert">{error}</p>}
      {items != null && items.length === 0 && !error && <p className="muted">{emptyText}</p>}

      {items != null && items.length > 0 && (
        <table className="table">
          <thead>
            <tr><th>Laboratorija</th><th>Kurs</th><th>Autor</th><th>Status</th><th>Predata</th><th /></tr>
          </thead>
          <tbody>
            {items.map(r => (
              <tr key={r.id}>
                <td>{r.labTitle}</td>
                <td>{r.courseName}</td>
                <td>{r.author.displayName}</td>
                <td><StatusBadge status={r.status} /></td>
                <td>{r.submittedAt ? formatDateTime(r.submittedAt) : '—'}</td>
                <td><button className="link" onClick={() => onOpen(r.id)} aria-label={`Otvori ${r.labTitle}`}>Otvori</button></td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  )
}
