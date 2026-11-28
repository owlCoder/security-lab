import { useState } from 'react'
import type { FormEvent } from 'react'
import {
  DEFAULT_LAB_TYPE,
  LAB_TYPES,
  MAX_GOAL_LENGTH,
  MAX_TITLE_LENGTH,
} from './DraftTypes'
import type { CreateDraftData, LabType } from './DraftTypes'

type Props = {
  /** Vraća true kada je nacrt kreiran. Polja forme se brišu tek tada. */
  onCreate: (draft: CreateDraftData) => Promise<boolean>
  submitting?: boolean
  error?: string
}

export default function CreateDraftForm({ onCreate, submitting = false, error = '' }: Props) {
  const [title, setTitle] = useState('')
  const [goal, setGoal] = useState('')
  const [type, setType] = useState<LabType>(DEFAULT_LAB_TYPE)
  const [titleError, setTitleError] = useState(false)

  function resetForm() {
    setTitle('')
    setGoal('')
    setType(DEFAULT_LAB_TYPE)
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (submitting) return

    const cleanTitle = title.trim()

    if (cleanTitle.length === 0) {
      setTitleError(true)
      return
    }

    setTitleError(false)

    const created = await onCreate({
      title: cleanTitle,
      goal: goal.trim(),
      type,
    })

    if (created) resetForm()
  }

  const titleDescribedBy = titleError
    ? 'draft-title-hint draft-title-error'
    : 'draft-title-hint'

  return (
    // noValidate: validaciju radimo sami, da browser ne prikaže svoj balončić
    // umesto naše poruke (a server svakako validira ponovo).
    <form className="drafts-form" onSubmit={handleSubmit} noValidate>
      <h3>Novi nacrt zadatka</h3>

      <div className="drafts-field">
        <label htmlFor="draft-title">Naziv</label>

        <input
          id="draft-title"
          type="text"
          value={title}
          maxLength={MAX_TITLE_LENGTH}
          onChange={event => setTitle(event.target.value)}
          aria-invalid={titleError}
          aria-required="true"
          aria-describedby={titleDescribedBy}
        />

        <p id="draft-title-hint" className="hint">
          Obavezno, najviše {MAX_TITLE_LENGTH} karaktera.
          ({title.length}/{MAX_TITLE_LENGTH})
        </p>

        {titleError && (
          <p id="draft-title-error" className="error-text" role="alert">
            Unesite naziv nacrta.
          </p>
        )}
      </div>

      <div className="drafts-field">
        <label htmlFor="draft-goal">Cilj zadatka</label>

        <textarea
          id="draft-goal"
          value={goal}
          maxLength={MAX_GOAL_LENGTH}
          onChange={event => setGoal(event.target.value)}
          rows={4}
        />

        <p className="hint">
          Opciono, najviše {MAX_GOAL_LENGTH} karaktera.
        </p>
      </div>

      <div className="drafts-field">
        <label htmlFor="draft-type">Tip laboratorije</label>

        <select
          id="draft-type"
          value={type}
          onChange={event => setType(event.target.value as LabType)}
        >
          {LAB_TYPES.map(labType => (
            <option key={labType.value} value={labType.value}>
              {labType.label}
            </option>
          ))}
        </select>
      </div>

      {error && (
        <p className="error-text" role="alert">
          {error}
        </p>
      )}

      <button type="submit" disabled={submitting} aria-busy={submitting}>
        {submitting ? 'Kreiranje…' : 'Kreiraj nacrt'}
      </button>
    </form>
  )
}
