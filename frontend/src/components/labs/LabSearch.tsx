import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { MAX_SEARCH_LENGTH, searchLabs } from '../../api/labs'
import type { LabSummary, TestUser } from '../../api/labs'
import { failureLines, requestIdOf, statusOf } from './messages'
import ResultMessage from './ResultMessage'
import './labSearch.css'

interface Props {
  user: TestUser
}

type Found = { items: LabSummary[]; truncated: boolean }
type Failure = { lines: string[]; status?: number; requestId?: string }

const STATUS_NAMES: Record<string, string> = { Draft: 'Nacrt', Archived: 'Arhivirana' }

function formatDate(value: string): string {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString('sr-Latn-RS')
}

export default function LabSearch({ user }: Props) {
  const [query, setQuery] = useState('')
  const [loading, setLoading] = useState(false)
  const [found, setFound] = useState<Found | null>(null)
  const [failure, setFailure] = useState<Failure | null>(null)
  const [queryError, setQueryError] = useState(false)
  const latestRequest = useRef(0)

  useEffect(() => {
    latestRequest.current += 1
    setFound(null)
    setFailure(null)
    setQueryError(false)
    setLoading(false)
  }, [user])

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (query.trim().length === 0) {
      setFound(null)
      setQueryError(true)
      setFailure({ lines: ['Unesite naziv ili deo naziva laboratorije.'] })
      return
    }

    const requestNumber = ++latestRequest.current
    setLoading(true)
    setFailure(null)
    const outcome = await searchLabs(query, user)
    if (requestNumber !== latestRequest.current) return
    setLoading(false)
    setQueryError(outcome.kind === 'invalid')

    if (outcome.kind === 'ok') {
      setFound({ items: outcome.items, truncated: outcome.truncated })
      return
    }
    setFound(null)
    setFailure({ lines: failureLines(outcome), status: statusOf(outcome), requestId: requestIdOf(outcome) })
  }

  return (
    <div className="labs-view">
      <form className="labs-form" role="search" onSubmit={handleSubmit} noValidate aria-busy={loading} aria-labelledby="labs-search-heading">
        <div className="labs-panel-head">
          <h3 id="labs-search-heading">Pretraga laboratorija</h3>
          <span className="labs-tag">LAB-05</span>
        </div>
        <p className="hint">
          Pretraga po nazivu ili delu naziva. Ne razlikuju se velika i mala slova ni kvačice (š = s).
          Server vraća samo laboratorije kojima imate pravo pristupa.
        </p>

        <div className="labs-search-row">
          <div className="labs-field">
            <label htmlFor="labs-search-query">Naziv ili deo naziva</label>
            <input
              id="labs-search-query"
              type="search"
              value={query}
              maxLength={MAX_SEARCH_LENGTH}
              autoComplete="off"
              spellCheck={false}
              onChange={e => setQuery(e.target.value)}
              aria-describedby="labs-search-hint"
              aria-invalid={queryError}
              required
            />
            <p id="labs-search-hint" className="hint labs-counter">
              Najviše {MAX_SEARCH_LENGTH} znakova ({query.length}/{MAX_SEARCH_LENGTH}).
            </p>
          </div>
          <button type="submit" disabled={loading}>{loading ? 'Pretraga…' : 'Pretraži'}</button>
        </div>
      </form>

      {failure && <ResultMessage tone="error" lines={failure.lines} status={failure.status} requestId={failure.requestId} />}

      {found && (
        <div className="labs-search-results">
          <p id="labs-search-count" role="status" className="labs-search-count">
            {found.items.length === 0
              ? 'Nijedna dostupna laboratorija ne odgovara pretrazi.'
              : `Pronađeno laboratorija: ${found.items.length}`}
          </p>
          {found.truncated && (
            <p className="hint">Prikazani su samo prvi rezultati. Unesite precizniji naziv da biste suzili pretragu.</p>
          )}
          {found.items.length > 0 && (
            <ul className="labs-search-list">
              {found.items.map(lab => (
                <li key={lab.id} className="labs-search-item">
                  <p className="labs-search-title">{lab.title}</p>
                  <dl className="labs-meta">
                    <div>
                      <dt>Status</dt>
                      <dd>{STATUS_NAMES[lab.status] ?? lab.status}</dd>
                    </div>
                    <div>
                      <dt>Kreirana</dt>
                      <dd>{formatDate(lab.createdAt)}</dd>
                    </div>
                    <div>
                      <dt>ID</dt>
                      <dd><code>{lab.id}</code></dd>
                    </div>
                  </dl>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  )
}
