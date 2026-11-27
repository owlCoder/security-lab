import { useEffect, useRef, useState } from 'react'
import { useAuth } from '../../auth/AuthContext'
import {
  addLaboratoryCompetency, CompetencyApiError, loadCompetencyCatalog,
  loadLaboratoryCompetencies, removeLaboratoryCompetency,
} from '../../api/competencies'
import type { Competency, LaboratoryCompetencies as LaboratoryData } from '../../api/competencies'
import type { AuthContextValue } from '../../types/auth'
import './competencies.css'

type Props = { laboratoryId: string }
type WorkspaceData = { laboratory: LaboratoryData; catalog: Competency[] | null; catalogError: string | null }
type LoadState = { status: 'loading' } | { status: 'error'; message: string } | { status: 'ready'; data: WorkspaceData }

function messageOf(error: unknown): string {
  return error instanceof CompetencyApiError ? error.message : 'Kompetencije nisu učitane. Pokušajte ponovo.'
}

function Icon({ kind }: { kind: 'back' | 'remove' | 'lock' }) {
  return <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
    {kind === 'back' && <path d="m10 5-7 7 7 7M3 12h18" />}
    {kind === 'remove' && <path d="m7 7 10 10M17 7 7 17" />}
    {kind === 'lock' && <><rect x="5" y="10" width="14" height="11" rx="2" /><path d="M8 10V7a4 4 0 0 1 8 0v3" /></>}
  </svg>
}

export default function LaboratoryCompetencies({ laboratoryId }: Props) {
  const { request, user } = useAuth()
  return <CompetencyWorkspace key={`${user?.userId ?? 'anonymous'}:${laboratoryId}`} laboratoryId={laboratoryId} request={request} />
}

