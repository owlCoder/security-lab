import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { getLabFilterOptions, MAX_SEARCH_LENGTH, searchLabs } from '../../api/labs'
import type { LabFilterOptions, LabSearchFilters, LabSummary, TestUser } from '../../api/labs'
import { failureLines, requestIdOf, statusOf } from './messages'
import ResultMessage from './ResultMessage'
import './labSearch.css'

interface Props {
  user: TestUser
}
type Found = { items: LabSummary[]; truncated: boolean }
type Failure = { lines: string[]; status?: number; requestId?: string }

const STATUS_NAMES: Record<string, string> = { Draft: 'Nacrt', Archived: 'Arhivirana' }
const DIFFICULTY_NAMES: Record<string, string> = { Easy: 'Laka', Medium: 'Srednja', Hard: 'Teška' }
const TYPE_NAMES: Record<string, string> = {
  AccessControl: 'Kontrola pristupa', LogAnalysis: 'Analiza logova', SecurityConfiguration: 'Bezbednosna konfiguracija',
  ThreatModeling: 'Modelovanje pretnji', MfaAndSessions: 'MFA i sesije', ContextualAuthorization: 'Kontekstualna autorizacija',
  IncidentResponse: 'Odgovor na incidente', IsolatedService: 'Izolovani servis',
}

function formatDate(value: string): string {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString('sr-Latn-RS')
}

export default function LabSearch({ user }: Props) {
  return <LabSearchForm key={user ? `${user.role}:${user.id}` : 'anonymous'} user={user} />
}

