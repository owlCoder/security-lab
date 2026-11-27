import { useState } from 'react'
import type { SyntheticEvent } from 'react'
import type { LabDevUser } from './api'
import StudentLabPage from './StudentLabPage'
import LabPreviewPage from './LabPreviewPage'
import './labs.css'

export default function LabVisibilitySection() {
  const [labIdInput, setLabIdInput] = useState('')
  const [labId, setLabId] = useState('')
  const [user, setUser] = useState<LabDevUser>('student')
  const [mode, setMode] = useState<'student' | 'preview'>('student')
  const [requestNumber, setRequestNumber] = useState(0)

  function handleSubmit(event: SyntheticEvent<HTMLFormElement>) {
    event.preventDefault()

    const enteredId = labIdInput.trim()

    if (enteredId === '') {
      return
    }

    setLabId(enteredId)
    setRequestNumber(current => current + 1)
  }

  return (
    <section
      className="card lab-visibility-section"
      aria-labelledby="lab-visibility-heading"
    >
      <h2 id="lab-visibility-heading">
        Studentski prikaz i pregled laboratorije
      </h2>

      {import.meta.env.DEV && (
        <div>
          <label htmlFor="lab-dev-user">Razvojni korisnik</label>
          <select
            id="lab-dev-user"
            value={user}
            onChange={event => setUser(event.target.value as LabDevUser)}
          >
            <option value="teacher">Nastavnik</option>
            <option value="student">Student</option>
            <option value="outsider">Van kursa</option>
          </select>
        </div>
      )}

      <div role="group" aria-label="Izbor prikaza laboratorije">
        <button
          type="button"
          aria-pressed={mode === 'student'}
          onClick={() => setMode('student')}
        >
          Studentski prikaz
        </button>
        <button
          type="button"
          aria-pressed={mode === 'preview'}
          onClick={() => setMode('preview')}
        >
          Pregled pre objave
        </button>
      </div>

      <form onSubmit={handleSubmit}>
        <label htmlFor="lab-visibility-id">ID laboratorije</label>
        <input
          id="lab-visibility-id"
          type="text"
          value={labIdInput}
          onChange={event => setLabIdInput(event.target.value)}
          required
        />
        <button type="submit" disabled={labIdInput.trim() === ''}>
          Prikaži laboratoriju
        </button>
      </form>

      {labId !== '' && mode === 'student' && (
        <StudentLabPage
          key={requestNumber}
          labId={labId}
          user={user}
        />
      )}

      {labId !== '' && mode === 'preview' && (
        <LabPreviewPage
          key={requestNumber}
          labId={labId}
          user={user}
        />
      )}
    </section>
  )
}