function CompetencyWorkspace({ laboratoryId, request }: Props & { request: AuthContextValue['request'] }) {
  const [state, setState] = useState<LoadState>({ status: 'loading' })
  const [attempt, setAttempt] = useState(0)
  const [query, setQuery] = useState('')
  const [pendingId, setPendingId] = useState<string | null>(null)
  const [announcement, setAnnouncement] = useState('')
  const mutation = useRef<AbortController | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    setState({ status: 'loading' })
    setAnnouncement('')
    void (async () => {
      try {
        const laboratory = await loadLaboratoryCompetencies(request, laboratoryId, controller.signal)
        let catalog: Competency[] | null = null
        let catalogError: string | null = null
        if (laboratory.canManage) {
          try { catalog = await loadCompetencyCatalog(request, controller.signal) }
          catch (error) {
            if (controller.signal.aborted) return
            if (error instanceof CompetencyApiError && error.status === 401) throw error
            catalogError = messageOf(error)
          }
        }
        if (!controller.signal.aborted) setState({ status: 'ready', data: { laboratory, catalog, catalogError } })
      } catch (error) {
        if (!controller.signal.aborted) setState({ status: 'error', message: messageOf(error) })
      }
    })()
    return () => { controller.abort() }
  }, [laboratoryId, request, attempt])

  useEffect(() => () => { mutation.current?.abort() }, [])

  async function changeLink(competency: Competency, add: boolean) {
    if (state.status !== 'ready' || !state.data.laboratory.canManage || mutation.current) return
    const controller = new AbortController()
    mutation.current = controller
    setPendingId(competency.id)
    setAnnouncement(`Čuvanje: ${competency.name}…`)
    try {
      const change = add ? addLaboratoryCompetency : removeLaboratoryCompetency
      await change(request, laboratoryId, competency.id, controller.signal)
      const laboratory = await loadLaboratoryCompetencies(request, laboratoryId, controller.signal)
      if (controller.signal.aborted) return
      setState({ status: 'ready', data: { ...state.data, laboratory } })
      setAnnouncement(`Kompetencija „${competency.name}” je ${add ? 'dodata' : 'uklonjena'}.`)
    } catch (error) {
      if (!controller.signal.aborted) {
        setState({ status: 'error', message: `Izmena nije potvrđena. ${messageOf(error)} Učitajte prikaz ponovo da proverite sačuvano stanje.` })
        setAnnouncement('')
      }
    } finally {
      if (!controller.signal.aborted) {
        mutation.current = null
        setPendingId(null)
      }
    }
  }

  const retry = () => setAttempt(value => value + 1)

  return <article className="competencies" lang="sr-Latn" aria-labelledby="competencies-heading">
    <a className="competencies-back" href="/"><Icon kind="back" />Nazad na laboratorije</a>
    <header className="competencies-heading">
      <h1 id="competencies-heading">Kompetencije laboratorije</h1>
    </header>

    {state.status === 'loading' && <section className="competencies-feedback" role="status" aria-busy="true">
      <p>Učitavanje kompetencija…</p>
    </section>}

    {state.status === 'error' && <section className="competencies-feedback">
      <h2>Kompetencije nisu dostupne</h2>
      <p role="alert">{state.message}</p>
      <button type="button" onClick={retry}>Učitaj ponovo</button>
    </section>}

    {state.status === 'ready' && <><section className="competencies-workspace" aria-label="Kompetencije izabrane laboratorije" aria-busy={pendingId !== null}>
      <div className="competencies-context">
        <div><p className="competencies-context-label">Laboratorija</p><h2>{state.data.laboratory.laboratoryTitle}</h2></div>
        {!state.data.laboratory.canManage && <span className="competencies-access"><Icon kind="lock" />Samo pregled</span>}
      </div>

      <div className={`competencies-grid${state.data.laboratory.canManage ? '' : ' competencies-grid-readonly'}`}>
        {state.data.laboratory.canManage && <div className="competencies-catalog">
          <h3>Katalog kompetencija</h3>
          {state.data.catalogError ? <div className="competencies-catalog-error">
            <p role="alert">Katalog nije učitan. {state.data.catalogError}</p>
            <button type="button" className="secondary" onClick={retry}>Učitaj katalog ponovo</button>
          </div> : <>
            <div className="competencies-search">
              <label className="competencies-sr-only" htmlFor="competencies-query">Pretražite kompetencije</label>
              <input id="competencies-query" type="search" value={query} maxLength={120} autoComplete="off"
                placeholder="Pretražite kompetencije..." onChange={event => setQuery(event.target.value)} />
            </div>
            <fieldset className="competencies-options" disabled={pendingId !== null}>
              <legend className="competencies-sr-only">Dostupne kompetencije</legend>
              {state.data.catalog?.filter(item => `${item.name} ${item.description ?? ''}`.toLocaleLowerCase('sr').includes(query.trim().toLocaleLowerCase('sr')))
                .map(item => {
                  const selected = state.data.laboratory.competencies.some(link => link.id === item.id)
                  return <label key={item.id} className={`competencies-option${selected ? ' is-selected' : ''}`}>
                    <input type="checkbox" checked={selected} onChange={event => void changeLink(item, event.target.checked)}
                      aria-label={item.name} aria-describedby={item.description ? `competency-description-${item.id}` : undefined} />
                    <span><strong>{item.name}</strong>{item.description && <span id={`competency-description-${item.id}`} className="competencies-description">{item.description}</span>}
                      {pendingId === item.id && <span className="competencies-saving">Čuvanje izmene…</span>}</span>
                  </label>
                })}
            </fieldset>
            {state.data.catalog?.length === 0 && <p className="competencies-empty">U katalogu trenutno nema kompetencija.</p>}
            {Boolean(state.data.catalog?.length) && !state.data.catalog?.some(item => `${item.name} ${item.description ?? ''}`.toLocaleLowerCase('sr').includes(query.trim().toLocaleLowerCase('sr'))) &&
              <div className="competencies-empty"><p>Nema kompetencija koje odgovaraju pretrazi.</p><button type="button" className="competencies-text-button" onClick={() => setQuery('')}>Obriši pretragu</button></div>}
          </>}
        </div>}

        <aside className="competencies-linked" aria-labelledby="competencies-linked-heading">
          <div className="competencies-linked-heading"><h3 id="competencies-linked-heading">Povezane kompetencije <span className="competencies-count">({state.data.laboratory.competencies.length})</span></h3>
          </div>
          {state.data.laboratory.competencies.length > 0 ? <ul className="competencies-linked-list">
            {state.data.laboratory.competencies.map(item => <li key={item.id}>
              <div><strong>{item.name}</strong>{!state.data.laboratory.canManage && item.description && <p className="competencies-description">{item.description}</p>}</div>
              {state.data.laboratory.canManage && <button type="button" className="competencies-remove" disabled={pendingId !== null}
                aria-label={`Ukloni kompetenciju: ${item.name}`} onClick={() => void changeLink(item, false)}><Icon kind="remove" /></button>}
            </li>)}
          </ul> : <div className="competencies-empty"><p>Još nema povezanih kompetencija.</p>
            <p className="competencies-help">{state.data.laboratory.canManage ? 'Izaberite kompetencije iz kataloga da opišete šta laboratorija proverava.' : 'Nastavnik još nije povezao kompetencije sa ovom laboratorijom.'}</p>
          </div>}
          {!state.data.laboratory.canManage && <p className="competencies-readonly-note"><Icon kind="lock" />Kompetencije uređuje ovlašćeni nastavnik.</p>}
        </aside>
      </div>
    </section>
      {state.data.laboratory.canManage && <p className="competencies-status" role="status" aria-live="polite" aria-atomic="true">
        {announcement || 'Izmene se automatski čuvaju.'}
      </p>}
    </>}
  </article>
}
