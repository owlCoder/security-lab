import { useEffect, useMemo, useState } from 'react'
import LabCard from './LabCard'
import { getCatalog, type CatalogOutcome } from './catalogApi'
import { allTags, EMPTY_FILTERS, filterLabs, type CatalogFilters } from './catalogFilters'
import {
  DIFFICULTIES,
  DIFFICULTY_LABELS,
  LAB_TYPES,
  TYPE_LABELS,
  type CatalogLab,
  type Difficulty,
  type LabType,
} from './catalogTypes'
import './catalog.css'

type State =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; labs: CatalogLab[] }

type Props = {
  load?: () => Promise<CatalogOutcome>
}

export default function LabCatalog({ load = getCatalog }: Props) {
  const [state, setState] = useState<State>({ status: 'loading' })
  const [filters, setFilters] = useState<CatalogFilters>(EMPTY_FILTERS)

  useEffect(() => {
    let active = true
    load().then(result => {
      if (!active) return
      setState(result.ok ? { status: 'ready', labs: result.labs } : { status: 'error', message: result.message })
    })
    return () => { active = false }
  }, [load])

  const labs = state.status === 'ready' ? state.labs : []
  const tags = useMemo(() => allTags(labs), [labs])
  const visible = useMemo(() => filterLabs(labs, filters), [labs, filters])

  const update = (patch: Partial<CatalogFilters>) => setFilters(prev => ({ ...prev, ...patch }))

  return (
    <section className="card catalog">
      <h2>Katalog laboratorija</h2>

      {state.status === 'loading' && <p>Učitavanje kataloga…</p>}
      {state.status === 'error' && <p className="catalog-error" role="alert">{state.message}</p>}

      {state.status === 'ready' && <>
        <div className="catalog-filters">
          <label htmlFor="catalog-search">Pretraga</label>
          <input
            id="catalog-search"
            type="search"
            maxLength={100}
            value={filters.search}
            onChange={e => update({ search: e.target.value })}
          />

          <label htmlFor="catalog-type">Tip</label>
          <select id="catalog-type" value={filters.type} onChange={e => update({ type: e.target.value as LabType | '' })}>
            <option value="">Svi tipovi</option>
            {LAB_TYPES.map(t => <option key={t} value={t}>{TYPE_LABELS[t]}</option>)}
          </select>

          <label htmlFor="catalog-difficulty">Težina</label>
          <select
            id="catalog-difficulty"
            value={filters.difficulty}
            onChange={e => update({ difficulty: e.target.value as Difficulty | '' })}
          >
            <option value="">Sve težine</option>
            {DIFFICULTIES.map(d => <option key={d} value={d}>{DIFFICULTY_LABELS[d]}</option>)}
          </select>

          <label htmlFor="catalog-tag">Oznaka</label>
          <select id="catalog-tag" value={filters.tag} onChange={e => update({ tag: e.target.value })}>
            <option value="">Sve oznake</option>
            {tags.map(t => <option key={t} value={t}>{t}</option>)}
          </select>

          <button type="button" onClick={() => setFilters(EMPTY_FILTERS)}>Poništi filtere</button>
        </div>

        <p className="catalog-count" aria-live="polite">Prikazano: {visible.length} od {labs.length}</p>

        {visible.length === 0
          ? <p>Nijedna laboratorija ne odgovara filterima.</p>
          : <div className="catalog-grid">{visible.map(lab => <LabCard key={lab.id} lab={lab} />)}</div>}
      </>}
    </section>
  )
}