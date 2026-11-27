import { useState } from 'react'
import type { FormEvent } from 'react'
import type { CreateCourseRequest } from '../../types/course'

import {
  normalizeCourse,
  validateCourse,
} from '../../lib/courseRules'

interface Props {
  onSave: (input: CreateCourseRequest) => Promise<void>
  onCancel: () => void
  busy: boolean
}

export default function CourseForm({
  onSave,
  onCancel,
  busy,
}: Props) {
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')

  const [error, setError] =
    useState<string | null>(null)

  async function submit(
    event: FormEvent<HTMLFormElement>,
  ) {
    event.preventDefault()

    if (busy) return

    const value = normalizeCourse({
      code,
      name,
      description,
    })

    const validationError = validateCourse(value)

    if (validationError) {
      setError(validationError)
      return
    }

    setError(null)

    await onSave(value)
  }

  return (
    <form
      className="course-form"
      onSubmit={submit}
      noValidate
      aria-busy={busy}
    >
      <h3>Kreiranje novog kursa</h3>

      <div className="form-group">
        <label htmlFor="course-code">
          Šifra kursa *
        </label>

        <input
          id="course-code"
          value={code}
          onChange={(event) => {
            setCode(event.target.value)
            setError(null)
          }}
          maxLength={32}
          required
          disabled={busy}
          placeholder="Npr. OIB-2026"
          aria-describedby="course-code-hint"
        />

        <small
          id="course-code-hint"
          className="field-hint"
        >
          Jedinstvena šifra kursa.
          {` ${code.length}/32`}
        </small>
      </div>

      <div className="form-group">
        <label htmlFor="course-name">
          Naziv kursa *
        </label>

        <input
          id="course-name"
          value={name}
          onChange={(event) => {
            setName(event.target.value)
            setError(null)
          }}
          maxLength={200}
          required
          disabled={busy}
          placeholder="Naziv kursa"
          aria-describedby="course-name-hint"
        />

        <small
          id="course-name-hint"
          className="field-hint"
        >
          {name.length}/200
        </small>
      </div>

      <div className="form-group">
        <label htmlFor="course-description">
          Opis kursa (opciono)
        </label>

        <textarea
          id="course-description"
          value={description}
          onChange={(event) => {
            setDescription(event.target.value)
            setError(null)
          }}
          maxLength={4000}
          rows={4}
          disabled={busy}
          placeholder="Kratak opis kursa"
          aria-describedby="course-description-hint"
        />

        <small
          id="course-description-hint"
          className="field-hint"
        >
          {description.length}/4000
        </small>
      </div>

      {error && (
        <p className="form-error" role="alert">
          {error}
        </p>
      )}

      <div className="form-actions">
        <button
          type="button"
          className="secondary-button"
          onClick={onCancel}
          disabled={busy}
        >
          Otkaži
        </button>

        <button
          type="submit"
          className="primary-button"
          disabled={busy}
        >
          {busy ? 'Čuvanje...' : 'Sačuvaj kurs'}
        </button>
      </div>
    </form>
  )
}