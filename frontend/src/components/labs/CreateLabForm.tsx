import { useState } from 'react'
import type { FormEvent } from 'react'
import { createLab, DEV_DEMO_COURSES, INVALID_COURSE_MESSAGE, MAX_TITLE_LENGTH } from '../../api/labs'
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
  // U razvoju se bira jedan od demo kurseva; van razvoja se unosi ID kursa.
  const [courseId, setCourseId] = useState(import.meta.env.DEV ? DEV_DEMO_COURSES[0].id : '')
  const [submitting, setSubmitting] = useState(false)
  const [result, setResult] = useState<Result | null>(null)
  const [titleError, setTitleError] = useState(false)
  const [courseError, setCourseError] = useState(false)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    // Samo pomoć korisniku. Ista pravila server proverava sam (400 za neispravan naslov ili kurs).
    const missingTitle = title.trim().length === 0
    const missingCourse = courseId.trim().length === 0
    if (missingTitle || missingCourse) {
      setTitleError(missingTitle)
      setCourseError(missingCourse)
      const lines = [
        ...(missingTitle ? ['Unesite naslov laboratorije.'] : []),
        ...(missingCourse ? ['Izaberite kurs.'] : []),
      ]
      setResult({ tone: 'error', lines })
      return
    }

    setTitleError(false)
    setCourseError(false)
    setSubmitting(true)
    setResult(null)
    const outcome = await createLab(title, courseId.trim(), user)
    setSubmitting(false)

    if (outcome.kind === 'created') {
      setTitle('')
      setResult({ tone: 'ok', status: 201, lines: [`Laboratorija je kreirana. ID: ${outcome.id}`] })
      return
    }

    // 400 se vezuje za polje na koje se odnosi: kurs ili naslov.
    const courseRejected = outcome.kind === 'invalid' && outcome.messages.includes(INVALID_COURSE_MESSAGE)
    setCourseError(courseRejected)
    setTitleError(outcome.kind === 'invalid' && !courseRejected)
    setResult({ tone: 'error', lines: failureLines(outcome), status: statusOf(outcome), requestId: requestIdOf(outcome) })
  }

  return (
    <form className="labs-form" onSubmit={handleSubmit} noValidate aria-busy={submitting} aria-labelledby="labs-create-heading">
      <div className="labs-panel-head">
        <h3 id="labs-create-heading">Nova laboratorija</h3>
        <span className="labs-tag">LAB-01</span>
      </div>
      <p className="hint">Kreiranje je dozvoljeno samo nastavniku izabranog kursa. ID, autora i status dodeljuje server.</p>

      <div className="labs-field">
        <label htmlFor="labs-course">Kurs</label>
        {import.meta.env.DEV ? (
          <select
            id="labs-course"
            value={courseId}
            onChange={e => setCourseId(e.target.value)}
            aria-describedby="labs-course-hint"
            aria-invalid={courseError}
            required
          >
            {DEV_DEMO_COURSES.map(c => <option key={c.id} value={c.id}>{c.label}</option>)}
          </select>
        ) : (
          <input
            id="labs-course"
            type="text"
            value={courseId}
            onChange={e => setCourseId(e.target.value)}
            aria-describedby="labs-course-hint"
            aria-invalid={courseError}
            required
          />
        )}
        <p id="labs-course-hint" className="hint">
          {import.meta.env.DEV
            ? <>Demo kursevi iz razvojnog režima <span className="badge-fake">FAKE</span>. Server proverava da ste nastavnik na kursu.</>
            : 'ID kursa na kome predajete. Server proverava da ste nastavnik na kursu.'}
        </p>
      </div>

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