function LabSearchForm({ user }: Props) {
  const [query, setQuery] = useState('')
  const [filters, setFilters] = useState<LabSearchFilters>({ tags: [] })
  const [options, setOptions] = useState<LabFilterOptions | null>(null)
  const [optionsFailure, setOptionsFailure] = useState<Failure | null>(null)
  const [optionsAttempt, setOptionsAttempt] = useState(0)
  const [loading, setLoading] = useState(false)
  const [found, setFound] = useState<Found | null>(null)
  const [failure, setFailure] = useState<Failure | null>(null)
  const [queryError, setQueryError] = useState(false)
  const latestRequest = useRef(0)
  const userId = user?.id
  const userRole = user?.role

  useEffect(() => {
    let active = true
    setOptionsFailure(null)
    void getLabFilterOptions(user).then(outcome => {
      if (!active) return
      if (outcome.kind === 'ok') setOptions(outcome.options)
      else setOptionsFailure({ lines: failureLines(outcome), status: statusOf(outcome), requestId: requestIdOf(outcome) })
    })
    return () => { active = false }
  }, [userId, userRole, optionsAttempt])

  useEffect(() => () => { latestRequest.current += 1 }, [])

  function clearPreviousResult() {
    latestRequest.current += 1
    setLoading(false)
    setFound(null)
    setFailure(null)
    setQueryError(false)
  }

  function changeFilters(next: LabSearchFilters) {
    const removed = (filters.difficulty && !next.difficulty) || (filters.type && !next.type)
      || (filters.tags ?? []).some(tag => !next.tags?.includes(tag))
    if (removed) {
      removeFilter(next)
      return
    }
    clearPreviousResult()
    setFilters(next)
  }

  async function runSearch(nextFilters: LabSearchFilters) {
    const requestNumber = ++latestRequest.current
    setLoading(true)
    setFound(null)
    setFailure(null)
    setQueryError(false)
    const outcome = await searchLabs(query, user, nextFilters)
    if (requestNumber !== latestRequest.current) return
    setLoading(false)
    setQueryError(outcome.kind === 'invalid')
    if (outcome.kind === 'ok') {
      setFound({ items: outcome.items, truncated: outcome.truncated })
      return
    }
    setFailure({ lines: failureLines(outcome), status: statusOf(outcome), requestId: requestIdOf(outcome) })
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    void runSearch(filters)
  }

  function removeFilter(next: LabSearchFilters) {
    setFilters(next)
    void runSearch(next)
  }

  const selectedTags = filters.tags ?? []
  const hasFilters = Boolean(filters.difficulty || filters.type || selectedTags.length)
  const filtersDisabled = !options?.metadataAvailable

  return (
    <div className="labs-view">
      <form className="labs-form" role="search" onSubmit={handleSubmit} noValidate aria-busy={loading} aria-labelledby="labs-search-heading">
        <div className="labs-panel-head">
          <h3 id="labs-search-heading">Pretraga laboratorija</h3>
          <span className="labs-tag">LAB-05</span>
        </div>
        <div className="labs-search-access" role="note" aria-labelledby="labs-search-access-title">
          <svg className="labs-search-shield" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true">
            <path d="M12 3 4 6v6c0 4 4 7 8 9 4-2 8-5 8-9V6l-8-3Z" />
            <path d="m8 12 3 3 5-6" />
          </svg>
          <div>
            <p id="labs-search-access-title" className="labs-search-access-title">Samo laboratorije kojima imate pristup</p>
            <p>Rezultati se prilagođavaju vašim pravima pristupa. Laboratorije koje vam nisu dostupne neće biti prikazane.</p>
          </div>
        </div>
        <p className="hint">
          Pretražite po nazivu ili kombinujte postojeće osobine. Rezultat mora da odgovara svim izabranim filterima i oznakama.
        </p>
        <div className="labs-search-row">
          <div className="labs-field">
            <label htmlFor="labs-search-query">Naziv ili deo naziva</label>
            <input id="labs-search-query" type="search" value={query} maxLength={MAX_SEARCH_LENGTH} autoComplete="off" spellCheck={false}
              onChange={e => { clearPreviousResult(); setQuery(e.target.value) }} aria-describedby="labs-search-hint" aria-invalid={queryError} />
            <p id="labs-search-hint" className="hint labs-counter">
              Naziv je opcion. Najviše {MAX_SEARCH_LENGTH} znakova ({query.length}/{MAX_SEARCH_LENGTH}).
            </p>
          </div>
          <button type="submit" disabled={loading}>{loading ? 'Pretraga…' : 'Pretraži'}</button>
        </div>

        <fieldset className="labs-search-filters" disabled={filtersDisabled}>
          <legend>Filteri po osobinama</legend>
          <div className="labs-filter-selects">
            <div className="labs-field">
              <label htmlFor="labs-search-difficulty">Težina</label>
              <select id="labs-search-difficulty" value={filters.difficulty ?? ''} onChange={e => changeFilters({ ...filters, difficulty: e.target.value || undefined })}>
                <option value="">Sve težine</option>
                {options?.difficulties.map(value => <option key={value} value={value}>{DIFFICULTY_NAMES[value] ?? value}</option>)}
              </select>
            </div>
            <div className="labs-field">
              <label htmlFor="labs-search-type">Tip</label>
              <select id="labs-search-type" value={filters.type ?? ''} onChange={e => changeFilters({ ...filters, type: e.target.value || undefined })}>
                <option value="">Svi tipovi</option>
                {options?.types.map(value => <option key={value} value={value}>{TYPE_NAMES[value] ?? value}</option>)}
              </select>
            </div>
          </div>
          <fieldset className="labs-search-tags">
            <legend>Oznake</legend>
            {options?.tags.length ? <div className="labs-tag-options">
              {options.tags.map(tag => <label key={tag} className="labs-tag-option">
                <input type="checkbox" checked={selectedTags.includes(tag)} onChange={e => changeFilters({ ...filters,
                  tags: e.target.checked ? [...selectedTags, tag] : selectedTags.filter(value => value !== tag),
                })} /><span>{tag}</span>
              </label>)}
            </div> : <p className="hint">Nema dostupnih oznaka.</p>}
          </fieldset>
        </fieldset>

        {!options && !optionsFailure && <p className="hint">Učitavanje dostupnih filtera…</p>}
        {options && !options.metadataAvailable && <p className="hint">Filteri po osobinama trenutno nisu dostupni. Možete da pretražujete po nazivu ili prikažete sve dostupne laboratorije.</p>}
        {optionsFailure && <div>
          <ResultMessage tone="error" lines={['Dostupni filteri nisu učitani.', ...optionsFailure.lines]} status={optionsFailure.status} requestId={optionsFailure.requestId} />
          <button type="button" onClick={() => setOptionsAttempt(value => value + 1)}>Učitaj filtere ponovo</button>
        </div>}

        {hasFilters && <div className="labs-selected-filters" aria-label="Izabrani filteri">
          <p>Izabrani filteri:</p>
          <div className="labs-filter-chips">
            {filters.difficulty && <button type="button" className="labs-filter-chip" aria-label={`Ukloni filter težine: ${DIFFICULTY_NAMES[filters.difficulty] ?? filters.difficulty}`} onClick={() => removeFilter({ ...filters, difficulty: undefined })}>
              Težina: {DIFFICULTY_NAMES[filters.difficulty] ?? filters.difficulty} <span aria-hidden="true">×</span>
            </button>}
            {filters.type && <button type="button" className="labs-filter-chip" aria-label={`Ukloni filter tipa: ${TYPE_NAMES[filters.type] ?? filters.type}`} onClick={() => removeFilter({ ...filters, type: undefined })}>
              Tip: {TYPE_NAMES[filters.type] ?? filters.type} <span aria-hidden="true">×</span>
            </button>}
            {selectedTags.map(tag => <button key={tag} type="button" className="labs-filter-chip" aria-label={`Ukloni oznaku: ${tag}`} onClick={() => removeFilter({ ...filters, tags: selectedTags.filter(value => value !== tag) })}>
              Oznaka: {tag} <span aria-hidden="true">×</span>
            </button>)}
          </div>
          <button type="button" onClick={() => removeFilter({ tags: [] })}>Resetuj sve filtere</button>
          <p className="hint">Uklanjanje filtera odmah osvežava rezultate. Resetovanje zadržava uneti naziv.</p>
        </div>}
      </form>

      {failure && <div className="labs-search-denial">
        {(failure.status === 401 || failure.status === 403) && <h4>
          {failure.status === 401 ? 'Pristup pretrazi nije potvrđen' : 'Pretraga nije dozvoljena'}
        </h4>}
        <ResultMessage tone="error" lines={failure.lines} status={failure.status} requestId={failure.requestId} />
      </div>}
      {found && <div className="labs-search-results" role="region" aria-labelledby="labs-search-results-heading">
        <h4 id="labs-search-results-heading">Dostupne laboratorije</h4>
        <p id="labs-search-count" role="status" className="labs-search-count">
          {found.items.length === 0 ? 'Nijedna dostupna laboratorija ne odgovara pretrazi.' : `Pronađeno laboratorija: ${found.items.length}`}
        </p>
        {found.items.length === 0 && <div className="labs-search-empty">
          <p>Pokušajte sa drugim nazivom ili uklonite neki od filtera.</p>
          <p className="hint">Pretraga obuhvata samo laboratorije koje smete da pregledate.</p>
        </div>}
        {found.truncated && <p className="hint">Prikazani su samo prvi rezultati. Unesite precizniji naziv ili primenite filtere da biste suzili pretragu.</p>}
        {found.items.length > 0 && <ul className="labs-search-list">
          {found.items.map(lab => <li key={lab.id} className="labs-search-item">
            <div className="labs-search-item-head">
              <p className="labs-search-title">{lab.title}</p>
              <span className="labs-search-available">Dostupno</span>
            </div>
            <dl className="labs-meta">
              <div><dt>Status</dt><dd>{STATUS_NAMES[lab.status] ?? lab.status}</dd></div>
              <div><dt>Kreirana</dt><dd>{formatDate(lab.createdAt)}</dd></div>
              <div><dt>ID</dt><dd><code>{lab.id}</code></dd></div>
            </dl>
          </li>)}
        </ul>}
      </div>}
    </div>
  )
}
