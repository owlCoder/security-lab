import { useState } from 'react'
import type { FormEvent } from 'react'
import { createLab, MAX_TITLE_LENGTH } from '../../api/labs'
import type { TestUser } from '../../api/labs'
import { failureLines, requestIdOf, statusOf } from './messages'
import ResultMessage from './ResultMessage'
import type { ResultTone } from './ResultMessage'

interface Props {
  user: TestUser
}

type Result = { tone: ResultTone; lines: string[]; status?: number; requestId?: string }

export default function CreateLabForm({ user }: Props) {
  const [title, setTitle] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [result, setResult] = useState<Result | null>(null)
  const [titleError, setTitleError] = useState(false)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    // Samo pomoć korisniku. Isto pravilo server proverava sam (400 za neispravan naslov).
    if (title.trim().length === 0) {
      setTitleError(true)
      setResult({ tone: 'error', lines: ['Unesite naslov laboratorije.'] })
      return
    }

    setTitleError(false)
    setSubmitting(true)
    setResult(null)
    const outcome = await createLab(title, user)
    setSubmitting(false)

    if (outcome.kind === 'created') {
      setTitle('')
      setResult({ tone: 'ok', status: 201, lines: [`Laboratorija je kreirana. ID: ${outcome.id}`] })
      return
    }
    setTitleError(outcome.kind === 'invalid')
    // 501: server je pustio nastavnika kroz proveru prava, ali LAB-01 kreiranje još nije napravljeno.
    const tone: ResultTone = outcome.kind === 'notImplemented' ? 'info' : 'error'
    setResult({ tone, lines: failureLines(outcome), status: statusOf(outcome), requestId: requestIdOf(outcome) })
  }

  return (
    <form className="labs-form" onSubmit={handleSubmit} noValidate aria-busy={submitting} aria-labelledby="labs-create-heading">
      <div className="labs-panel-head">
        <h3 id="labs-create-heading">Nova laboratorija</h3>
        <span className="labs-tag">LAB-01</span>
      </div>
      <p className="hint">Kreiranje je dozvoljeno samo nastavniku. ID dodeljuje server.</p>

      <div className="labs-field">
        <label htmlFor="labs-title">Naslov laboratorije</label>
        <input
          id="labs-title"
          type="text"
          value={title}
          maxLength={MAX_TITLE_LENGTH}
          onChange={e => setTitle(e.target.value)}
          aria-describedby="labs-title-hint"
          aria-invalid={titleError}
          required
        />
        <p id="labs-title-hint" className="hint labs-counter">
          Obavezno, najviše {MAX_TITLE_LENGTH} znakova ({title.length}/{MAX_TITLE_LENGTH}).
        </p>
      </div>

      <button type="submit" disabled={submitting}>
        {submitting ? 'Kreiranje…' : 'Kreiraj laboratoriju'}
      </button>
      {result && <ResultMessage {...result} />}
    </form>
  )
}
