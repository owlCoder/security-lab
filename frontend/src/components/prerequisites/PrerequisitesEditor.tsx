
import { useEffect, useState } from 'react'
import {
  getPrerequisites,
  addPrerequisite,
  removePrerequisite,
} from '../../api/prerequisites'

interface PrerequisitesEditorProps {
  laboratoryId: string
}

export default function PrerequisitesEditor({
  laboratoryId,
}: PrerequisitesEditorProps) {
  const [prerequisites, setPrerequisites] = useState<string[]>([])
  const [newPrerequisiteId, setNewPrerequisiteId] = useState('')
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [success, setSuccess] = useState('')

  useEffect(() => {
    let active = true

    async function loadPrerequisites() {
      const result = await getPrerequisites(laboratoryId)

      if (!active) return

      if (result.ok) {
        setPrerequisites(result.data)
        setError('')
      } else {
        setPrerequisites([])
        setError(result.error.message)
      }

      setLoading(false)
    }

    setLoading(true)
    setError('')
    setSuccess('')
    setPrerequisites([])
    void loadPrerequisites()

    return () => {
      active = false
    }
  }, [laboratoryId])

  async function handleAdd() {
    const requiredId = newPrerequisiteId.trim()
    if (!requiredId || saving) return

    setSaving(true)
    setError('')
    setSuccess('')

    const result = await addPrerequisite(laboratoryId, requiredId)

    if (!result.ok) {
      setError(result.error.message)
    } else {
      const updated = await getPrerequisites(laboratoryId)

      if (updated.ok) {
        setPrerequisites(updated.data)
        setNewPrerequisiteId('')
        setSuccess('Preduslov je uspešno dodat.')
      } else {
        setError(updated.error.message)
      }
    }

    setSaving(false)
  }

  async function handleRemove(requiredId: string) {
    if (saving) return

    setSaving(true)
    setError('')
    setSuccess('')

    const result = await removePrerequisite(laboratoryId, requiredId)

    if (!result.ok) {
      setError(result.error.message)
    } else {
      const updated = await getPrerequisites(laboratoryId)

      if (updated.ok) {
        setPrerequisites(updated.data)
        setSuccess('Preduslov je uspešno uklonjen.')
      } else {
        setError(updated.error.message)
      }
    }

    setSaving(false)
  }

  return (
    <section aria-label="Upravljanje preduslovima">
      <h2>Upravljanje preduslovima</h2>

      <h3>Postojeći preduslovi</h3>

      {loading ? (
        <p>Učitavanje preduslova...</p>
      ) : (
        <>
          {prerequisites.length === 0 && !error && (
            <p>Laboratorija nema definisane preduslove.</p>
          )}

          <ul>
            {prerequisites.map(id => (
              <li key={id}>
                <span>{id}</span>{' '}
                <button
                  type="button"
                  disabled={saving}
                  onClick={() => void handleRemove(id)}
                >
                  Ukloni
                </button>
              </li>
            ))}
          </ul>
        </>
      )}

      <h3>Dodavanje preduslova</h3>

      <label htmlFor="new-prerequisite">
        ID laboratorije koja je preduslov
      </label>

      <input
        id="new-prerequisite"
        type="text"
        value={newPrerequisiteId}
        onChange={event => setNewPrerequisiteId(event.target.value)}
        disabled={saving || loading}
      />

      <button
        type="button"
        disabled={saving || loading || !newPrerequisiteId.trim()}
        onClick={() => void handleAdd()}
      >
        {saving ? 'Čuvanje...' : 'Dodaj preduslov'}
      </button>

      {error && <p role="alert">{error}</p>}
      {success && <p role="status">{success}</p>}
    </section>
  )
}
