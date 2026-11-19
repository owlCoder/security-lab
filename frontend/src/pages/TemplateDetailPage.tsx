import { useState } from 'react'
import { authoringErrorMessage, createDraftFromTemplate } from '../api/authoring'
import type { LabDraft, LabTemplate } from '../types/authoring'

type Props = {
  template: LabTemplate
  onBack: () => void
}

export default function TemplateDetailPage({ template, onBack }: Props) {
  const [creating, setCreating] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [draft, setDraft] = useState<LabDraft | null>(null)
  const [title, setTitle] = useState('')
  const [goal, setGoal] = useState('')

  async function create() {
    setCreating(true)
    setError(null)
    const r = await createDraftFromTemplate(template.id)
    setCreating(false)
    if (r.ok) {
      setDraft(r.data)
      setTitle('')
      setGoal('')
    } else {
      setError(authoringErrorMessage(r.error))
    }
  }

  return (
    <section className="card">
      <div className="card-head">
        <h2>{template.title}</h2>
        <button className="secondary" onClick={onBack}>← Nazad na šablone</button>
      </div>
      <p className="muted">Tip: {template.type}</p>
      <p>{template.goal}</p>

      {!draft && (
        <button onClick={create} disabled={creating}>
          {creating ? 'Kreiranje…' : 'Kreiraj zadatak iz šablona'}
        </button>
      )}

      {error && <p className="alert alert-error" role="alert">{error}</p>}

      {draft && (
        <>
          <p className="alert alert-success" role="status">
            <strong>Zadatak je kreiran.</strong> Dobio je novi ID, a šablon je ostao nepromenjen.
          </p>

          <h3>Novi zadatak (nacrt)</h3>
          <p className="muted">Novi ID: {draft.id}</p>
          <p className="muted">Izvorni šablon: {draft.sourceTemplateId ?? template.id} (ID se nije promenio)</p>
          <p className="muted">Status: {draft.status} · Verzija: {draft.version}</p>

          <form onSubmit={e => e.preventDefault()}>
            <label htmlFor="draft-title">Naslov</label>
            <input
              id="draft-title"
              value={title}
              onChange={e => setTitle(e.target.value)}
              placeholder={draft.title}
              maxLength={120}
            />

            <label htmlFor="draft-goal">Cilj</label>
            <textarea
              id="draft-goal"
              value={goal}
              onChange={e => setGoal(e.target.value)}
              placeholder={draft.goal}
              maxLength={2000}
              rows={4}
            />

            <button type="submit" disabled>Sačuvaj</button>
            <p className="muted">Čuvanje unosa biće moguće kada backend izloži izmenu nacrta (AUT-01).</p>
          </form>

          <button className="secondary" onClick={() => setDraft(null)}>Kreiraj još jedan</button>
        </>
      )}
    </section>
  )
}