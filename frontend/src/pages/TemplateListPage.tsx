import { useCallback, useEffect, useState } from 'react'
import { authoringErrorMessage, listTemplates } from '../api/authoring'
import type { LabTemplate } from '../types/authoring'

type Props = {
  onOpen: (template: LabTemplate) => void
}

export default function TemplateListPage({ onOpen }: Props) {
  const [items, setItems] = useState<LabTemplate[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  const refresh = useCallback(async () => {
    setLoading(true)
    setError(null)
    const r = await listTemplates()
    setLoading(false)
    if (r.ok) setItems(r.data)
    else setError(authoringErrorMessage(r.error))
  }, [])

  useEffect(() => { refresh() }, [refresh])

  return (
    <section className="card">
      <div className="card-head">
        <h2>Šabloni laboratorija</h2>
        <button className="secondary" onClick={refresh} disabled={loading}>Osveži</button>
      </div>
      <p className="muted">Izaberite gotov šablon i od njega napravite novi zadatak (nacrt). Izvorni šablon se ne menja.</p>

      {loading && items == null && <p className="muted">Učitavanje…</p>}
      {error && <p className="alert alert-error" role="alert">{error}</p>}
      {items != null && items.length === 0 && !error && <p className="muted">Nema dostupnih šablona.</p>}

      {items != null && items.length > 0 && (
        <table className="table">
          <thead>
            <tr><th>Naslov</th><th>Cilj</th><th>Tip</th><th /></tr>
          </thead>
          <tbody>
            {items.map(t => (
              <tr key={t.id}>
                <td>{t.title}</td>
                <td>{t.goal}</td>
                <td>{t.type}</td>
                <td><button className="link" onClick={() => onOpen(t)} aria-label={`Otvori ${t.title}`}>Detalji</button></td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  )
}