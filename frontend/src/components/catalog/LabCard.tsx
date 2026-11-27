import { DIFFICULTY_LABELS, TYPE_LABELS, type CatalogLab, type MissingPrerequisite } from './catalogTypes'

type AccessStatus = 'open' | 'locked' | 'unknown'

const BADGE_LABELS: Record<AccessStatus, string> = {
  open: 'Otključano',
  locked: 'Zaključano',
  unknown: 'Stanje nepoznato',
}

function UnlockCondition({ missing }: { missing: MissingPrerequisite[] }) {
  if (missing.length === 0) {
    return <p className="catalog-lock" role="note">Laboratorija trenutno nije dostupna.</p>
  }
  return (
    <div className="catalog-lock" role="note">
      <p>Za otključavanje je potrebno završiti:</p>
      <ul>
        {missing.map(m => <li key={m.id}>{m.title}</li>)}
      </ul>
    </div>
  )
}

export default function LabCard({ lab }: { lab: CatalogLab }) {
  const status: AccessStatus = lab.access.unavailable ? 'unknown' : lab.access.isUnlocked ? 'open' : 'locked'

  return (
    <article className={status === 'open' ? 'catalog-card' : `catalog-card ${status}`}>
      <header className="catalog-card-head">
        <h3>{lab.title}</h3>
        <span className={`catalog-badge ${status}`}>{BADGE_LABELS[status]}</span>
      </header>

      <p className="catalog-objective">{lab.objective}</p>

      <dl className="catalog-meta">
        <div><dt>Tip</dt><dd>{TYPE_LABELS[lab.type]}</dd></div>
        <div><dt>Težina</dt><dd>{DIFFICULTY_LABELS[lab.difficulty]}</dd></div>
      </dl>

      {lab.tags.length > 0 && (
        <ul className="catalog-tags" aria-label="Oznake">
          {lab.tags.map(tag => <li key={tag}>{tag}</li>)}
        </ul>
      )}

      {status === 'locked' && <UnlockCondition missing={lab.access.missingPrerequisites} />}
      {status === 'unknown' && (
        <p className="catalog-lock" role="note">
          Stanje pristupa trenutno nije moguće učitati. Laboratorija nije dostupna dok server ne potvrdi pristup.
        </p>
      )}
    </article>
  )
}